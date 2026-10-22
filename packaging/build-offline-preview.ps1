[CmdletBinding()]
param([string]$AppDirectory, [string]$CompilerPath, [switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$workspace = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
function Assert-NoReparse([string]$Path) {
    $cursor=[IO.Path]::GetFullPath($Path)
    while($cursor) {
        if(Test-Path -LiteralPath $cursor) {
            if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Linked path is not allowed: $cursor"}
        }
        $parent=Split-Path $cursor -Parent
        if($parent -eq $cursor){break};$cursor=$parent
    }
}
function Escape-Nsis([string]$Value) {
    if($Value -match '[\x00-\x1f]'){throw 'NSIS values must contain no control characters.'}
    return $Value.Replace('$','$$').Replace('"','$\"')
}
if(-not $AppDirectory){$AppDirectory=Split-Path (& (Join-Path $workspace 'tools\launch-published.ps1') -ResolveOnly) -Parent}
$AppDirectory=[IO.Path]::GetFullPath($AppDirectory).TrimEnd('\')
if(-not $AppDirectory.StartsWith((Join-Path $workspace 'dist')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Package a standalone build inside this workspace dist directory.'}
Assert-NoReparse $AppDirectory
if(-not (Test-Path -LiteralPath $AppDirectory -PathType Container)){throw 'Standalone build directory is missing.'}
if(-not $CompilerPath){$CompilerPath=Join-Path $workspace '.tools\nsis-3.13\Bin\makensis.exe'}
$CompilerPath=[IO.Path]::GetFullPath($CompilerPath)
Assert-NoReparse $CompilerPath
$nsisRoot=Split-Path (Split-Path $CompilerPath -Parent) -Parent
$nsisLicense=Join-Path $nsisRoot 'COPYING'
$expectedCompiler='65343c225ec88393cc4453b0963caf63f763f42f48cb55736d51893ff2e29d4a'
if(-not (Test-Path -LiteralPath $CompilerPath -PathType Leaf) -or (Get-FileHash -LiteralPath $CompilerPath -Algorithm SHA256).Hash -ine $expectedCompiler){throw 'Use the verified NSIS 3.13 Bin\makensis.exe compiler; an arbitrary compiler override is not accepted.'}
if(-not (Test-Path -LiteralPath $nsisLicense -PathType Leaf) -or (Get-FileHash -LiteralPath $nsisLicense -Algorithm SHA256).Hash -ine '388357c1215ff403c5ebde3a5ecd273e68f8b79a579996775245d1ee65442aba'){throw 'The pinned NSIS license is missing or altered.'}
foreach($required in @('Rilmo Recorder.exe','Rilmo Recorder.dll','Rilmo Recorder.runtimeconfig.json','coreclr.dll','obs.dll','obs-ffmpeg-mux.exe','runtime-staging.json','runtime\browser\browser-runtime-inventory.json','rilmo-virtual-camera64.dll','virtual-camera\rilmo-virtual-camera32.dll','tools\setup-virtual-camera.ps1','LICENSE','third_party\supplemental\GPL-3.0.txt','THIRD-PARTY-NOTICES.md','PREVIEW-NOTICE.txt')) {
    if(-not (Test-Path -LiteralPath (Join-Path $AppDirectory $required) -PathType Leaf)){throw "Incomplete standalone build: $required"}
}
$runtime=Get-Content -LiteralPath (Join-Path $AppDirectory 'Rilmo Recorder.runtimeconfig.json') -Raw | ConvertFrom-Json
if($runtime.runtimeOptions.tfm -cne 'net10.0' -or -not $runtime.runtimeOptions.PSObject.Properties['includedFrameworks']){throw 'Publish a self-contained .NET 10 app before packaging.'}
$fileVersion=(Get-Item -LiteralPath (Join-Path $AppDirectory 'Rilmo Recorder.dll')).VersionInfo.FileVersion
if($fileVersion -notmatch '^\d+\.\d+\.\d+\.\d+$'){throw 'The application has no numeric four-part file version.'}
$version=[Version]$fileVersion
if(@(@($version.Major,$version.Minor,$version.Build,$version.Revision) | Where-Object {$_ -gt 65535}).Count){throw 'Application version exceeds the Windows version resource range.'}
$productVersion='{0}.{1}.{2}' -f $version.Major,$version.Minor,$version.Build
# Inspect entries before descending, including hidden entries.
$files=[Collections.Generic.List[IO.FileInfo]]::new()
$directories=[Collections.Generic.List[IO.DirectoryInfo]]::new()
$pending=[Collections.Generic.Stack[string]]::new();$pending.Push($AppDirectory)
while($pending.Count) {
    foreach($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
        if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Linked payload entry is not allowed: $($entry.FullName)"}
        if($entry.PSIsContainer){$directories.Add($entry);$pending.Push($entry.FullName)}else{$files.Add($entry)}
    }
}
$files=@($files | Sort-Object FullName)
if(Test-Path -LiteralPath (Join-Path $AppDirectory '_installer')){throw 'Reserved _installer metadata directory is present in the input.'}
if($files.Name -contains 'Uninstall Rilmo.exe'){throw 'Do not package a previously installed directory.'}
$out=Join-Path $PSScriptRoot ('out\offline-preview-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
Assert-NoReparse $out
New-Item -ItemType Directory -Path $out | Out-Null
$install=[Collections.Generic.List[string]]::new()
$uninstall=[Collections.Generic.List[string]]::new()
$support=[Collections.Generic.List[string]]::new()
$pathChecks=[Collections.Generic.List[string]]::new()
$fileIndex=0
$manifest=foreach($file in $files) {
    $relative=$file.FullName.Substring($AppDirectory.Length+1)
    if([IO.Path]::IsPathRooted($relative) -or $relative.Split('\') -contains '..'){throw 'Payload path escaped the app root.'}
    $safe=Escape-Nsis $relative;$parent=[IO.Path]::GetDirectoryName($relative)
    $pathChecks.Add('!insertmacro CheckOwnedPath "$INSTDIR\'+$safe+'" ${PathCheckFailure} "${PathCheckPrefix}"')
    $install.Add('ClearErrors')
    $install.Add('SetOutPath "$INSTDIR'+$(if($parent){'\'+(Escape-Nsis $parent)}else{''})+'"')
    $install.Add('IfErrors installFailed')
    $install.Add('File "'+(Escape-Nsis $file.FullName)+'"')
    $install.Add('IfErrors installFailed')
    # Keep the cleanup tool available after any ordinary payload deletion failure.
    $list=$uninstall
    if($relative -ieq 'tools\setup-virtual-camera.ps1'){$list=$support}
    $label='removedFile_'+$fileIndex;$fileIndex++
    $list.Add('IfFileExists "$INSTDIR\'+$safe+'" 0 '+$label)
    $list.Add('ClearErrors')
    $list.Add('Delete "$INSTDIR\'+$safe+'"')
    $list.Add('IfErrors uninstallFailed')
    $list.Add($label+':')
    [ordered]@{path=$relative;bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
}
# These removals are nonrecursive; unrelated files deliberately keep directories alive.
foreach($directory in ($directories | Sort-Object {$_.FullName.Length} -Descending)) {
    $pathChecks.Add('!insertmacro CheckOwnedPath "$INSTDIR\'+(Escape-Nsis $directory.FullName.Substring($AppDirectory.Length+1))+'" ${PathCheckFailure} "${PathCheckPrefix}"')
    $support.Add('RMDir "$INSTDIR\'+(Escape-Nsis $directory.FullName.Substring($AppDirectory.Length+1))+'"')
}
$installPath=Join-Path $out 'install-files.nsh';$uninstallPath=Join-Path $out 'uninstall-files.nsh';$supportPath=Join-Path $out 'uninstall-support-files.nsh';$checksPath=Join-Path $out 'path-checks.nsh';$configPath=Join-Path $out 'config.nsh';$manifestPath=Join-Path $out 'payload-manifest.json'
$install | Set-Content -LiteralPath $installPath -Encoding utf8
$uninstall | Set-Content -LiteralPath $uninstallPath -Encoding utf8
$support | Set-Content -LiteralPath $supportPath -Encoding utf8
$pathChecks | Set-Content -LiteralPath $checksPath -Encoding utf8
$installer=Join-Path $out ('Rilmo-Recorder-'+$productVersion+'-local-preview-setup.exe')
$defines=[ordered]@{InstallerFile=$installer;ProductIcon=(Join-Path $workspace 'assets\recorder.ico');LicenseFile=(Join-Path $AppDirectory 'third_party\supplemental\GPL-3.0.txt');NsisLicenseFile=$nsisLicense;PayloadManifest=$manifestPath;InstallFiles=$installPath;UninstallFiles=$uninstallPath;UninstallSupportFiles=$supportPath;PathChecks=$checksPath;ProductVersion=$productVersion;ProductFileVersion=$fileVersion}
@($defines.GetEnumerator() | ForEach-Object {'!define '+$_.Key+' "'+(Escape-Nsis $_.Value)+'"'})+('!define PayloadKiB '+[Math]::Ceiling(($files|Measure-Object Length -Sum).Sum/1024)) | Set-Content -LiteralPath $configPath -Encoding utf8
[ordered]@{version=$productVersion;fileVersion=$fileVersion;purpose='Unsigned local review candidate; not approved for distribution';upgradeBehavior='Refuses nonempty existing installation; uninstall first';appDirectory=$AppDirectory;files=@($manifest)} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
if($CheckOnly){Write-Output "Prepared exact-file install/uninstall manifests: $out";return}
& $CompilerPath /V2 /INPUTCHARSET UTF8 ('/DGeneratedConfig='+$configPath) (Join-Path $PSScriptRoot 'offline-preview.nsi')
if($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $installer -PathType Leaf)){throw 'Offline preview compiler failed.'}
foreach($entry in $manifest) {
    if((Get-FileHash -LiteralPath (Join-Path $AppDirectory $entry.path) -Algorithm SHA256).Hash -ine $entry.sha256){throw 'App payload changed during compilation; this candidate has no successful receipt.'}
}
[ordered]@{installer=$installer;version=$productVersion;sha256=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant();bytes=(Get-Item -LiteralPath $installer).Length;payloadFiles=$files.Count;compiler=$CompilerPath;compilerSha256=$expectedCompiler;compressor='zlib';nsisLicenseIncluded='_installer/NSIS-COPYING.txt';publicDistributionApproved=$false;installTested=$false;uninstallTested=$false;upgradeSupported=$false;rollbackTested=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'installer-receipt.json') -Encoding utf8
Write-Output "Unsigned offline preview compiled: $installer"
