$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dll = Join-Path $root 'src\bin\Release\net10.0-windows\win-x64\Rilmo Recorder.dll'
if (-not (Test-Path -LiteralPath $dll)) { & (Join-Path $root 'build.ps1') }
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$localSdk = Join-Path $root '.tools\dotnet\dotnet.exe'
$dotnetPath = if (Test-Path -LiteralPath $localSdk) { $localSdk } elseif ($dotnetCommand) { $dotnetCommand.Source } else { throw 'Install the .NET 10 Desktop Runtime or build the self-contained version.' }
$previousDotnetRoot = $env:DOTNET_ROOT
try {
    $env:DOTNET_ROOT = Split-Path -Parent $dotnetPath
    Start-Process -FilePath (Join-Path (Split-Path $dll) 'Rilmo Recorder.exe') -WorkingDirectory (Split-Path $dll) -WindowStyle Normal
} finally { $env:DOTNET_ROOT = $previousDotnetRoot }
