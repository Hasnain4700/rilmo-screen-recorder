param([switch]$IncludeBrowser)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$localSdk = Join-Path $root '.tools\dotnet\dotnet.exe'
$dotnetPath = if (Test-Path -LiteralPath $localSdk) { $localSdk } elseif ($dotnetCommand) { $dotnetCommand.Source } else { throw 'The .NET 10 SDK is required.' }
$output = Join-Path $root 'artifacts\ui-smoke'
& $dotnetPath build (Join-Path $root 'src\Rilmo.Recorder.csproj') -c Release -p:RilmoTestMode=true -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'UI smoke build failed.' }
& (Join-Path $root 'tools\stage-runtime.ps1') -AppDirectory $output
if($IncludeBrowser){ & (Join-Path $root 'tools\stage-browser-runtime.ps1') -AppDirectory $output }
& (Join-Path $root 'tools\stage-virtual-camera.ps1') -AppDirectory $output
$previousDotnetRoot = $env:DOTNET_ROOT
$resultDirectory = Join-Path $output ('results-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
try {
    $env:DOTNET_ROOT = Split-Path -Parent $dotnetPath
    $testProcess = Start-Process -FilePath (Join-Path $output 'Rilmo Recorder.exe') -ArgumentList ('--smoke-ui "' + $resultDirectory + '"') -WorkingDirectory $output -WindowStyle Hidden -PassThru
    if(-not $testProcess.WaitForExit(180000)){ $testProcess.Kill($true); throw ('Owned UI test exceeded three minutes: '+$resultDirectory) }
    $testExitCode = $testProcess.ExitCode
} finally { $env:DOTNET_ROOT = $previousDotnetRoot }
if ($testExitCode -ne 0) { throw ('UI smoke failed. See ' + (Join-Path $resultDirectory 'failure.txt')) }
Get-Content -LiteralPath (Join-Path $resultDirectory 'result.json')
