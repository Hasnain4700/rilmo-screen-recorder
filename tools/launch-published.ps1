[CmdletBinding()]
param([switch]$ResolveOnly)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$workspace = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$publishRoot = [IO.Path]::GetFullPath((Join-Path $workspace 'dist'))
$pointer = Join-Path $publishRoot 'latest-build.txt'
if (-not (Test-Path -LiteralPath $pointer -PathType Leaf)) { throw 'No successful standalone publish is selected. Run build.ps1 -Publish first.' }
foreach ($path in @($publishRoot, $pointer)) {
    if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The published-build pointer and dist directory must not be reparse points.' }
}
if ((Get-Item -LiteralPath $pointer).Length -gt 4096) { throw 'The published-build pointer is malformed.' }
$selected = (Get-Content -LiteralPath $pointer -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($selected) -or -not [IO.Path]::IsPathRooted($selected)) { throw 'The published-build pointer must contain one absolute path.' }
$directory = [IO.Path]::GetFullPath($selected).TrimEnd('\')
if ([IO.Path]::GetDirectoryName($directory) -ine $publishRoot) { throw 'The selected build must be a direct child directory of this workspace dist folder.' }
if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw 'The selected standalone build no longer exists. Run build.ps1 -Publish again.' }
if ((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The selected build must not be a junction or symbolic link.' }
foreach ($required in @('Rilmo Recorder.exe','Rilmo Recorder.dll','Rilmo Recorder.runtimeconfig.json','coreclr.dll','hostfxr.dll','obs.dll','obs-ffmpeg-mux.exe','runtime-staging.json')) {
    $path = Join-Path $directory $required
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "The selected standalone build is incomplete: $required" }
    if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "The selected build contains an unsupported linked file: $required" }
}
$runtimeConfig = Get-Content -LiteralPath (Join-Path $directory 'Rilmo Recorder.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtimeConfig.runtimeOptions.tfm -cne 'net10.0' -or $null -eq $runtimeConfig.runtimeOptions.PSObject.Properties['includedFrameworks']) { throw 'The selected build must be a self-contained .NET 10 publish.' }
$executable = Join-Path $directory 'Rilmo Recorder.exe'
if ($ResolveOnly) { Write-Output $executable; return }
Start-Process -FilePath $executable -WorkingDirectory $directory -WindowStyle Normal
