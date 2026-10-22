# SPDX-License-Identifier: GPL-2.0-or-later
# Independent test identity; installation/certificate trust only on disposable CI.
[CmdletBinding()]
param([switch]$BuildOnly)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$out=Join-Path $root 'artifacts/msix-camera-probe'
$evidence=Join-Path $root 'artifacts/component-ci/evidence'
New-Item -ItemType Directory -Force -Path $out,$evidence|Out-Null
$stamp=[Guid]::NewGuid().ToString('N')
$probeArch=if($env:RUNNER_ARCH -eq 'ARM64'){'arm64'}else{'x64'}
$header=@"
#pragma once
static const GUID PROBE_CLSID={0xb840d336,0x5491,0x4ed6,{0x91,0x5c,0x13,0xc4,0xc9,0x87,0xe5,0x48}};
#define PROBE_CLSID_STRING L"{B840D336-5491-4ED6-915C-13C4C987E548}"
#define PROBE_FACTORY_EVENT L"Global\\RilmoProbeFactory_$stamp"
#define PROBE_ACTIVATION_EVENT L"Global\\RilmoProbeActivation_$stamp"
#define PROBE_SOURCE_READY_EVENT L"Global\\RilmoProbeSourceReady_$stamp"
#define PROBE_DESCRIPTOR_EVENT L"Global\\RilmoProbeDescriptor_$stamp"
#define PROBE_ATTRIBUTES_EVENT L"Global\\RilmoProbeAttributes_$stamp"
"@
$header|Set-Content -LiteralPath (Join-Path $out 'probe-identity.h') -Encoding utf8
$zig=Join-Path $root '.tools/zig-x86_64-windows-0.14.1/zig.exe'
$source=Join-Path $root 'native/msix-camera-probe'
$exports=Join-Path $out 'probe.def'
@('EXPORTS','DllGetClassObject','DllCanUnloadNow')|Set-Content -LiteralPath $exports -Encoding ascii
if($probeArch -eq 'arm64'){
 $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
 $vs=& $vswhere -latest -prerelease -products '*' -property installationPath
 if(-not $vs){throw 'Preinstalled Visual Studio ARM64 tools are unavailable.'}
 Import-Module (Join-Path $vs 'Common7/Tools/Microsoft.VisualStudio.DevShell.dll')
 Enter-VsDevShell -VsInstallPath $vs -SkipAutomaticLocation -DevCmdArguments '-arch=arm64 -host_arch=arm64'|Out-Null
 $common=@('/nologo','/std:c++17','/O2','/MT','/EHsc',('/I'+$out))
 & cl.exe @common /LD (Join-Path $source 'source.cpp') ('/Fo'+(Join-Path $out 'source.obj')) mfplat.lib mfuuid.lib ole32.lib uuid.lib advapi32.lib /link ('/DEF:'+$exports) ('/OUT:'+(Join-Path $out 'CameraProbe.dll'))
 if($LASTEXITCODE -ne 0){throw 'ARM64 probe activation DLL build failed.'}
 & cl.exe @common (Join-Path $source 'host.cpp') ('/Fo'+(Join-Path $out 'host.obj')) mfplat.lib mfreadwrite.lib mfuuid.lib ole32.lib uuid.lib advapi32.lib /link ('/OUT:'+(Join-Path $out 'CameraProbe.exe'))
 if($LASTEXITCODE -ne 0){throw 'ARM64 probe host build failed.'}
}else{
 $common=@('-target','x86_64-windows-gnu','-O2','-std=c++17','-static','-I',$out)
 & $zig c++ @common -shared (Join-Path $source 'source.cpp') $exports -lmfplat -lmfuuid -lole32 -luuid -ladvapi32 -o (Join-Path $out 'CameraProbe.dll')
 if($LASTEXITCODE -ne 0){throw 'Probe activation DLL build failed.'}
 & $zig c++ @common -municode (Join-Path $source 'host.cpp') -lmfplat -lmfreadwrite -lmfuuid -lole32 -luuid -ladvapi32 -o (Join-Path $out 'CameraProbe.exe')
 if($LASTEXITCODE -ne 0){throw 'Probe host build failed.'}
}
& (Join-Path $out 'CameraProbe.exe') --self-test 2>&1|Tee-Object -FilePath (Join-Path $evidence 'msix-source-self-test.log')
if($LASTEXITCODE -ne 0){throw 'Synthetic Media Foundation source self-test failed.'}
if($BuildOnly){Write-Output 'Probe built and synthetic source tested. No device, package or certificate was installed.';return}
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows'){throw 'Package/certificate installation is permitted only on the disposable Windows CI runner. Use -BuildOnly locally.'}
$build=[int](Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber
$result=[ordered]@{schemaVersion=1;osBuild=$build;architecture=$probeArch;sourceCommit=$env:GITHUB_SHA;probeOnly=$true;mediaStreamingTested=$false;cameraServiceActivationProved=$false;packageInstalled=$false;packageRemoved=$false;certificateRemoved=$false;error=$null}
if($build -lt 22000){$result.skipped='Windows camera API requires Windows 11 build 22000 or later.';$result|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $evidence 'msix-camera-probe.json') -Encoding utf8;return}
if($PSVersionTable.PSEdition -ne 'Desktop'){
 & (Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe') -NoProfile -File $PSCommandPath
 if($LASTEXITCODE -ne 0){throw 'Windows PowerShell package probe failed.'};return
}
$identity='Rilmo.CameraProbe.'+$stamp
$publisher='CN=RilmoCameraProbe-'+$stamp
$certificate=$null;$package=$null;$pidValue=0
$probeStarted=Get-Date
function Test-UnpackagedControl {
 # Independent control on the disposable runner, after the MSIX is removed.
 # Do not infer a packaging limitation if the identical unboxed source fails.
 $control=[ordered]@{registryCreated=$false;registryRemoved=$false;filesRemoved=$false;error=$null}
 $class='HKLM:\SOFTWARE\Classes\CLSID\{B840D336-5491-4ED6-915C-13C4C987E548}'
 $folder=[IO.Path]::GetFullPath((Join-Path $env:ProgramFiles ('RilmoCameraProbe-'+$stamp)))
 $expectedParent=[IO.Path]::GetFullPath($env:ProgramFiles)
 if([IO.Path]::GetDirectoryName($folder) -ine $expectedParent -or (Test-Path -LiteralPath $class) -or (Test-Path -LiteralPath $folder)){throw 'Control identity or exact output folder already exists; preserving it.'}
 try{
  New-Item -ItemType Directory -Path $folder|Out-Null
  foreach($name in @('CameraProbe.exe','CameraProbe.dll')){Copy-Item -LiteralPath (Join-Path $out $name) -Destination $folder}
  $null=New-Item -Path ($class+'\InprocServer32') -Force
  $control.registryCreated=$true
  Set-Item -LiteralPath ($class+'\InprocServer32') -Value (Join-Path $folder 'CameraProbe.dll')
  Set-ItemProperty -LiteralPath ($class+'\InprocServer32') -Name ThreadingModel -Value Both
  $report=Join-Path $evidence 'msix-probe-unpackaged-control.log'
  $child=Start-Process -FilePath (Join-Path $folder 'CameraProbe.exe') -ArgumentList @('--report',('"'+$report+'"')) -WindowStyle Hidden -PassThru
  if(-not $child.WaitForExit(60000)){Stop-Process -Id $child.Id;$control.error='Unpackaged control timed out.'}
  if(Test-Path -LiteralPath $report){
   $text=Get-Content -LiteralPath $report -Raw
   $control.mediaStreamingTested=$text -match 'MEDIA_STREAM_TESTED=true'
   $control.cameraServiceActivationProved=$text -match 'SERVICE_ACTIVATED_SOURCE=true|SERVICE_ACTIVATE=1'
  }
 }catch{$control.error=$_.Exception.Message}
 finally{
  if($control.registryCreated){try{Remove-Item -LiteralPath $class -Recurse -Force}catch{$control.registryCleanupError=$_.Exception.Message}}
  $control.registryRemoved=-not(Test-Path -LiteralPath $class)
  # Verify the final absolute target before recursive deletion. Service image
  # mappings may keep the test DLL locked; report that instead of stopping or
  # changing a Windows service to force a clean result.
  if([IO.Path]::GetDirectoryName($folder) -ieq $expectedParent -and [IO.Path]::GetFileName($folder) -ceq ('RilmoCameraProbe-'+$stamp)){
   try{Remove-Item -LiteralPath $folder -Recurse -Force}catch{$control.fileCleanupError=$_.Exception.Message}
  }
  $control.filesRemoved=-not(Test-Path -LiteralPath $folder)
 }
 return $control
}
try{
 $manifest=@"
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" xmlns:com4="http://schemas.microsoft.com/appx/manifest/com/windows10/4" xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities" IgnorableNamespaces="uap com4 rescap">
 <Identity Name="$identity" Publisher="$publisher" Version="0.0.1.0" ProcessorArchitecture="$probeArch" />
 <Properties><DisplayName>Rilmo camera deployment probe</DisplayName><PublisherDisplayName>Rilmo CI test</PublisherDisplayName><Logo>Logo.png</Logo></Properties>
 <Resources><Resource Language="en-us" /></Resources>
 <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.22000.0" MaxVersionTested="10.0.26100.0" /></Dependencies>
 <Applications><Application Id="Probe" Executable="CameraProbe.exe" EntryPoint="Windows.FullTrustApplication"><uap:VisualElements DisplayName="Rilmo camera deployment probe" Description="Test-only activation probe. No video." BackgroundColor="#101113" Square150x150Logo="Logo.png" Square44x44Logo="Logo.png" AppListEntry="none" /></Application></Applications>
 <Extensions><com4:Extension Category="windows.comServer"><com4:ComServer><com4:InProcessServer Path="CameraProbe.dll"><com4:Class Id="B840D336-5491-4ED6-915C-13C4C987E548" ThreadingModel="Both" DisplayName="Rilmo camera activation probe" /></com4:InProcessServer></com4:ComServer></com4:Extension></Extensions>
 <Capabilities><rescap:Capability Name="runFullTrust" /><DeviceCapability Name="webcam" /></Capabilities>
</Package>
"@
 $stage=Join-Path $out 'package';New-Item -ItemType Directory -Force -Path $stage|Out-Null
 foreach($name in @('CameraProbe.exe','CameraProbe.dll')){Copy-Item -LiteralPath (Join-Path $out $name) -Destination $stage -Force}
 $manifest|Set-Content -LiteralPath (Join-Path $stage 'AppxManifest.xml') -Encoding utf8
 Add-Type -AssemblyName System.Drawing
 $bitmap=New-Object Drawing.Bitmap(150,150)
 try{$bitmap.Save((Join-Path $stage 'Logo.png'),[Drawing.Imaging.ImageFormat]::Png)}finally{$bitmap.Dispose()}
 $sdk=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
 $makeappx=Get-ChildItem -Path (Join-Path $sdk '*/x64/makeappx.exe')|Sort-Object FullName -Descending|Select-Object -First 1
 $signtool=Get-ChildItem -Path (Join-Path $sdk '*/x64/signtool.exe')|Sort-Object FullName -Descending|Select-Object -First 1
 if(-not $makeappx -or -not $signtool){throw 'Installed Microsoft SDK packaging tools are unavailable.'}
 $msix=Join-Path $out ($identity+'.msix')
 & $makeappx.FullName pack /d $stage /p $msix /o 2>&1|Out-File -LiteralPath (Join-Path $evidence 'msix-probe-packaging.log') -Encoding utf8
 if($LASTEXITCODE -ne 0){throw 'Probe manifest/package validation failed.'}
 $certificate=New-SelfSignedCertificate -Type CodeSigningCert -Subject $publisher -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy NonExportable -NotAfter ([DateTime]::Now.AddHours(2))
 $certFile=Join-Path $out 'test-only.cer'
 $null=Export-Certificate -Cert $certificate -FilePath $certFile
 # AppX deployment validates machine trust, not the caller's personal store.
 # This exact test leaf is trusted only on the disposable CI VM, never on the
 # maintainer's PC and never as a certificate authority in Trusted Root.
 $null=Import-Certificate -FilePath $certFile -CertStoreLocation Cert:\LocalMachine\TrustedPeople
 & $signtool.FullName sign /fd SHA256 /sha1 $certificate.Thumbprint $msix 2>&1|Out-File -LiteralPath (Join-Path $evidence 'msix-probe-signing.log') -Encoding utf8
 if($LASTEXITCODE -ne 0){throw 'Temporary test-package signing failed.'}
 Add-AppxPackage -Path $msix
 $package=Get-AppxPackage -Name $identity
 if(-not $package){throw 'Probe package was not registered.'};$result.packageInstalled=$true
 $unpackagedReport=Join-Path $evidence 'msix-probe-unpackaged.log'
 $process=Start-Process -FilePath (Join-Path $out 'CameraProbe.exe') -ArgumentList @('--report',('"'+$unpackagedReport+'"')) -WindowStyle Hidden -PassThru
 if(-not $process.WaitForExit(60000)){Stop-Process -Id $process.Id;throw 'Owned probe process timed out.'}
 Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class RilmoProbeActivation {
 [ComImport,Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IActivation { [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)]string id,[MarshalAs(UnmanagedType.LPWStr)]string arguments,int flags,out uint pid); }
 public static int Launch(string id,string arguments,out uint pid) {
  object value=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")));
  try{return ((IActivation)value).ActivateApplication(id,arguments,6,out pid);}finally{Marshal.FinalReleaseComObject(value);}
 }
}
'@
 $packagedReport=Join-Path $evidence 'msix-probe-packaged.log'
 [uint32]$activationPid=0
 $hr=[RilmoProbeActivation]::Launch(($package.PackageFamilyName+'!Probe'),('--report "'+$packagedReport+'"'),[ref]$activationPid)
 $result.packagedLaunchHresult=('0x{0:x8}' -f $hr);$pidValue=$activationPid
 if($hr -ge 0 -and $activationPid){
  $owned=Get-Process -Id $activationPid -ErrorAction SilentlyContinue
  if($owned -and -not $owned.WaitForExit(60000)){Stop-Process -Id $owned.Id;throw 'Owned packaged probe process timed out.'}
 }
 if(Test-Path -LiteralPath $packagedReport){
  $text=Get-Content -LiteralPath $packagedReport -Raw
  $result.cameraServiceActivationProved=$text -match 'SERVICE_ACTIVATED_SOURCE=true'
  $result.cameraServiceLoadedSourceDll=$text -match 'SERVICE_LOADED_FACTORY=true'
  $result.packagedIdentityProved=$text -match 'PACKAGE_IDENTITY=true'
  $result.mediaStreamingTested=$text -match 'MEDIA_STREAM_TESTED=true'
 }
}catch{$result.error=$_.Exception.Message;Write-Output ('Probe limitation: '+$result.error)}
finally{
 # Read-only, bounded diagnostics from this disposable VM. Never enable logging
 # or change service policy to make a camera test pass.
 $diagnosticPreference=$ErrorActionPreference
 try{
  $ErrorActionPreference='Continue'
  $diagnostics=[ordered]@{services=@();events=@();cameraConsent=@()}
  foreach($service in @('FrameServer','FrameServerMonitor','DeviceInstall')){
   $diagnostics.services+=@{name=$service;query=((& sc.exe query $service 2>&1|Out-String).Trim());config=((& sc.exe qc $service 2>&1|Out-String).Trim())}
  }
  foreach($key in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam','HKLM:\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam')){
   if(Test-Path -LiteralPath $key){$diagnostics.cameraConsent+=@{key=$key;value=(Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue).Value}}
  }
  foreach($channel in @('Microsoft-Windows-CameraFrameServer/Operational','Microsoft-Windows-CodeIntegrity/Operational','System')){
   # Some ARM64 runner builds reject Get-WinEvent's filter handle. Native
   # wevtutil preserves either bounded XML or the exact channel error instead.
   $query='*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[timediff(@SystemTime) <= 600000]]]'
   $records=(& wevtutil.exe qe $channel ('/q:'+$query) /c:12 /rd:true /f:xml 2>&1|Out-String)
   $diagnostics.events+=@{channel=$channel;queryExitCode=$LASTEXITCODE;xmlOrError=$records.Substring(0,[Math]::Min(20000,$records.Length))}
  }
  $diagnostics|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $evidence 'msix-probe-system-diagnostics.json') -Encoding utf8
 }catch{$result.diagnosticError=$_.Exception.Message}
 finally{$ErrorActionPreference=$diagnosticPreference}
 $report=Join-Path $evidence 'msix-probe-packaged.log'
 if(Test-Path -LiteralPath $report){
  $text=Get-Content -LiteralPath $report -Raw
  $result.packagedIdentityProved=$text -match 'PACKAGE_IDENTITY=true'
  $result.cameraServiceLoadedSourceDll=$text -match 'SERVICE_LOADED_FACTORY=true|SERVICE_FACTORY=1'
  $result.cameraServiceActivationProved=$text -match 'SERVICE_ACTIVATED_SOURCE=true|SERVICE_ACTIVATE=1'
  $result.mediaStreamingTested=$text -match 'MEDIA_STREAM_TESTED=true'
 }
 $installed=Get-AppxPackage -Name $identity -ErrorAction SilentlyContinue
 if($installed){try{Remove-AppxPackage -Package $installed.PackageFullName}catch{$result.packageCleanupError=$_.Exception.Message}}
 $result.packageRemoved=-not [bool](Get-AppxPackage -Name $identity -ErrorAction SilentlyContinue)
 if($result.packageRemoved){try{$result.unpackagedControl=Test-UnpackagedControl}catch{$result.unpackagedControlError=$_.Exception.Message}}
 if($certificate){
  foreach($store in @('Cert:\LocalMachine\TrustedPeople','Cert:\CurrentUser\TrustedPeople','Cert:\CurrentUser\My')){
   $path=$store+'\'+$certificate.Thumbprint
   if(Test-Path -LiteralPath $path){try{Remove-Item -LiteralPath $path -Force}catch{$result.certificateCleanupError=$_.Exception.Message}}
  }
  $result.certificateRemoved=-not(Test-Path -LiteralPath ('Cert:\LocalMachine\TrustedPeople\'+$certificate.Thumbprint)) -and -not(Test-Path -LiteralPath ('Cert:\CurrentUser\My\'+$certificate.Thumbprint))
 }
 $result|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $evidence 'msix-camera-probe.json') -Encoding utf8
}
if(-not $result.packageRemoved -or ($certificate -and -not $result.certificateRemoved)){throw 'Disposable probe cleanup failed. Inspect the retained diagnostics.'}
Write-Output 'Test-only package probe complete. Inspect cameraServiceActivationProved; this is not a streaming or Store certification pass.'
