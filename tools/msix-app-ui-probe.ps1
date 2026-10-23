# SPDX-License-Identifier: GPL-3.0-or-later
# Private disposable-CI installation of the real production UI. No OBS payload,
# recording, final WACK result or binary distribution is claimed by this probe.
param([Parameter(Mandatory)][string]$ManagedDirectory)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'Only disposable GitHub-hosted Windows CI may install this test package.'}
if($PSVersionTable.PSEdition -ne 'Desktop'){throw 'AppX deployment requires Windows PowerShell 5.1.'}
$root=Split-Path $PSScriptRoot -Parent
$managed=[IO.Path]::GetFullPath($ManagedDirectory)
if(-not $managed.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)-or -not(Test-Path -LiteralPath (Join-Path $managed 'coreclr.dll'))){throw 'Expected own self-contained managed publish.'}
$nonce=[Guid]::NewGuid().ToString('N').Substring(0,12)
$identity='Rilmo.AppUiProbe.'+$nonce;$publisher='CN=RilmoAppUiProbe-'+$nonce
$out=Join-Path $root ('artifacts/component-ci/msix-ui-'+$nonce);$stage=Join-Path $out 'stage'
$evidence=Join-Path $root 'artifacts/component-ci/evidence'
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'app'),$evidence|Out-Null
Get-ChildItem -LiteralPath $managed -File|Where-Object Extension -ne '.pdb'|Copy-Item -Destination (Join-Path $stage 'app')
Copy-Item -LiteralPath (Join-Path $root 'packaging/Assets') -Destination $stage -Recurse
$manifest=(Get-Content -LiteralPath (Join-Path $root 'packaging/AppxManifest.template.xml') -Raw).Replace('__IDENTITY_NAME__',$identity).Replace('__PUBLISHER__',$publisher).Replace('__PUBLISHER_DISPLAY_NAME__','Rilmo disposable UI test').Replace('__VERSION__','0.4.2.0').Replace('__MAX_VERSION_TESTED__','10.0.26100.0')
$manifest|Set-Content -LiteralPath (Join-Path $stage 'AppxManifest.xml') -Encoding utf8
$sdk=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
$makeappx=Get-ChildItem -Path (Join-Path $sdk '*/x64/makeappx.exe')|Sort-Object FullName -Descending|Select-Object -First 1
$signtool=Get-ChildItem -Path (Join-Path $sdk '*/x64/signtool.exe')|Sort-Object FullName -Descending|Select-Object -First 1
if(-not $makeappx -or -not $signtool){throw 'Microsoft packaging tools are missing.'}
$msix=Join-Path $out 'test-only.msix';$certificate=$null;$addedFramework=$null;$owned=$null
$result=[ordered]@{schemaVersion=1;sourceCommit=$env:GITHUB_SHA;osBuild=[Environment]::OSVersion.Version.Build;identity=$identity;testOnly=$true;packageInstalled=$false;packagedIdentityProved=$false;uiRenders=@();packageRemoved=$false;certificateRemoved=$false;frameworkRestored=$true;fullNativeRuntimeTested=$false;recordingTested=$false;wackPassed=$false;storeUploadAllowed=$false;error=$null}
try{
 $framework=Get-AppxPackage -Name Microsoft.VCLibs.140.00.UWPDesktop|Where-Object { $_.Architecture -eq 'X64' -and [version]$_.Version -ge [version]'14.0.33728.0' }|Select-Object -First 1
 if(-not $framework){
  $dependency=Join-Path $out 'Microsoft.VCLibs.x64.Desktop.appx'
  Invoke-WebRequest -UseBasicParsing -Uri 'https://aka.ms/Microsoft.VCLibs.x64.14.00.Desktop.appx' -OutFile $dependency
  if((Get-FileHash -LiteralPath $dependency -Algorithm SHA256).Hash -ine 'b56a9101f706f9d95f815f5b7fa6efbac972e86573d378b96a07cff5540c5961'){throw 'Desktop framework archive changed; review and update the pinned input before installation.'}
  $signature=Get-AuthenticodeSignature -LiteralPath $dependency
  if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){throw 'Microsoft framework download failed signature verification.'}
  Add-AppxPackage -Path $dependency
  $addedFramework=Get-AppxPackage -Name Microsoft.VCLibs.140.00.UWPDesktop|Where-Object { $_.Architecture -eq 'X64' -and [version]$_.Version -ge [version]'14.0.33728.0' }|Select-Object -First 1
  $framework=$addedFramework
 }
 if(-not $framework){throw 'Required desktop framework version is unavailable.'}
 $result.frameworkVersion=[string]$framework.Version
 & $makeappx.FullName pack /d $stage /p $msix /o 2>&1|Out-File -LiteralPath (Join-Path $evidence 'msix-app-ui-packaging.log') -Encoding utf8
 if($LASTEXITCODE -ne 0){throw 'Production UI test package validation failed.'}
 $certificate=New-SelfSignedCertificate -Type CodeSigningCert -Subject $publisher -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy NonExportable -NotAfter ([DateTime]::Now.AddHours(2))
 $cer=Join-Path $out 'test-only.cer';$null=Export-Certificate -Cert $certificate -FilePath $cer
 $null=Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
 & $signtool.FullName sign /fd SHA256 /sha1 $certificate.Thumbprint $msix 2>&1|Out-File -LiteralPath (Join-Path $evidence 'msix-app-ui-signing.log') -Encoding utf8
 if($LASTEXITCODE -ne 0){throw 'Temporary UI package signing failed.'}
 Add-AppxPackage -Path $msix
 $package=Get-AppxPackage -Name $identity
 if(-not $package){throw 'UI test package was not installed.'};$result.packageInstalled=$true
 Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class RilmoAppUiActivation {
 [ComImport,Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IActivation { [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)]string id,[MarshalAs(UnmanagedType.LPWStr)]string arguments,int flags,out uint pid); }
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern int GetPackageFullName(IntPtr process,ref uint length,StringBuilder name);
 public static int Launch(string id,string arguments,out uint pid) {
  object value=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")));
  try{return ((IActivation)value).ActivateApplication(id,arguments,6,out pid);}finally{Marshal.FinalReleaseComObject(value);}
 }
 public static string PackageName(IntPtr process) {uint length=0;if(GetPackageFullName(process,ref length,null)!=122)return null;var name=new StringBuilder((int)length);return GetPackageFullName(process,ref length,name)==0?name.ToString():null;}
}
'@
 foreach($page in @('record','studio','live','scene')){
  $image=Join-Path $evidence ('msix-app-'+$page+'.png');[uint32]$activationPid=0
  $hr=[RilmoAppUiActivation]::Launch(($package.PackageFamilyName+'!App'),('--render-ui "'+$image+'" 1440 900 '+$page),[ref]$activationPid)
  if($hr -lt 0 -or -not $activationPid){throw ('Packaged UI activation failed: '+('0x{0:x8}' -f $hr))}
  $owned=Get-Process -Id $activationPid -ErrorAction Stop
  $name=[RilmoAppUiActivation]::PackageName($owned.Handle)
  if($name -ne $package.PackageFullName){throw 'Rendered process did not have the installed test identity.'}
  $result.packagedIdentityProved=$true
  if(-not $owned.WaitForExit(30000)){throw 'Owned packaged UI renderer timed out.'}
  if($owned.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $image)){throw ('Packaged UI render failed: '+$page)}
  $result.uiRenders+=@{page=$page;passed=$true};$owned.Dispose();$owned=$null
 }
}catch{$result.error=$_.Exception.Message}
finally{
 if($owned){try{if(-not $owned.HasExited){$owned.Kill()};$owned.Dispose()}catch{}}
 $installed=Get-AppxPackage -Name $identity -ErrorAction SilentlyContinue
 if($installed){try{Remove-AppxPackage -Package $installed.PackageFullName}catch{$result.packageCleanupError=$_.Exception.Message}}
 $result.packageRemoved=-not [bool](Get-AppxPackage -Name $identity -ErrorAction SilentlyContinue)
 if($certificate){
  foreach($store in @('Cert:\LocalMachine\TrustedPeople','Cert:\CurrentUser\My')){ $path=$store+'\'+$certificate.Thumbprint;if(Test-Path -LiteralPath $path){try{Remove-Item -LiteralPath $path -Force}catch{$result.certificateCleanupError=$_.Exception.Message}} }
  $result.certificateRemoved=-not(Test-Path -LiteralPath ('Cert:\LocalMachine\TrustedPeople\'+$certificate.Thumbprint))-and -not(Test-Path -LiteralPath ('Cert:\CurrentUser\My\'+$certificate.Thumbprint))
 }
 if($addedFramework){try{Remove-AppxPackage -Package $addedFramework.PackageFullName}catch{$result.frameworkRestored=$false;$result.frameworkCleanupError=$_.Exception.Message}}
 $result|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $evidence 'msix-app-ui-probe.json') -Encoding utf8
}
if($result.error -or -not $result.packageRemoved -or -not $result.certificateRemoved -or -not $result.frameworkRestored){throw 'MSIX application UI/cleanup probe did not pass. Inspect retained evidence.'}
Write-Output 'PASS: production UI installed, had package identity, rendered four pages and was removed. Native recording and WACK were not tested.'
