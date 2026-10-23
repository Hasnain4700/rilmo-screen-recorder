param([switch]$IncludeBrowser,[switch]$StoreEdition,[string]$DesktopFrameworkTestDirectory,[switch]$NoAudio)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$localSdk = Join-Path $root '.tools\dotnet\dotnet.exe'
$dotnetPath = if (Test-Path -LiteralPath $localSdk) { $localSdk } elseif ($dotnetCommand) { $dotnetCommand.Source } else { throw 'The .NET 10 SDK is required.' }
$output = Join-Path $root $(if($StoreEdition){'artifacts\ui-smoke-store'}else{'artifacts\ui-smoke'})
if($DesktopFrameworkTestDirectory){
 if(-not $StoreEdition -or -not $IncludeBrowser){throw 'Framework compatibility requires the Store edition and browser integration test.'}
 $output=Join-Path $root 'artifacts\ui-smoke-store-framework'
}
& $dotnetPath build (Join-Path $root 'src\Rilmo.Recorder.csproj') -c Release -p:RilmoTestMode=true ("-p:RilmoStoreEdition="+$StoreEdition.IsPresent.ToString().ToLowerInvariant()) -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'UI smoke build failed.' }
& (Join-Path $root 'tools\stage-runtime.ps1') -AppDirectory $output
if($IncludeBrowser){ & (Join-Path $root 'tools\stage-browser-runtime.ps1') -AppDirectory $output }
if(-not $StoreEdition){ & (Join-Path $root 'tools\stage-virtual-camera.ps1') -AppDirectory $output }
if($DesktopFrameworkTestDirectory){
 # Private compatibility control only: these are test copies, not a distribution
 # strategy. Actual MSIX uses the package dependency, never these app-local DLLs.
 $substitutions=@();$verified=@{}
 foreach($file in Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object Name -match '^(msvcp140(?:_[a-z0-9_]+)?|vcruntime140(?:_1)?|concrt140)\.dll$'){
  $source=Join-Path $DesktopFrameworkTestDirectory $file.Name
  if(-not $verified.ContainsKey($file.Name)){
   $signature=Get-AuthenticodeSignature -LiteralPath $source
   if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){throw 'Framework compatibility input is not signed by Microsoft.'}
   $verified[$file.Name]=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
  }
  Copy-Item -LiteralPath $source -Destination $file.FullName -Force
  $substitutions+=,[ordered]@{path=[IO.Path]::GetRelativePath($output,$file.FullName);sha256=$verified[$file.Name];fileVersion=(Get-Item -LiteralPath $source).VersionInfo.FileVersion}
 }
 [ordered]@{testOnly=$true;packageGraphTested=$false;sourceDirectory=$DesktopFrameworkTestDirectory;substitutions=$substitutions}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $output 'framework-test-inputs.json') -Encoding utf8
}
$previousDotnetRoot = $env:DOTNET_ROOT
$resultDirectory = Join-Path $output ('results-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
try {
    $env:DOTNET_ROOT = Split-Path -Parent $dotnetPath
    $testProcess = Start-Process -FilePath (Join-Path $output 'Rilmo Recorder.exe') -ArgumentList ('--smoke-ui "' + $resultDirectory + '"'+$(if($NoAudio){' --no-audio'}else{''})) -WorkingDirectory $output -WindowStyle Hidden -PassThru
    if(-not $testProcess.WaitForExit(180000)){ $testProcess.Kill($true); throw ('Owned UI test exceeded three minutes: '+$resultDirectory) }
    $testExitCode = $testProcess.ExitCode
} finally { $env:DOTNET_ROOT = $previousDotnetRoot }
if ($testExitCode -ne 0) { throw ('UI smoke failed. See ' + (Join-Path $resultDirectory 'failure.txt')) }
Get-Content -LiteralPath (Join-Path $resultDirectory 'result.json')
