param(
    [string]$Version
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Workspace-local SDK acquisition only: no MSI, registry, PATH, or machine settings.
$rilmoWorkspace = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$rilmoGlobalPath = Join-Path $rilmoWorkspace 'global.json'
if (!(Test-Path -LiteralPath $rilmoGlobalPath)) { throw "Cannot select the workspace SDK: $rilmoGlobalPath is missing." }
$rilmoGlobal = Get-Content -LiteralPath $rilmoGlobalPath -Raw | ConvertFrom-Json
$rilmoPinnedVersion = [string]$rilmoGlobal.sdk.version
if ($rilmoPinnedVersion -notmatch '^10\.0\.\d+$' -or $rilmoGlobal.sdk.rollForward -ne 'latestPatch' -or $rilmoGlobal.sdk.allowPrerelease -ne $false) {
    throw 'This bootstrap requires a stable .NET 10.0 SDK in global.json with rollForward=latestPatch and allowPrerelease=false.'
}

# Install the repository pin by default, not the newest feature band in the channel.
# latestPatch permits only equal/newer patches within that same major/minor/feature band.
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $rilmoPinnedVersion }
$rilmoPinnedSdk = [version]$rilmoPinnedVersion
$rilmoRequestedSdk = $null
$rilmoFeatureBand = $rilmoPinnedSdk.Build - ($rilmoPinnedSdk.Build % 100)
$rilmoCompatible = $Version -match '^10\.0\.\d+$' -and [version]::TryParse($Version, [ref]$rilmoRequestedSdk)
if ($rilmoCompatible) {
    $rilmoCompatible = $rilmoRequestedSdk.Major -eq $rilmoPinnedSdk.Major -and
        $rilmoRequestedSdk.Minor -eq $rilmoPinnedSdk.Minor -and
        ($rilmoRequestedSdk.Build - ($rilmoRequestedSdk.Build % 100)) -eq $rilmoFeatureBand -and
        $rilmoRequestedSdk -ge $rilmoPinnedSdk
}
if (!$rilmoCompatible) {
    throw "SDK '$Version' is incompatible with global.json ($rilmoPinnedVersion, latestPatch). Choose a stable SDK >= $rilmoPinnedVersion in the $($rilmoPinnedSdk.Major).$($rilmoPinnedSdk.Minor).${rilmoFeatureBand} feature band; other major, minor, feature-band, or older versions are not accepted."
}

$rilmoTools = Join-Path $rilmoWorkspace '.tools'
$rilmoTarget = Join-Path $rilmoTools 'dotnet'
$rilmoDownloads = Join-Path $rilmoTools 'downloads'
New-Item -ItemType Directory -Force -Path $rilmoTools, $rilmoDownloads | Out-Null

$rilmoMetadataUrl = 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$rilmoMetadataPath = Join-Path $rilmoDownloads 'dotnet-10.0-releases.json'
Invoke-WebRequest -Uri $rilmoMetadataUrl -OutFile $rilmoMetadataPath
$rilmoMetadata = Get-Content -LiteralPath $rilmoMetadataPath -Raw | ConvertFrom-Json
if ($rilmoMetadata.'channel-version' -ne '10.0' -or $rilmoMetadata.'release-type' -ne 'lts') { throw 'Expected official .NET 10.0 LTS release metadata.' }

$rilmoSdkEntries = @($rilmoMetadata.releases | ForEach-Object { $_.sdk; $_.sdks })
$rilmoSdk = $rilmoSdkEntries | Where-Object { $_.version -eq $Version } | Select-Object -First 1
if ($null -eq $rilmoSdk) { throw "SDK $Version was not listed in official .NET 10 release metadata." }
$rilmoArchive = $rilmoSdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -eq 'dotnet-sdk-win-x64.zip' } | Select-Object -First 1
if ($null -eq $rilmoArchive) { throw "No Windows x64 ZIP is published for SDK $Version." }
$rilmoArchiveUri = [Uri]$rilmoArchive.url
if ($rilmoArchiveUri.Scheme -ne 'https' -or $rilmoArchiveUri.Host -notin @('builds.dotnet.microsoft.com', 'dotnetcli.azureedge.net')) { throw 'The SDK archive does not use an approved Microsoft distribution host.' }
if ($rilmoArchive.hash -notmatch '^[a-fA-F0-9]{128}$') { throw 'Official metadata did not contain a valid SHA-512 hash.' }
$rilmoExpectedHash = $rilmoArchive.hash.ToUpperInvariant()
$rilmoZip = Join-Path $rilmoDownloads "dotnet-sdk-$Version-win-x64.zip"
$rilmoPartial = $rilmoZip + '.partial'

if (Test-Path -LiteralPath $rilmoTarget) {
    throw "The target already exists: $rilmoTarget. This script never overwrites or deletes an existing SDK."
}
if (!(Test-Path -LiteralPath $rilmoZip)) {
    Write-Output "Downloading official .NET SDK $Version for Windows x64."
    $rilmoPartialVerified = (Test-Path -LiteralPath $rilmoPartial) -and (Get-FileHash -LiteralPath $rilmoPartial -Algorithm SHA512).Hash -eq $rilmoExpectedHash
    if (!$rilmoPartialVerified) {
        for ($rilmoAttempt = 1; $rilmoAttempt -le 3; $rilmoAttempt++) {
            try {
                Invoke-WebRequest -Uri $rilmoArchiveUri.AbsoluteUri -OutFile $rilmoPartial -Resume
                break
            }
            catch {
                if ($rilmoAttempt -eq 3) { throw }
                Write-Output "Download interrupted; resuming verified-source download (attempt $($rilmoAttempt + 1) of 3)."
                Start-Sleep -Seconds 2
            }
        }
    }
    if ((Get-FileHash -LiteralPath $rilmoPartial -Algorithm SHA512).Hash -ne $rilmoExpectedHash) { throw "SDK checksum verification failed; no files were extracted. Partial archive: $rilmoPartial" }
    Move-Item -LiteralPath $rilmoPartial -Destination $rilmoZip
}
$rilmoActualHash = (Get-FileHash -LiteralPath $rilmoZip -Algorithm SHA512).Hash
if ($rilmoActualHash -ne $rilmoExpectedHash) { throw "SDK checksum verification failed; no files were extracted. Archive: $rilmoZip" }
Write-Output "SHA-512 verified: $rilmoActualHash"

$rilmoStaging = Join-Path $rilmoTools ("dotnet-stage-$Version-" + [Guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $rilmoStaging) { throw 'The staging directory must be new and empty.' }
New-Item -ItemType Directory -Path $rilmoStaging | Out-Null
Expand-Archive -LiteralPath $rilmoZip -DestinationPath $rilmoStaging
if (!(Test-Path -LiteralPath (Join-Path $rilmoStaging 'dotnet.exe'))) { throw "Extracted SDK is missing dotnet.exe: $rilmoStaging" }

# Both resolved paths must remain direct children of the intended workspace tools directory.
foreach ($rilmoMovePath in @($rilmoStaging, $rilmoTarget)) {
    $rilmoResolvedMovePath = [IO.Path]::GetFullPath($rilmoMovePath)
    if ([IO.Path]::GetDirectoryName($rilmoResolvedMovePath) -ne [IO.Path]::GetFullPath($rilmoTools)) { throw 'SDK move escaped the workspace tools directory.' }
}
Move-Item -LiteralPath $rilmoStaging -Destination $rilmoTarget

$rilmoReceipt = [ordered]@{
    sdkVersion = $Version
    runtimeVersion = $rilmoSdk.'runtime-version'
    metadataUrl = $rilmoMetadataUrl
    archiveUrl = $rilmoArchiveUri.AbsoluteUri
    archivePath = $rilmoZip
    sha512 = $rilmoActualHash
    targetDirectory = $rilmoTarget
    verifiedAtUtc = [DateTime]::UtcNow.ToString('o')
}
$rilmoReceipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $rilmoTools 'dotnet-install-receipt.json') -Encoding utf8

# Limit any SDK first-run data to this workspace; restore process environment afterward.
$rilmoPreviousCliHome = $env:DOTNET_CLI_HOME
$rilmoPreviousTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$rilmoPreviousFirstRun = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
try {
    $env:DOTNET_CLI_HOME = Join-Path $rilmoTools 'dotnet-cli-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    & (Join-Path $rilmoTarget 'dotnet.exe') --info
    if ($LASTEXITCODE -ne 0) { throw 'The extracted dotnet --info check failed.' }
}
finally {
    $env:DOTNET_CLI_HOME = $rilmoPreviousCliHome
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $rilmoPreviousTelemetry
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $rilmoPreviousFirstRun
}
