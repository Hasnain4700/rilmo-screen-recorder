[CmdletBinding()]
param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot 'release-config.example.json'),
    [switch]$CheckOnly,
    [string]$MakeAppxPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$workspacePath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$configFile = [System.IO.Path]::GetFullPath($ConfigPath)
if (-not (Test-Path -LiteralPath $configFile -PathType Leaf)) { throw "Release configuration does not exist: $configFile" }
$configRoot = Split-Path -Parent $configFile
$config = Get-Content -LiteralPath $configFile -Raw | ConvertFrom-Json
$blockers = [System.Collections.Generic.List[string]]::new()

function Read-Field($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}
function Test-Resolved([object]$Value) {
    return -not [string]::IsNullOrWhiteSpace([string]$Value) -and [string]$Value -notmatch '__|TODO|REPLACE_ME'
}
function Get-ConfigPath([string]$Name) {
    $value = Read-Field $config $Name
    if (-not (Test-Resolved $value)) { $blockers.Add("$Name is unresolved."); return $null }
    if ([System.IO.Path]::IsPathRooted([string]$value)) { return [System.IO.Path]::GetFullPath([string]$value) }
    return [System.IO.Path]::GetFullPath((Join-Path $configRoot ([string]$value)))
}
function Test-Https([object]$Value) {
    $parsed = $null
    return (Test-Resolved $Value) -and [Uri]::TryCreate([string]$Value, [UriKind]::Absolute, [ref]$parsed) -and $parsed.Scheme -eq 'https' -and $parsed.Host -notmatch '(^|\.)(example\.(com|org|net)|localhost)$'
}
function Get-ContainedPath([string]$Root, [string]$Relative) {
    if ([System.IO.Path]::IsPathRooted($Relative)) { throw "Inventory path must be relative: $Relative" }
    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $result = [System.IO.Path]::GetFullPath((Join-Path $rootFull $Relative))
    if (-not $result.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) { throw "Inventory path escapes runtime: $Relative" }
    return $result
}

foreach ($name in @('identityName', 'publisher', 'publisherDisplayName', 'supportContact')) {
    if (-not (Test-Resolved (Read-Field $config $name))) { $blockers.Add("$name must be supplied from the actual publisher/Partner Center account.") }
}
if ((Test-Resolved (Read-Field $config 'identityName')) -and $config.identityName -notmatch '^[A-Za-z0-9.-]{3,50}$') { $blockers.Add('identityName has an invalid MSIX identity format.') }
if ((Test-Resolved (Read-Field $config 'publisher')) -and $config.publisher -notmatch '^CN=') { $blockers.Add('publisher must be the exact Partner Center distinguished name, beginning CN=.') }
foreach ($name in @('version', 'maxVersionTested')) {
    $value = Read-Field $config $name
    if ([string]$value -notmatch '^\d+\.\d+\.\d+\.\d+$') { $blockers.Add("$name must have four numeric components.") }
    elseif (([string]$value).Split('.') | Where-Object { [long]$_ -gt 65535 }) { $blockers.Add("$name contains a component above 65535.") }
}
if ([string](Read-Field $config 'version') -match '^\d+\.\d+\.\d+\.\d+$' -and ([string]$config.version).Split('.')[3] -ne '0') { $blockers.Add('Store package revision (fourth version component) must be 0.') }
$testedVersion = $null
if ([version]::TryParse([string](Read-Field $config 'maxVersionTested'), [ref]$testedVersion) -and $testedVersion -lt [version]'10.0.19041.0') { $blockers.Add('maxVersionTested cannot be below the minimum supported Windows version 10.0.19041.0.') }
foreach ($name in @('sourceUrl', 'privacyPolicyUrl')) { if (-not (Test-Https (Read-Field $config $name))) { $blockers.Add("$name must be an actual public HTTPS release URL; placeholder domains are rejected.") } }

$appDirectory = Get-ConfigPath 'appPublishDirectory'
$runtimeDirectory = Get-ConfigPath 'runtimeDirectory'
$inventoryPath = Get-ConfigPath 'runtimeInventoryPath'
$sourceArchive = Get-ConfigPath 'sourceArchivePath'
foreach ($sourceDirectory in @($appDirectory, $runtimeDirectory)) {
    if ($sourceDirectory -and ($PSScriptRoot + '\').StartsWith(($sourceDirectory.TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase)) { $blockers.Add("Source directory contains packaging output and would cause recursive staging: $sourceDirectory") }
}
foreach ($required in @('Rilmo Recorder.exe', 'Rilmo Recorder.dll', 'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll')) {
    if (-not $appDirectory -or -not (Test-Path -LiteralPath (Join-Path $appDirectory $required) -PathType Leaf)) { $blockers.Add("Self-contained application output missing: $required. Publish the release app first.") }
}

# Reject stale or mixed publish folders before staging. A normal build uses
# frameworks; this package requires a self-contained publish with includedFrameworks.
$runtimeConfigPath = if ($appDirectory) { Join-Path $appDirectory 'Rilmo Recorder.runtimeconfig.json' } else { $null }
if (-not $runtimeConfigPath -or -not (Test-Path -LiteralPath $runtimeConfigPath -PathType Leaf)) {
    $blockers.Add('Application runtimeconfig is missing. Publish a self-contained .NET 10 release first.')
} else {
    try {
        if ((Get-Item -LiteralPath $runtimeConfigPath).Length -gt 65536) { throw 'Runtimeconfig exceeds the 64 KiB preflight limit.' }
        $runtimeConfigText = Get-Content -LiteralPath $runtimeConfigPath -Raw
        $runtimeConfig = $runtimeConfigText | ConvertFrom-Json -ErrorAction Stop
        if ($runtimeConfigText -notmatch '^\s*\{' -or $runtimeConfig -isnot [pscustomobject]) { throw 'Expected a JSON object.' }
        $runtimeOptionsProperty = $runtimeConfig.PSObject.Properties['runtimeOptions']
        if ($null -eq $runtimeOptionsProperty -or $runtimeOptionsProperty.Value -isnot [pscustomobject]) { throw 'runtimeOptions must be an object.' }
        $runtimeOptions = $runtimeOptionsProperty.Value
        $targetFrameworkProperty = $runtimeOptions.PSObject.Properties['tfm']
        if ($null -eq $targetFrameworkProperty -or $targetFrameworkProperty.Value -isnot [string] -or $targetFrameworkProperty.Value -cne 'net10.0') { $blockers.Add('Application runtimeconfig tfm must be net10.0; republish the current .NET 10 source.') }
        foreach ($field in @('framework', 'frameworks')) {
            if ($null -ne $runtimeOptions.PSObject.Properties[$field]) { $blockers.Add("Application runtimeconfig contains $field. A framework-dependent build cannot be packaged; publish with --self-contained true.") }
        }
        $includedProperty = $runtimeOptions.PSObject.Properties['includedFrameworks']
        if ($null -eq $includedProperty -or $includedProperty.Value -isnot [array] -or $includedProperty.Value.Count -eq 0 -or $includedProperty.Value.Count -gt 16) {
            throw 'includedFrameworks must be a nonempty JSON array with at most 16 entries.'
        }
        $frameworkNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($framework in $includedProperty.Value) {
            if ($framework -isnot [pscustomobject]) { throw 'Each includedFrameworks entry must be an object.' }
            $frameworkNameProperty = $framework.PSObject.Properties['name']
            $frameworkVersionProperty = $framework.PSObject.Properties['version']
            if ($null -eq $frameworkNameProperty -or $frameworkNameProperty.Value -isnot [string] -or [string]::IsNullOrWhiteSpace($frameworkNameProperty.Value)) { throw 'An included framework name is missing or malformed.' }
            if ($null -eq $frameworkVersionProperty -or $frameworkVersionProperty.Value -isnot [string]) { throw 'An included framework version is missing or malformed.' }
            $frameworkName = $frameworkNameProperty.Value
            $frameworkVersion = $frameworkVersionProperty.Value
            if (-not $frameworkNames.Add($frameworkName)) { $blockers.Add("Duplicate included framework: $frameworkName") }
            $parsedFrameworkVersion = $null
            if ($frameworkVersion -notmatch '^10\.0\.[0-9]+$' -or -not [version]::TryParse($frameworkVersion, [ref]$parsedFrameworkVersion)) { $blockers.Add("Included framework $frameworkName must declare a stable 10.0.x version; found '$frameworkVersion'.") }
        }
        foreach ($frameworkName in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
            if (-not $frameworkNames.Contains($frameworkName)) { $blockers.Add("Self-contained .NET 10 runtimeconfig is missing included framework $frameworkName.") }
        }
    } catch { $blockers.Add("Application runtimeconfig is unreadable or malformed: $($_.Exception.Message)") }
}
foreach ($nativeRuntime in @('hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll')) {
    if (-not $appDirectory) { continue }
    $nativeRuntimePath = Join-Path $appDirectory $nativeRuntime
    if (-not (Test-Path -LiteralPath $nativeRuntimePath -PathType Leaf)) { continue } # Missing files were reported above.
    try {
        $nativeVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($nativeRuntimePath)
        if ($nativeVersion.FileMajorPart -ne 10 -or $nativeVersion.FileMinorPart -ne 0 -or $nativeVersion.ProductMajorPart -ne 10 -or $nativeVersion.ProductMinorPart -ne 0) {
            $blockers.Add("Native runtime $nativeRuntime must be .NET 10.0; found file version '$($nativeVersion.FileVersion)' and product version '$($nativeVersion.ProductVersion)'. Republish into a clean directory.")
        }
    } catch { $blockers.Add("Cannot read native runtime version for ${nativeRuntime}: $($_.Exception.Message)") }
}

if (-not $runtimeDirectory -or -not (Test-Path -LiteralPath $runtimeDirectory -PathType Container)) {
    $blockers.Add('Pinned redistributable OBS runtime is missing. Installed OBS is development-only; this script never copies it.')
} else {
    if ((Get-Item -LiteralPath $runtimeDirectory).Attributes -band [System.IO.FileAttributes]::ReparsePoint) { $blockers.Add('Runtime root must be an audited real directory, not a junction or symbolic link.') }
    foreach ($entry in Get-ChildItem -LiteralPath $runtimeDirectory -Recurse) { if ($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { $blockers.Add("Runtime inventory must not contain junctions or symbolic links: $($entry.FullName)") } }
    foreach ($installedRoot in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if ($installedRoot -and $runtimeDirectory.StartsWith(([System.IO.Path]::GetFullPath($installedRoot).TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase)) { $blockers.Add('runtimeDirectory points into Program Files; supply the audited redistributable runtime build instead.') }
    }
    foreach ($required in @('bin\64bit\obs.dll', 'bin\64bit\libobs-d3d11.dll', 'bin\64bit\obs-ffmpeg-mux.exe', 'obs-plugins\64bit\win-capture.dll', 'obs-plugins\64bit\win-wasapi.dll', 'obs-plugins\64bit\obs-ffmpeg.dll', 'obs-plugins\64bit\obs-x264.dll', 'data\libobs')) {
        if (-not (Test-Path -LiteralPath (Join-Path $runtimeDirectory $required))) { $blockers.Add("Required runtime component missing: $required") }
    }
}

if (-not $inventoryPath -or -not (Test-Path -LiteralPath $inventoryPath -PathType Leaf)) { $blockers.Add('Runtime inventory is missing: record every runtime file hash and component license/source before packaging.') }
elseif ($runtimeDirectory -and (Test-Path -LiteralPath $runtimeDirectory -PathType Container)) {
    $inventoryText = Get-Content -LiteralPath $inventoryPath -Raw
    if ($inventoryText -match '__|REPLACE_ME') { $blockers.Add('Runtime inventory still contains placeholders.') }
    $inventory = $inventoryText | ConvertFrom-Json
    if ((Read-Field $inventory 'runtimeVersion') -ne (Read-Field $config 'runtimeVersion') -or (Read-Field $config 'runtimeVersion') -ne '32.2.2') { $blockers.Add('Runtime inventory and engine ABI baseline must agree on OBS 32.2.2; revalidate the engine before changing this gate.') }
    if ((Read-Field $inventory 'correspondingSourceComplete') -ne $true -or (Read-Field $inventory 'publicDistributionApproved') -ne $true) { $blockers.Add('Bundled runtime is a local preview: complete the corresponding-source and redistribution reviews before creating a public Store candidate.') }
    $embeddedInventory = Join-Path $runtimeDirectory 'runtime-inventory.json'
    if (-not (Test-Path -LiteralPath $embeddedInventory -PathType Leaf) -or (Get-FileHash -LiteralPath $embeddedInventory -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $inventoryPath -Algorithm SHA256).Hash) { $blockers.Add('Runtime directory must contain runtime-inventory.json identical to the configured audit inventory.') }
    if (-not (Test-Resolved (Read-Field $inventory 'buildRevision'))) { $blockers.Add('Runtime source/build revision is unresolved.') }
    $listed = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($item in @(Read-Field $inventory 'files')) {
        $relative = [string](Read-Field $item 'path')
        if ([string]::IsNullOrWhiteSpace($relative)) { $blockers.Add('Runtime inventory has a missing file path.'); continue }
        $file = Get-ContainedPath $runtimeDirectory $relative
        if (-not $listed.Add($file)) { $blockers.Add("Duplicate runtime inventory entry: $relative") }
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { $blockers.Add("Inventory file missing: $relative") }
        elseif ([string](Read-Field $item 'sha256') -notmatch '^[A-Fa-f0-9]{64}$' -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $item.sha256) { $blockers.Add("Runtime SHA-256 mismatch: $relative") }
    }
    foreach ($file in Get-ChildItem -LiteralPath $runtimeDirectory -File -Recurse) { if ($file.FullName -ne $embeddedInventory -and -not $listed.Contains($file.FullName)) { $blockers.Add("Runtime file is absent from inventory: $($file.FullName)") } }
    $components = @(Read-Field $inventory 'components')
    if ($components.Count -eq 0 -or $null -eq $components[0]) { $blockers.Add('Runtime component/license inventory is empty.') }
    foreach ($component in $components) {
        foreach ($field in @('name', 'version', 'license')) { if (-not (Test-Resolved (Read-Field $component $field))) { $blockers.Add("Runtime component has unresolved $field.") } }
        if (-not (Test-Https (Read-Field $component 'sourceUrl'))) { $blockers.Add('Runtime component needs an exact HTTPS source location.') }
        $licenseFiles = @(Read-Field $component 'licenseFiles')
        if ($licenseFiles.Count -eq 0 -or $null -eq $licenseFiles[0]) { $blockers.Add('Runtime component has no license text file.') }
        foreach ($license in $licenseFiles) { if ($license -and -not (Test-Path -LiteralPath (Get-ContainedPath $runtimeDirectory $license) -PathType Leaf)) { $blockers.Add("Missing component license: $license") } }
    }
}

if (-not $sourceArchive -or -not (Test-Path -LiteralPath $sourceArchive -PathType Leaf)) { $blockers.Add('Complete corresponding-source archive is missing.') }
elseif ([string](Read-Field $config 'sourceArchiveSha256') -notmatch '^[A-Fa-f0-9]{64}$' -or (Get-FileHash -LiteralPath $sourceArchive -Algorithm SHA256).Hash -ne $config.sourceArchiveSha256) { $blockers.Add('Corresponding-source SHA-256 is missing or does not match.') }
else {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $sourceZip = $null
    try {
        $sourceZip = [System.IO.Compression.ZipFile]::OpenRead($sourceArchive)
        $names = @($sourceZip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        foreach ($required in @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'build.ps1', 'src/Rilmo.Recorder.csproj', 'release-source-manifest.json')) {
            if ($names -notcontains $required) { $blockers.Add("Corresponding-source ZIP is missing root-relative entry: $required") }
        }
        $sourceManifests=@($sourceZip.Entries | Where-Object { $_.FullName -eq 'release-source-manifest.json' })
        if($sourceManifests.Count -ne 1 -or $sourceManifests[0].Length -gt 4MB){$blockers.Add('Source archive must have exactly one bounded release-source-manifest.json (maximum 4 MiB).')}
        else {
            $reader=[IO.StreamReader]::new($sourceManifests[0].Open())
            try{$sourceManifest=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}
            if((Read-Field $sourceManifest 'correspondingSourceComplete') -ne $true -or (Read-Field $sourceManifest 'publicDistributionApproved') -ne $true){$blockers.Add('Source archive is a review bundle, not an approved complete corresponding-source release.')}
        }
    } catch { $blockers.Add("Corresponding-source archive is not a readable ZIP: $($_.Exception.Message)") }
    finally { if ($sourceZip) { $sourceZip.Dispose() } }
}

$evidence = Read-Field $config 'releaseEvidence'
if ($appDirectory -and (Test-Path -LiteralPath (Join-Path $appDirectory 'rilmo-virtual-camera64.dll'))) {
    $blockers.Add('The published app includes the external Rilmo DirectShow camera. This MSIX scaffold does not install/remove its machine COM device or support the elevated setup lifecycle. Implement and validate that installation model, or prepare a compliant signed EXE/MSI Store installer; do not ship a broken setup button in this MSIX.')
    $cameraManifestPath=Join-Path $appDirectory 'virtual-camera/source-provenance.json'
    if(-not(Test-Path -LiteralPath $cameraManifestPath)){ $blockers.Add('Camera build/source provenance is missing.') }
    else {
        $cameraManifest=Get-Content -LiteralPath $cameraManifestPath -Raw | ConvertFrom-Json
        if((Read-Field $cameraManifest 'publicDistributionApproved') -ne $true -or (Read-Field $cameraManifest 'correspondingSourceReleaseComplete') -ne $true){$blockers.Add('Virtual camera source/static-runtime/signing and redistribution review is incomplete.')}
    }
}
if ($appDirectory -and (Test-Path -LiteralPath (Join-Path $appDirectory 'runtime/browser'))) {
    $browserManifestPath=Join-Path $appDirectory 'runtime/browser/browser-runtime-inventory.json'
    if(-not(Test-Path -LiteralPath $browserManifestPath)){ $blockers.Add('Browser runtime inventory is missing.') }
    else {
        $browserManifest=Get-Content -LiteralPath $browserManifestPath -Raw | ConvertFrom-Json
        if((Read-Field $browserManifest 'correspondingSourceComplete') -ne $true -or (Read-Field $browserManifest 'publicDistributionApproved') -ne $true){$blockers.Add('Browser CEF/Chromium/Qt source, notices, and redistribution review is incomplete.')}
        foreach($entry in @(Read-Field $browserManifest 'files')){
            $file=Get-ContainedPath (Join-Path $appDirectory 'runtime/browser') ([string](Read-Field $entry 'path'))
            if(-not(Test-Path -LiteralPath $file -PathType Leaf) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine (Read-Field $entry 'sha256')){$blockers.Add('Browser runtime inventory hash/file mismatch: '+(Read-Field $entry 'path'))}
        }
    }
}
if ((Read-Field $evidence 'runtimeResolution') -ne 'package-relative') { $blockers.Add('Review the package-relative engine: native DLLs/helpers beside the application, modules/data in app\runtime\obs. Record proof that no installed OBS or environment override is used.') }
foreach ($name in @('runtimeAndHelperReview', 'sourceComplianceReview', 'privacyAndCapabilitiesReview', 'networkDataFlowReview', 'cleanMachineRecordingTest')) {
    $relative = Read-Field $evidence $name
    if (-not (Test-Resolved $relative)) { $blockers.Add("Release evidence is unresolved: $name"); continue }
    $path = [System.IO.Path]::GetFullPath((Join-Path $configRoot ([string]$relative)))
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $blockers.Add("Release evidence missing: $name ($path)") }
    elseif ((Get-Content -LiteralPath $path -Raw) -match '__|TODO|REPLACE_ME') { $blockers.Add("Release evidence contains unresolved placeholders: $name") }
}
foreach ($required in @('LICENSE', 'THIRD-PARTY-NOTICES.md')) { if (-not (Test-Path -LiteralPath (Join-Path $workspacePath $required) -PathType Leaf)) { $blockers.Add("Project license artifact missing: $required") } }

if ($blockers.Count -gt 0) { throw ("MSIX release blocked; no package was created. Resolve these gates:`n - " + ($blockers -join "`n - ")) }
if ($CheckOnly) { Write-Output 'Static release gates passed. No files packaged, signed, installed, or published. Human review and actual machine tests remain required.'; return }

if (-not $MakeAppxPath) {
    $command = Get-Command makeappx.exe -ErrorAction SilentlyContinue
    if ($command) { $MakeAppxPath = $command.Source }
    else {
        $candidate = 'C:\REV\tools\store-sdk\10.0.26100.9169\bin\10.0.26100.0\x64\makeappx.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { $MakeAppxPath = $candidate }
    }
}
if (-not $MakeAppxPath -or -not (Test-Path -LiteralPath $MakeAppxPath -PathType Leaf)) { throw 'MakeAppx.exe is unavailable. Install the Windows SDK or supply -MakeAppxPath.' }

& (Join-Path $PSScriptRoot 'generate-assets.ps1') | Out-Null
$outputRoot = Join-Path $PSScriptRoot ('out\' + $config.version + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$stage = Join-Path $outputRoot 'stage'
$stageApp = Join-Path $stage 'app'
$stageRuntime = Join-Path $stageApp 'runtime\obs'
foreach ($path in @($stageApp, $stageRuntime)) { [System.IO.Directory]::CreateDirectory($path) | Out-Null }
Get-ChildItem -LiteralPath $appDirectory | Copy-Item -Destination $stageApp -Recurse
& (Join-Path $workspacePath 'tools\stage-runtime.ps1') -AppDirectory $stageApp -RuntimeDirectory $runtimeDirectory
& (Join-Path $workspacePath 'tools\configure-msix-framework.ps1') -AppDirectory $stageApp
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assets') -Destination $stage -Recurse
foreach ($file in @('LICENSE', 'THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $workspacePath $file) -Destination $stageApp }
Copy-Item -LiteralPath $inventoryPath -Destination (Join-Path $stageApp 'runtime-inventory.json')
$releaseLinks = "Source: $($config.sourceUrl)`nSource archive SHA-256: $($config.sourceArchiveSha256)`nPrivacy: $($config.privacyPolicyUrl)`nSupport: $($config.supportContact)`n"
[System.IO.File]::WriteAllText((Join-Path $stageApp 'RELEASE-LINKS.txt'), $releaseLinks, [System.Text.UTF8Encoding]::new($false))
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.template.xml') -Raw
foreach ($replacement in @(
    @('__IDENTITY_NAME__', $config.identityName), @('__PUBLISHER__', $config.publisher),
    @('__PUBLISHER_DISPLAY_NAME__', $config.publisherDisplayName), @('__VERSION__', $config.version),
    @('__MAX_VERSION_TESTED__', $config.maxVersionTested)
)) { $manifest = $manifest.Replace($replacement[0], [System.Security.SecurityElement]::Escape([string]$replacement[1])) }
if ($manifest -match '__[A-Z_]+__') { throw 'Unresolved manifest token; packaging stopped.' }
[xml]$parsedManifest = $manifest
[System.IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'), $manifest, [System.Text.UTF8Encoding]::new($false))
$package = Join-Path $outputRoot ('RilmoRecorder-' + $config.version + '-x64-unsigned.msix')
& $MakeAppxPath pack /d $stage /p $package /o
if ($LASTEXITCODE -ne 0) { throw "MakeAppx validation/packing failed with exit code $LASTEXITCODE. Staging preserved at $stage" }
Write-Output "Unsigned candidate created: $package"
Write-Output 'Not signed, installed, published, or certified. Complete WACK, clean-machine testing, source-delivery review, and Partner Center review before release.'
