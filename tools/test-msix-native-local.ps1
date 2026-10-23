# SPDX-License-Identifier: GPL-3.0-or-later
# Explicit local test only. Install an independent development identity, then
# remove its package and the exact temporary leaf certificate/private key.
[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$PackagePath,
 [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedSha256,
 [switch]$AcknowledgeTemporaryTestCertificate,
 [string]$SignToolPath
)
$ErrorActionPreference='Stop'
if(-not $AcknowledgeTemporaryTestCertificate){throw 'Temporary test certificate approval is required.'}
if($PSVersionTable.PSEdition -ne 'Desktop'){throw 'Use Windows PowerShell 5.1 for AppX deployment.'}
$principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Run in the current user session with administrator elevation.'}
$root=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$inputFile=[IO.Path]::GetFullPath($PackagePath)
if(-not $inputFile.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Only private workspace packages may be tested.'}
if((Get-FileHash -LiteralPath $inputFile -Algorithm SHA256).Hash -ine $ExpectedSha256){throw 'The reviewed test package changed.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($inputFile)
try{
 $reader=[IO.StreamReader]::new($zip.GetEntry('AppxManifest.xml').Open())
 try{[xml]$manifest=$reader.ReadToEnd()}finally{$reader.Dispose()}
 if($manifest.Package.Identity.Name -cne 'Rilmo.Development.ScreenRecorder' -or $manifest.Package.Identity.Publisher -cne 'CN=RilmoDevelopmentOnly'){throw 'The test identity/publisher must be independent of the Store product.'}
 if(@($zip.Entries|Where-Object FullName -match '(?i)(^|/)(obs-virtualcam-module(?:32|64)\.dll|virtualcam-(?:un)?install\.bat|rilmo-virtual-camera(?:32|64)\.dll)$').Count){throw 'Camera registration files must not be present.'}
}finally{$zip.Dispose()}
$identity='Rilmo.Development.ScreenRecorder'
if(Get-AppxPackage -Name $identity -ErrorAction SilentlyContinue){throw 'An existing development package must be preserved; inspect before testing.'}
$framework=Get-AppxPackage -Name Microsoft.VCLibs.140.00.UWPDesktop|Where-Object { $_.Architecture -eq 'X64' -and [version]$_.Version -ge [version]'14.0.33728.0' }|Select-Object -First 1
if(-not $framework){throw 'The required existing Microsoft desktop framework is unavailable; this test does not install frameworks.'}
$out=Join-Path $root ('artifacts/msix-native-local-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $out|Out-Null
$signtool=$SignToolPath
if(-not $signtool){$signtool=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/App Certification Kit/signtool.exe'}
if(-not(Test-Path -LiteralPath $signtool)){throw 'Microsoft SignTool is required.'}
$result=[ordered]@{schemaVersion=1;startedUtc=[DateTime]::UtcNow.ToString('o');unsignedPackageSha256=$ExpectedSha256;identity=$identity;integrationTestBuild=$true;productionPackageQualified=$false;packageInstalled=$false;packagedIdentityProved=$false;frameworkVersion=[string]$framework.Version;frameworkModules=@();recordingTestPassed=$false;packageRemoved=$false;certificateRemoved=$false;storeUploadAllowed=$false;error=$null}
$certificate=$null;$owned=$null;$package=$null
try{
 $signed=Join-Path $out 'private-test.msix';Copy-Item -LiteralPath $inputFile -Destination $signed
 $certificate=New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=RilmoDevelopmentOnly' -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy NonExportable -NotAfter ([DateTime]::Now.AddHours(2))
 $result.certificateThumbprint=$certificate.Thumbprint
 $cer=Join-Path $out 'test-only-public.cer';$null=Export-Certificate -Cert $certificate -FilePath $cer
 $null=Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
 & $signtool sign /fd SHA256 /sha1 $certificate.Thumbprint $signed *> (Join-Path $out 'signing.log')
 if($LASTEXITCODE -ne 0){throw 'Private test signing failed.'}
 Add-AppxPackage -Path $signed
 $package=Get-AppxPackage -Name $identity
 if(-not $package){throw 'The independent test package was not installed.'}
 $result.packageInstalled=$true;$result.packageFullName=$package.PackageFullName
 $result|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $out 'result.json') -Encoding utf8
 Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class RilmoNativePackageActivation {
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
 $smoke=Join-Path $out 'ui-smoke';[uint32]$activationPid=0
 $hr=[RilmoNativePackageActivation]::Launch(($package.PackageFamilyName+'!App'),('--smoke-ui "'+$smoke+'"'),[ref]$activationPid)
 if($hr -lt 0 -or -not $activationPid){throw ('Installed test activation failed: '+('0x{0:x8}' -f $hr))}
 $owned=Get-Process -Id $activationPid -ErrorAction Stop
 if([RilmoNativePackageActivation]::PackageName($owned.Handle) -cne $package.PackageFullName){throw 'The test process did not have the installed package identity.'}
 $result.packagedIdentityProved=$true;$result.processId=$activationPid
 $deadline=[DateTime]::UtcNow.AddMinutes(5)
 $frameworkPaths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 $cppPaths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 do{
  if($owned.WaitForExit(750)){break}
  # Process.Modules is cached until Refresh; the engine loads after activation.
  $owned.Refresh()
  foreach($module in $owned.Modules){
   if($module.ModuleName -match '^(msvcp140|vcruntime140|concrt140)'){$null=$cppPaths.Add($module.FileName)}
   if($module.FileName.StartsWith($framework.InstallLocation+'\',[StringComparison]::OrdinalIgnoreCase)){$null=$frameworkPaths.Add($module.FileName)}
  }
  if([DateTime]::UtcNow -ge $deadline){throw 'The owned installed integration test exceeded five minutes.'}
 }while($true)
 $result.frameworkModules=@($frameworkPaths|Sort-Object);$result.observedCppModules=@($cppPaths|Sort-Object)
 $result.processExitCode=$owned.ExitCode
 if($owned.ExitCode -ne 0){throw ('Installed integration test failed; inspect '+$smoke)}
 $testResult=Join-Path $smoke 'result.json'
 if(-not(Test-Path -LiteralPath $testResult)){throw 'The instrumented test did not produce integration evidence.'}
 $result.uiSmokeResult=Get-Content -LiteralPath $testResult -Raw|ConvertFrom-Json
 if(-not $result.uiSmokeResult.passed -or -not $result.uiSmokeResult.browserUi){throw 'Recording and both browser sessions must explicitly pass.'}
 $result.recordingTestPassed=$true
 if(-not $result.frameworkModules.Count){throw 'No module from the declared Microsoft desktop framework was observed.'}
}catch{$result.error=$_.Exception.Message}
finally{
 if($owned){try{if(-not $owned.HasExited){$owned.Kill()};$owned.Dispose()}catch{$result.processCleanupError=$_.Exception.Message}}
 $installed=Get-AppxPackage -Name $identity -ErrorAction SilentlyContinue
 if($installed){
  foreach($process in Get-Process -ErrorAction SilentlyContinue){try{if($process.Path -and $process.Path.StartsWith($installed.InstallLocation+'\',[StringComparison]::OrdinalIgnoreCase)){Stop-Process -Id $process.Id -ErrorAction Stop}}catch{$result.processCleanupError=$_.Exception.Message}}
  try{Remove-AppxPackage -Package $installed.PackageFullName}catch{$result.packageCleanupError=$_.Exception.Message}
 }
 $result.packageRemoved=-not [bool](Get-AppxPackage -Name $identity -ErrorAction SilentlyContinue)
 if($certificate){
  foreach($store in @('Cert:\LocalMachine\TrustedPeople','Cert:\CurrentUser\My')){
   $path=$store+'\'+$certificate.Thumbprint
   if(Test-Path -LiteralPath $path){try{if($store -eq 'Cert:\CurrentUser\My'){Remove-Item -LiteralPath $path -Force -DeleteKey}else{Remove-Item -LiteralPath $path -Force}}catch{$result.certificateCleanupError=$_.Exception.Message}}
  }
  $result.certificateRemoved=-not(Test-Path -LiteralPath ('Cert:\LocalMachine\TrustedPeople\'+$certificate.Thumbprint))-and -not(Test-Path -LiteralPath ('Cert:\CurrentUser\My\'+$certificate.Thumbprint))
 }
 $result.finishedUtc=[DateTime]::UtcNow.ToString('o')
 $result|ConvertTo-Json -Depth 15|Set-Content -LiteralPath (Join-Path $out 'result.json') -Encoding utf8
}
if($result.error -or -not $result.packageRemoved -or -not $result.certificateRemoved){throw ('Installed native test needs review: '+$out)}
Write-Output ('Installed native test passed and temporary state removed: '+$out)
