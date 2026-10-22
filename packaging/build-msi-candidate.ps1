# SPDX-License-Identifier: GPL-3.0-or-later
[CmdletBinding()]
param([string]$AppDirectory,[string]$TestPackageVersion,[switch]$TestFailAfterFiles)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
if(-not $AppDirectory){$AppDirectory=(Get-Content -LiteralPath (Join-Path $root 'dist/latest-build.txt') -Raw).Trim()}
$AppDirectory=[IO.Path]::GetFullPath($AppDirectory).TrimEnd('\')
if(-not $AppDirectory.StartsWith((Join-Path $root 'dist')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Use a published build inside workspace/dist.'}
function Assert-PlainPath([string]$Path) {
    $cursor=[IO.Path]::GetFullPath($Path)
    while($cursor) {
        if((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw "Linked path: $cursor"}
        $parent=Split-Path $cursor -Parent;if($parent -eq $cursor){break};$cursor=$parent
    }
}
function Xml([string]$Value){return [Security.SecurityElement]::Escape($Value)}
function Id([string]$Prefix,[string]$Value) {
    $sha=[Security.Cryptography.SHA256]::Create()
    try{$hash=$sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant()))}finally{$sha.Dispose()}
    return $Prefix+([BitConverter]::ToString($hash).Replace('-','').Substring(0,40))
}
function ComponentGuid([string]$Value) {
    $sha=[Security.Cryptography.SHA256]::Create()
    try{$hash=$sha.ComputeHash([Text.Encoding]::UTF8.GetBytes('rilmo-msi-v1/'+$Value.ToLowerInvariant()))}finally{$sha.Dispose()}
    $bytes=[byte[]]$hash[0..15]
    return ([Guid]::new($bytes)).ToString('B').ToUpperInvariant()
}
Assert-PlainPath $AppDirectory
foreach($name in @('Rilmo Recorder.exe','coreclr.dll','runtime-staging.json','runtime/browser/browser-runtime-inventory.json','rilmo-virtual-camera64.dll','virtual-camera/rilmo-virtual-camera32.dll','LICENSE','PREVIEW-NOTICE.txt')){
    if(-not(Test-Path -LiteralPath (Join-Path $AppDirectory $name) -PathType Leaf)){throw "Incomplete standalone payload: $name"}
}
if(Test-Path -LiteralPath (Join-Path $AppDirectory 'windows-installer.json')){throw 'Do not package an installed directory.'}
$wix=Join-Path $root '.tools/wix4/wix.exe'
$oldDotnetRoot=$env:DOTNET_ROOT;$oldRoll=$env:DOTNET_ROLL_FORWARD
try {
    $env:DOTNET_ROOT=Join-Path $root '.tools/dotnet';$env:DOTNET_ROLL_FORWARD='Major'
    if((& $wix --version) -notmatch '^4\.0\.6\+73c89738'){throw 'Pinned WiX 4.0.6 is required.'}
    $filter64=& (Join-Path $root 'native/virtual-camera/bin/camera-filter-data64.exe')
    if($LASTEXITCODE -ne 0 -or $filter64 -notmatch '^[0-9A-F]{32,4096}$'){throw 'DirectShow metadata generation failed.'}
    $filter32=& (Join-Path $root 'native/virtual-camera/bin/camera-filter-data32.exe')
    if($LASTEXITCODE -ne 0 -or $filter32 -cne $filter64){throw 'DirectShow metadata differs between architectures.'}
    $versionInfo=(Get-Item -LiteralPath (Join-Path $AppDirectory 'Rilmo Recorder.dll')).VersionInfo
    $v=[Version]$versionInfo.FileVersion
    if($v.Major -gt 255 -or $v.Minor -gt 255 -or $v.Build -gt 65535 -or $v.Revision -ne 0){throw 'Invalid MSI application version.'}
    $version='{0}.{1}.{2}' -f $v.Major,$v.Minor,$v.Build
    $appVersion=$version
    if($TestPackageVersion){
        if($TestPackageVersion -notmatch '^\d+\.\d+\.\d+$' -or [version]$TestPackageVersion -le [version]$appVersion){throw 'An upgrade fixture must use a higher three-part package version.'}
        $version=$TestPackageVersion
    }
    if($TestFailAfterFiles -and -not $TestPackageVersion){throw 'Rollback injection is allowed only in a separately versioned test fixture.'}
    $out=Join-Path $PSScriptRoot ('out/msi-candidate-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
    Assert-PlainPath $out;New-Item -ItemType Directory -Path $out | Out-Null
    $productCode=[Guid]::NewGuid().ToString('B').ToUpperInvariant()
    $guard=Join-Path $out 'installer-guard.dll'
    $zig=Join-Path $root '.tools/zig-x86_64-windows-0.14.1/zig.exe'
    $guardRc=Join-Path $out 'installer-guard.rc';$guardRes=Join-Path $out 'installer-guard.res'
    (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installer-guard.rc.in') -Raw).Replace('@VERSION_COMMAS@',($appVersion.Replace('.',',')+',0')).Replace('@VERSION@',$appVersion)|Set-Content -LiteralPath $guardRc -Encoding utf8
    & $zig rc /fo $guardRes $guardRc
    if($LASTEXITCODE -ne 0){throw 'Installer metadata compilation failed.'}
    & $zig cc -target x86_64-windows-gnu -O2 -shared (Join-Path $PSScriptRoot 'installer-guard.c') $guardRes -lmsi -o $guard
    if($LASTEXITCODE -ne 0){throw 'Installer launch guard compilation failed.'}
    $metadataPath=Join-Path $out 'windows-installer.json'
    [ordered]@{schemaVersion=1;model='windows-installer';productCode=$productCode;version=$version;cameraOwnedByInstaller=$true} | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding utf8
    # Enumerate without following links; no wildcard uninstaller or regsvr32 action.
    $files=[Collections.Generic.List[IO.FileInfo]]::new();$pending=[Collections.Generic.Stack[string]]::new();$pending.Push($AppDirectory)
    while($pending.Count){foreach($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force){
        if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Linked payload: $($entry.FullName)"}
        if($entry.PSIsContainer){$pending.Push($entry.FullName)}else{$files.Add($entry)}
    }}
    $files=@($files|Sort-Object FullName)
    $dirs=@{};$dirs['']='INSTALLFOLDER';$refs=[Collections.Generic.List[string]]::new();$fragments=[Collections.Generic.List[string]]::new();$manifest=@()
    $allFiles=@($files)+@(Get-Item -LiteralPath $metadataPath)
    foreach($file in $allFiles){
        $relative=if($file.FullName -eq $metadataPath){'windows-installer.json'}else{$file.FullName.Substring($AppDirectory.Length+1)}
        $parent=[IO.Path]::GetDirectoryName($relative)
        $cursor=$parent
        while($cursor){if(-not $dirs.ContainsKey($cursor)){$dirs[$cursor]=Id 'D_' $cursor};$cursor=[IO.Path]::GetDirectoryName($cursor)}
        $fileId=Id 'F_' $relative;$componentId=Id 'C_' $relative;$refs.Add($componentId)
        $shortcut=if($relative -eq 'Rilmo Recorder.exe'){'<Shortcut Id="StartMenuRilmo" Directory="ProgramMenuFolder" Name="Rilmo Screen Recorder" Advertise="yes" Icon="RilmoIcon" WorkingDirectory="INSTALLFOLDER" />'}else{''}
        $fragments.Add('<DirectoryRef Id="'+$dirs[$parent]+'"><Component Id="'+$componentId+'" Guid="'+(ComponentGuid $relative)+'" Bitness="always64"><File Id="'+$fileId+'" Source="'+(Xml $file.FullName)+'" Name="'+(Xml $file.Name)+'" KeyPath="yes">'+$shortcut+'</File></Component></DirectoryRef>')
        $manifest += [ordered]@{path=$relative;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant();bytes=$file.Length;source=$file.FullName}
    }
    $directoryXml=[Collections.Generic.List[string]]::new()
    foreach($entry in $dirs.GetEnumerator()|Where-Object Key|Sort-Object Key){
        $parent=[IO.Path]::GetDirectoryName($entry.Key)
        $directoryXml.Add('<DirectoryRef Id="'+$dirs[$parent]+'"><Directory Id="'+$entry.Value+'" Name="'+(Xml ([IO.Path]::GetFileName($entry.Key)))+'" /></DirectoryRef>')
    }
    $clsid='{9E7FF7BC-AA7A-4B1D-8A9E-DDDFB0692DFD}';$category='{860BB310-5D01-11D0-BD3B-00A0C911CE86}'
    foreach($bits in @('64','32')){
        $file=if($bits -eq '64'){'rilmo-virtual-camera64.dll'}else{'virtual-camera\rilmo-virtual-camera32.dll'}
        $cameraFileId=Id 'F_' $file;$cameraId='CameraRegistry'+$bits;$refs.Add($cameraId)
        $classKey='Software\Classes\CLSID\'+$clsid;$categoryKey='Software\Classes\CLSID\'+$category+'\Instance\'+$clsid
        $fragments.Add('<DirectoryRef Id="INSTALLFOLDER"><Component Id="'+$cameraId+'" Guid="'+(ComponentGuid $cameraId)+'" Bitness="always'+$bits+'">'+
            '<RegistryKey Root="HKLM" Key="'+$classKey+'" ForceDeleteOnUninstall="yes"><RegistryValue Type="string" Value="Rilmo Virtual Camera" /><RegistryKey Key="InprocServer32"><RegistryValue Type="string" Value="[#'+$cameraFileId+']" KeyPath="yes" /><RegistryValue Name="ThreadingModel" Type="string" Value="Both" /></RegistryKey></RegistryKey>'+
            '<RegistryKey Root="HKLM" Key="'+$categoryKey+'" ForceDeleteOnUninstall="yes"><RegistryValue Name="CLSID" Type="string" Value="'+$clsid+'" /><RegistryValue Name="FriendlyName" Type="string" Value="Rilmo Virtual Camera" /><RegistryValue Name="FilterData" Type="binary" Value="'+$filter64+'" /></RegistryKey></Component></DirectoryRef>')
    }
    $source=@'
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
<Package Name="Rilmo Screen Recorder (development candidate)" Manufacturer="Muhammad Hasnain" Version="VERSION" ProductCode="PRODUCTCODE" UpgradeCode="{F39FFB94-0F89-4E33-B626-16BBC58139A4}" Scope="perMachine" InstallerVersion="500" Compressed="yes">
<SummaryInformation Description="Unsigned Rilmo development candidate; certification pending" />
<MajorUpgrade DowngradeErrorMessage="A newer Rilmo Screen Recorder is installed." Schedule="afterInstallInitialize" />
<MediaTemplate EmbedCab="yes" CompressionLevel="mszip" />
<Property Id="ARPNOMODIFY" Value="1" />
<Property Id="ARPURLINFOABOUT" Value="https://rilmo-screen-recorder.wigglymaple8.chatgpt.site" />
<Property Id="ARPHELPLINK" Value="https://rilmo-screen-recorder.wigglymaple8.chatgpt.site/support" />
<Property Id="MSIRESTARTMANAGERCONTROL" Value="DisableShutdown" />
<Property Id="MSIFASTINSTALL" Value="0" />
<SetProperty Id="ARPINSTALLLOCATION" Value="[INSTALLFOLDER]" After="CostFinalize" Sequence="execute" />
<!-- VersionNT/WindowsBuild are compatibility values (6.3/9600) on Win10/11.
     Read the OS-maintained build value using MSI's standard AppSearch. -->
<Property Id="RILMO_OS_BUILD"><RegistrySearch Id="ActualWindowsBuild" Root="HKLM" Key="Software\Microsoft\Windows NT\CurrentVersion" Name="CurrentBuildNumber" Type="raw" Bitness="always64" /></Property>
<Binary Id="InstallerGuard" SourceFile="GUARDPATH" />
<CustomAction Id="CheckRilmoIdle" BinaryRef="InstallerGuard" DllEntry="CheckRilmoIdle" Execute="immediate" Return="check" />
<InstallUISequence><Custom Action="CheckRilmoIdle" Before="LaunchConditions" /></InstallUISequence>
<InstallExecuteSequence><Custom Action="CheckRilmoIdle" Before="LaunchConditions" /></InstallExecuteSequence>
<Property Id="LEGACY_RILMO"><RegistrySearch Id="LegacyRilmo" Root="HKLM" Key="Software\Microsoft\Windows\CurrentVersion\Uninstall\Rilmo.Recorder.Desktop" Name="UninstallString" Type="raw" Bitness="always64" /></Property>
<Property Id="RILMO_CAMERA64"><RegistrySearch Id="Camera64" Root="HKLM" Key="Software\Classes\CLSID\{9E7FF7BC-AA7A-4B1D-8A9E-DDDFB0692DFD}\InprocServer32" Type="raw" Bitness="always64" /></Property>
<Property Id="RILMO_CAMERA32"><RegistrySearch Id="Camera32" Root="HKLM" Key="Software\Classes\CLSID\{9E7FF7BC-AA7A-4B1D-8A9E-DDDFB0692DFD}\InprocServer32" Type="raw" Bitness="always32" /></Property>
<Launch Condition="Installed OR (VersionNT64 AND RILMO_OS_BUILD &gt;= 19041)" Message="Rilmo needs 64-bit Windows 10 build 19041 or newer." />
<Launch Condition="NOT RILMO_RUNNING" Message="Exit Rilmo using its tray menu before installing, repairing or uninstalling. Active recordings will not be stopped by setup." />
<Launch Condition="Installed OR NOT LEGACY_RILMO" Message="Uninstall the earlier Rilmo Recorder development preview from Windows Settings first. Your recordings and preferences are preserved." />
<Launch Condition="Installed OR WIX_UPGRADE_DETECTED OR (NOT RILMO_CAMERA64 AND NOT RILMO_CAMERA32)" Message="An independently installed Rilmo camera already exists. Remove it using its original setup tool before installing this candidate." />
<Icon Id="RilmoIcon" SourceFile="ICONPATH" />
<Property Id="ARPPRODUCTICON" Value="RilmoIcon" />
<StandardDirectory Id="ProgramFiles64Folder"><Directory Id="INSTALLFOLDER" Name="Rilmo Screen Recorder" /></StandardDirectory>
<StandardDirectory Id="ProgramMenuFolder" />
DIRECTORIES
COMPONENTS
<Feature Id="Recorder" Title="Recorder and Rilmo Virtual Camera" Level="1">REFERENCES</Feature>
</Package></Wix>
'@
    $source=$source.Replace('VERSION',$version).Replace('PRODUCTCODE',$productCode).Replace('GUARDPATH',(Xml $guard)).Replace('ICONPATH',(Xml (Join-Path $root 'assets/recorder.ico'))).Replace('DIRECTORIES',($directoryXml -join "`n")).Replace('COMPONENTS',($fragments -join "`n")).Replace('REFERENCES',(($refs|ForEach-Object{'<ComponentRef Id="'+$_+'" />'}) -join ''))
    if($TestFailAfterFiles){
        $source=$source.Replace('<InstallExecuteSequence>','<CustomAction Id="IntentionalRollbackFixture" Error="Intentional test failure after file installation. This package is a rollback fixture, not a release." /><InstallExecuteSequence><Custom Action="IntentionalRollbackFixture" After="InstallFiles" Condition="NOT Installed" />')
    }
    $wxs=Join-Path $out 'Rilmo.wxs';[IO.File]::WriteAllText($wxs,$source,[Text.UTF8Encoding]::new($false))
    $msi=Join-Path $out ('Rilmo-Screen-Recorder-'+$version+'-unsigned-candidate.msi')
    # Validation stays enabled. Never suppress ICE failures to label a candidate passed.
    & $wix build $wxs -arch x64 -o $msi
    if($LASTEXITCODE -ne 0){throw 'WiX MSI build/validation failed.'}
    foreach($entry in $manifest){if((Get-FileHash -LiteralPath $entry.source -Algorithm SHA256).Hash -ine $entry.sha256){throw 'Payload changed during packaging.'}}
    $receipt=[ordered]@{schemaVersion=1;createdUtc=[DateTime]::UtcNow.ToString('o');msi=$msi;sha256=(Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash.ToLowerInvariant();bytes=(Get-Item -LiteralPath $msi).Length;version=$version;appVersion=$appVersion;testFixture=[bool]$TestPackageVersion;intentionalRollbackFailure=[bool]$TestFailAfterFiles;productCode=$productCode;upgradeCode='{F39FFB94-0F89-4E33-B626-16BBC58139A4}';toolchain='WiX 4.0.6';declarativeCameraRegistration=$true;nativeCodeSigningComplete=$false;installTested=$false;repairTested=$false;upgradeTested=$false;rollbackTested=$false;uninstallTested=$false;wackPassed=$false;publicDistributionApproved=$false;files=$manifest}
    $receipt|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $out 'candidate-receipt.json') -Encoding utf8
    Write-Output "Unsigned candidate built; release gates pending: $msi"
} finally {$env:DOTNET_ROOT=$oldDotnetRoot;$env:DOTNET_ROLL_FORWARD=$oldRoll}
