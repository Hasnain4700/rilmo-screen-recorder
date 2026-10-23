param([switch]$Publish, [string]$RuntimeDirectory, [switch]$StoreEdition)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$localSdk = Join-Path $root '.tools\dotnet\dotnet.exe'
$dotnetPath = if (Test-Path -LiteralPath $localSdk) { $localSdk } elseif ($dotnetCommand) { $dotnetCommand.Source } else { throw 'Install the .NET 10 SDK (tools/install-dotnet.ps1), then run build.ps1 again.' }
$projectPath = Join-Path $root 'src\Rilmo.Recorder.csproj'
if (-not $RuntimeDirectory) { $RuntimeDirectory = Join-Path $root 'vendor\obs\32.2.2' }
if (-not (Test-Path -LiteralPath (Join-Path $RuntimeDirectory 'runtime-inventory.json'))) { throw 'Prepare the bundled runtime first with tools/prepare-runtime.ps1. See docs/standalone-runtime.md for official acquisition and the explicit local fallback.' }
$publishRoot = Join-Path $root 'dist'
$finalPublishPath = Join-Path $publishRoot 'Rilmo Recorder'
if ($Publish) {
    $runningFromPublish = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and $_.Path.StartsWith($finalPublishPath + '\', [StringComparison]::OrdinalIgnoreCase) } catch { $false }
    })
    if ($runningFromPublish.Count -gt 0) {
        $finalPublishPath = Join-Path $publishRoot ('Rilmo Recorder standalone-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
        Write-Output ('Existing app/helpers are running; preserving that folder and publishing separately: ' + $finalPublishPath)
    }
}
$sdkEnvironmentNames = @('DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_GENERATE_ASPNET_CERTIFICATE', 'DOTNET_NOLOGO')
$previousSdkEnvironment = @{}
foreach ($name in $sdkEnvironmentNames) { $previousSdkEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    [Environment]::SetEnvironmentVariable('DOTNET_CLI_TELEMETRY_OPTOUT', '1', 'Process')
    [Environment]::SetEnvironmentVariable('DOTNET_GENERATE_ASPNET_CERTIFICATE', 'false', 'Process')
    [Environment]::SetEnvironmentVariable('DOTNET_NOLOGO', '1', 'Process')
    if ($Publish) {
        $outputPath = Join-Path $publishRoot ('Rilmo Recorder.stage-' + [Guid]::NewGuid().ToString('N'))
        & $dotnetPath publish $projectPath -c Release -p:RilmoTestMode=false ("-p:RilmoStoreEdition="+$StoreEdition.IsPresent.ToString().ToLowerInvariant()) -r win-x64 --self-contained true -o $outputPath
    } else {
        & $dotnetPath build $projectPath -c Release -p:RilmoTestMode=false ("-p:RilmoStoreEdition="+$StoreEdition.IsPresent.ToString().ToLowerInvariant())
        $outputPath = Join-Path $root 'src\bin\Release\net10.0-windows\win-x64'
    }
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
} finally {
    foreach ($name in $sdkEnvironmentNames) { [Environment]::SetEnvironmentVariable($name, $previousSdkEnvironment[$name], 'Process') }
}
& (Join-Path $root 'tools\stage-runtime.ps1') -AppDirectory $outputPath -RuntimeDirectory $RuntimeDirectory
& (Join-Path $root 'tools\stage-browser-runtime.ps1') -AppDirectory $outputPath
if(-not $StoreEdition){ & (Join-Path $root 'tools\stage-virtual-camera.ps1') -AppDirectory $outputPath }
if ($Publish) {
    foreach ($name in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $outputPath $name) -Force }
    Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $outputPath -Recurse -Force
    $supplementalNotices = Join-Path $root 'third_party\supplemental'
    if (Test-Path -LiteralPath $supplementalNotices) {
        New-Item -ItemType Directory -Path (Join-Path $outputPath 'third_party') -Force | Out-Null
        Copy-Item -LiteralPath $supplementalNotices -Destination (Join-Path $outputPath 'third_party') -Recurse -Force
    }
    $installerNotices = Join-Path $root 'third_party\installer'
    if (Test-Path -LiteralPath $installerNotices) {
        New-Item -ItemType Directory -Path (Join-Path $outputPath 'third_party') -Force | Out-Null
        Copy-Item -LiteralPath $installerNotices -Destination (Join-Path $outputPath 'third_party') -Recurse -Force
    }
    Set-Content -LiteralPath (Join-Path $outputPath 'PREVIEW-NOTICE.txt') -Encoding UTF8 -Value 'Standalone local preview with bundled .NET 10 and selected native OBS 32.2.2 components. It does not use an installed OBS application or an environment override. Full OBS feature parity, clean-machine certification and Microsoft Store release remain incomplete. Runtime provenance and unfinished source/license obligations are recorded in runtime/obs/provenance.json. This folder is not an approved public distribution package.'
    # Only replace the user-facing publish folder after build and native staging
    # succeed. Preserve the previous working distribution without deleting it.
    foreach ($candidate in @($outputPath, $finalPublishPath)) {
        $resolved = [IO.Path]::GetFullPath($candidate)
        if ([IO.Path]::GetDirectoryName($resolved) -ine [IO.Path]::GetFullPath($publishRoot)) { throw 'Publish move escaped the intended dist directory.' }
    }
    $previousPublish = $null
    if (Test-Path -LiteralPath $finalPublishPath) {
        $previousPublish = Join-Path $publishRoot ('Rilmo Recorder.previous-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
        Move-Item -LiteralPath $finalPublishPath -Destination $previousPublish
    }
    try { Move-Item -LiteralPath $outputPath -Destination $finalPublishPath }
    catch {
        if ($previousPublish -and -not (Test-Path -LiteralPath $finalPublishPath)) { Move-Item -LiteralPath $previousPublish -Destination $finalPublishPath }
        throw
    }
    $outputPath = $finalPublishPath
    if ($previousPublish) { Write-Output ('Previous build preserved: ' + $previousPublish) }
    $latestBuildFile = Join-Path $publishRoot 'latest-build.txt'
    if ((Test-Path -LiteralPath $latestBuildFile) -and ((Get-Item -LiteralPath $latestBuildFile).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Latest-build pointer cannot be a reparse point.' }
    $latestBuildTemporary = Join-Path $publishRoot ('latest-build-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    [IO.File]::WriteAllText($latestBuildTemporary, [IO.Path]::GetFullPath($outputPath) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $latestBuildFile) {
        # Windows PowerShell marshals $null to an empty string for this overload.
        # A real unique backup path also preserves the previous launch selection.
        $previousPointer = Join-Path $publishRoot ('latest-build.previous-' + [Guid]::NewGuid().ToString('N') + '.txt')
        [IO.File]::Replace($latestBuildTemporary, $latestBuildFile, $previousPointer)
    }
    else { [IO.File]::Move($latestBuildTemporary, $latestBuildFile) }
}
Write-Output ('Build ready: ' + $outputPath)
