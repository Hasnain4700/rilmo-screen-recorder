# SPDX-License-Identifier: GPL-3.0-or-later
# Disposable runner only. Download pinned inputs and retain evidence, never MSIX,
# native binaries, recordings, certificates or signing keys as public artifacts.
param([Parameter(Mandatory)][string]$DotnetPath)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'Only disposable GitHub-hosted Windows may run this acquisition/install test.'}
$root=Split-Path $PSScriptRoot -Parent
$work=Join-Path $root 'artifacts/component-ci';$evidence=Join-Path $work 'evidence'
$archive=Join-Path $work 'Microsoft.VCLibs.x64.Desktop.appx'
Invoke-WebRequest -Uri 'https://aka.ms/Microsoft.VCLibs.x64.14.00.Desktop.appx' -OutFile $archive
if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ine 'b56a9101f706f9d95f815f5b7fa6efbac972e86573d378b96a07cff5540c5961'){throw 'Microsoft desktop framework pin changed.'}
$signature=Get-AuthenticodeSignature -LiteralPath $archive
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){throw 'Microsoft desktop framework signature is invalid.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$frameworkFiles=Join-Path $work 'framework-files'
[IO.Compression.ZipFile]::ExtractToDirectory($archive,$frameworkFiles)
# These copies are acquisition/test inputs. The MSIX transformation removes
# them and resolves the declared installed Microsoft framework at runtime.
& (Join-Path $PSScriptRoot 'prepare-runtime.ps1') -NoDownload:$false -VcRuntimeDirectory $frameworkFiles
& (Join-Path $PSScriptRoot 'fetch-browser-release-inputs.ps1')
& (Join-Path $PSScriptRoot 'prepare-browser-runtime.ps1') -VcRuntimeDirectory $frameworkFiles
$managed=Join-Path $work 'native-test-managed'
& $DotnetPath publish (Join-Path $root 'src/Rilmo.Recorder.csproj') -c Release -r win-x64 --self-contained true -p:RilmoTestMode=true -p:RilmoStoreEdition=true -p:RestoreLockedMode=true -o $managed --nologo
if($LASTEXITCODE -ne 0){throw 'Installed integration test publish failed.'}
& (Join-Path $PSScriptRoot 'stage-runtime.ps1') -AppDirectory $managed
& (Join-Path $PSScriptRoot 'stage-browser-runtime.ps1') -AppDirectory $managed
$sdk=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
$sdkArch=if($env:RUNNER_ARCH -eq 'ARM64'){'arm64'}else{'x64'}
$makeappx=Get-ChildItem -Path (Join-Path $sdk ('*/'+$sdkArch+'/makeappx.exe'))|Sort-Object FullName -Descending|Select-Object -First 1
$signtool=Get-ChildItem -Path (Join-Path $sdk ('*/'+$sdkArch+'/signtool.exe'))|Sort-Object FullName -Descending|Select-Object -First 1
if(-not $makeappx -or -not $signtool){throw 'Microsoft SDK packaging tools are unavailable.'}
& (Join-Path $PSScriptRoot 'build-msix-development.ps1') -PublishedDirectory $managed -MakeAppxPath $makeappx.FullName
$candidate=Get-ChildItem -LiteralPath (Join-Path $root 'artifacts') -Directory -Filter 'msix-development-20*'|Sort-Object Name -Descending|Select-Object -First 1
$package=Get-Content -LiteralPath (Join-Path $candidate.FullName 'result.json') -Raw|ConvertFrom-Json
try{
 & powershell.exe -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'test-msix-native-ci-install.ps1') -PackagePath $package.package -ExpectedSha256 $package.sha256 -FrameworkPath $archive -SignToolPath $signtool.FullName
 if($LASTEXITCODE -ne 0){throw 'Installed native MSIX test did not pass.'}
}finally{
 $run=Get-ChildItem -LiteralPath (Join-Path $root 'artifacts') -Directory -Filter 'msix-native-local-*'|Sort-Object Name -Descending|Select-Object -First 1
 if($run){
  foreach($name in @('result.json','ui-smoke/result.json','ui-smoke/failure.txt')){
   $file=Join-Path $run.FullName $name
   if(Test-Path -LiteralPath $file){Copy-Item -LiteralPath $file -Destination (Join-Path $evidence ('native-msix-'+$name.Replace('/','-')))}
  }
  foreach($name in @('record-idle.png','studio.png','browser-preview-0.png','browser-preview-1.png')){
   $file=Join-Path $run.FullName ('ui-smoke/'+$name)
   if(Test-Path -LiteralPath $file){Copy-Item -LiteralPath $file -Destination (Join-Path $evidence ('native-msix-'+$name))}
  }
 }
}
