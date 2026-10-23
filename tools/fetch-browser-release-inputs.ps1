[CmdletBinding()]
param([switch]$NoDownload)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$destination=Join-Path $workspace 'release\browser-source-inputs'
$downloads=Join-Path $workspace '.tools\downloads'
[IO.Directory]::CreateDirectory($destination)|Out-Null
[IO.Directory]::CreateDirectory($downloads)|Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
if(-not ('System.IO.Compression.BrotliStream' -as [type])){throw 'Run this source/notice collector with PowerShell 7 or newer.'}
$files=[Collections.Generic.List[object]]::new()
function Add-Input([string]$Path,[string]$Url,[string]$Kind,[string]$ArchiveEntry='') {
    $full=[IO.Path]::GetFullPath($Path)
    if(-not $full.StartsWith($workspace+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Release input escaped the workspace.'}
    $files.Add([ordered]@{path=$full.Substring($workspace.Length+1).Replace('\','/');sha256=(Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant();size=(Get-Item -LiteralPath $full).Length;sourceUrl=$Url;kind=$Kind;archiveEntry=$ArchiveEntry})
}
function Acquire([string]$Name,[string]$Url,[string]$Hash,[string]$Kind,[string]$Folder=$downloads) {
    $path=Join-Path $Folder $Name
    if(-not (Test-Path -LiteralPath $path)){
        if($NoDownload){throw "Missing pinned input: $Name"}
        & curl.exe --fail --location --retry 2 --connect-timeout 30 --silent --show-error --continue-at - --output ($path+'.partial') $Url
        if($LASTEXITCODE -ne 0){throw "Download failed; partial preserved: $Name"}
        if((Get-FileHash -LiteralPath ($path+'.partial') -Algorithm SHA256).Hash -ine $Hash){throw "Pinned input failed SHA-256; not extracted: $Name"}
        Move-Item -LiteralPath ($path+'.partial') -Destination $path
    }
    if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $Hash){throw "Pinned input failed SHA-256: $Name"}
    Add-Input $path $Url $Kind
    return $path
}
function Read-ZipEntry($Zip,[string]$Name){
    $entry=$Zip.GetEntry($Name);if($null -eq $entry){throw "Archive entry is missing: $Name"}
    if($entry.Length -gt 32MB){throw "Notice entry exceeds its extraction limit: $Name"}
    $input=$entry.Open();$buffer=[IO.MemoryStream]::new()
    try{$input.CopyTo($buffer);return ,$buffer.ToArray()}finally{$input.Dispose();$buffer.Dispose()}
}
function Save-Notice([byte[]]$Bytes,[string]$Relative,[string]$Url,[string]$ArchiveEntry){
    $path=[IO.Path]::GetFullPath((Join-Path $destination $Relative))
    if(-not $path.StartsWith($destination+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Notice extraction escaped its destination.'}
    [IO.Directory]::CreateDirectory((Split-Path -Parent $path))|Out-Null
    [IO.File]::WriteAllBytes($path,$Bytes)
    Add-Input $path $Url 'verbatim-notice-or-build-metadata' $ArchiveEntry
}
$cefUrl='https://cdn-fastly.obsproject.com/downloads/cef_binary_6533_windows_x64_v2.zip'
$cef=Acquire 'cef_binary_6533_windows_x64_v2.zip' $cefUrl '922efbda1f2f8be9e5b2754d878a14d90afc81f04e94fc9101a7513e2b5cecc1' 'upstream-binary-distribution-with-wrapper-sources'
$qtUrl='https://download.qt.io/archive/qt/6.11/6.11.1/submodules/qtbase-everywhere-src-6.11.1.zip'
$qt=Acquire 'qtbase-everywhere-src-6.11.1.zip' $qtUrl '3529cc37297a5a7aae4486843b9fd41c30df1d79a770f85e240b537dcc327ca5' 'complete-qtbase-source-archive'
$cefCommit='2b7d20b8cdcba6e4e9507a1015b8262515a196c6'
$cefSourceUrl='https://codeload.github.com/chromiumembedded/cef/zip/'+$cefCommit
$cefSource=Acquire ('cef-'+$cefCommit+'.zip') $cefSourceUrl 'f5039ba15b7ba129fe11a9ba1c039f2a2a06d9d7958129c45b993bbe458ad7a5' 'cef-source-and-chromium-patch-series'
$deps='https://raw.githubusercontent.com/obsproject/obs-deps/8683107a02300923abe4f293920f4b5edc8cb624/'
$null=Acquire 'qt6.ps1' ($deps+'deps.qt/qt6.ps1') '26d2e4c13bc48c58f020ecb0b2eae6117ef4f0741ca10662aacfA316ae278fa3' 'pinned-build-recipe-not-executed' $destination
$null=Acquire '0001-QTBUG-146665.patch' ($deps+'deps.qt/patches/Qt6/win/0001-QTBUG-146665.patch') '461d06dff5aab49816693ef301639626c142caedfb63332f4f756471899bfce7' 'qtbase-windows-build-patch' $destination
$null=Acquire 'qtbase-everywhere-src-6.11.1.zip.sha256' ($deps+'deps.qt/checksums/qtbase-everywhere-src-6.11.1.zip.sha256') '3edc24d220c5e5e717e8305b1a987c8e5fd3e651572cee76bab4fe0bbd34a050' 'upstream-source-checksum' $destination
$null=Acquire 'CHROMIUM_BUILD_COMPATIBILITY.txt' ('https://raw.githubusercontent.com/chromiumembedded/cef/'+$cefCommit+'/CHROMIUM_BUILD_COMPATIBILITY.txt') '76dc7f2c07c1ceeee8e242286b89fe1a2cff68b9e8556e9a2a224c6b533551e5' 'pinned-chromium-depot-tools-checkouts' $destination
$null=Acquire 'Chromium-DEPS' 'https://raw.githubusercontent.com/chromium/chromium/c51f2916e4a14157c276fd33c8bfb24155aac1ad/DEPS' 'bd35b66a22eb81109589ee5093b7c492fd7ed32485574446a107e3f274affc55' 'pinned-chromium-dependency-checkouts' $destination
$cefZip=[IO.Compression.ZipFile]::OpenRead($cef)
try {
    foreach($name in @('LICENSE.txt','README.txt','include/cef_version.h','include/cef_pack_resources.h')){
        Save-Notice (Read-ZipEntry $cefZip $name) ('cef-notices/'+[IO.Path]::GetFileName($name)) $cefUrl $name
    }
    $pak=Read-ZipEntry $cefZip 'Resources/resources.pak'
    $sha=[Security.Cryptography.SHA256]::Create()
    try{$pakHash=[Convert]::ToHexString($sha.ComputeHash($pak)).ToLowerInvariant()}finally{$sha.Dispose()}
    $installedPak=Join-Path $workspace 'vendor\obs-browser\32.2.2\bin\resources.pak'
    $preparedPakMatches=$null
    if(Test-Path -LiteralPath $installedPak){
        $preparedPakMatches=(Get-FileHash -LiteralPath $installedPak -Algorithm SHA256).Hash -ieq $pakHash
        if(-not $preparedPakMatches){throw 'Prepared browser resources do not match the exact pinned CEF archive.'}
    }
    if([BitConverter]::ToUInt32($pak,0) -ne 5){throw 'Unsupported Chromium resource pack version.'}
    $count=[BitConverter]::ToUInt16($pak,8)
    $definitions=[Text.Encoding]::UTF8.GetString((Read-ZipEntry $cefZip 'include/cef_pack_resources.h'))
    $creditResources=[ordered]@{IDR_ABOUT_UI_CREDITS_HTML='Chromium-Credits.html';IDR_ABOUT_UI_CREDITS_CSS='credits.css';IDR_ABOUT_UI_CREDITS_JS='credits.js'}
    $resourceRecords=[Collections.Generic.List[object]]::new()
    foreach($symbol in $creditResources.Keys){
        $match=[regex]::Match($definitions,'(?m)^#define '+$symbol+' (\d+)\s*$')
        if(-not $match.Success){throw "Credit resource ID is missing: $symbol"}
        $id=[int]$match.Groups[1].Value;$found=$false
        for($index=0;$index -lt $count;$index++){
            $entry=12+$index*6
            if([BitConverter]::ToUInt16($pak,$entry) -ne $id){continue}
            $offset=[BitConverter]::ToUInt32($pak,$entry+2);$end=[BitConverter]::ToUInt32($pak,$entry+8)
            if($end -gt $pak.Length -or $end -lt $offset+8 -or $pak[$offset] -ne 0x1e -or $pak[$offset+1] -ne 0x9b){throw 'Invalid Chromium Brotli resource header.'}
            [long]$expected=0;for($byte=0;$byte -lt 6;$byte++){$expected=$expected -bor ([long]$pak[$offset+2+$byte] -shl (8*$byte))}
            if($expected -gt 32MB){throw 'Credit resource exceeds its extraction limit.'}
            $input=[IO.MemoryStream]::new($pak,[int]($offset+8),[int]($end-$offset-8));$brotli=[IO.Compression.BrotliStream]::new($input,[IO.Compression.CompressionMode]::Decompress);$output=[IO.MemoryStream]::new()
            try{
                $brotli.CopyTo($output);if($output.Length -ne $expected){throw 'Credit resource declared size mismatch.'}
                Save-Notice $output.ToArray() ('cef-notices/'+$creditResources[$symbol]) $cefUrl ('Resources/resources.pak#'+$symbol+'='+$id)
                $resourceRecords.Add([ordered]@{symbol=$symbol;id=$id;packedBytes=$end-$offset;unpackedBytes=$expected;compression='Chromium Brotli header 1e9b + uint48 size'})
            }finally{$output.Dispose();$brotli.Dispose();$input.Dispose()}
            $found=$true;break
        }
        if(-not $found){throw "Chromium credit resource is absent: $symbol"}
    }
}finally{$cefZip.Dispose()}
$qtZip=[IO.Compression.ZipFile]::OpenRead($qt)
try{
    $prefix='qtbase-everywhere-src-6.11.1/'
    foreach($entry in $qtZip.Entries){
        if($entry.Length -eq 0 -or -not $entry.FullName.StartsWith($prefix,[StringComparison]::Ordinal)){continue}
        $relative=$entry.FullName.Substring($prefix.Length)
        if($relative.StartsWith('LICENSES/',[StringComparison]::Ordinal) -or
            ($relative.StartsWith('src/3rdparty/',[StringComparison]::Ordinal) -and ($entry.Name -match '^(LICENSE|COPYING|COPYRIGHT|AUTHORS|NOTICE|CREDITS)([._-]|$)' -or $entry.Name -eq 'qt_attribution.json')) -or
            $relative -in @('src/corelib/global/qglobal.h','src/gui/kernel/qguiapplication.cpp','src/widgets/kernel/qwidget.cpp')){
            Save-Notice (Read-ZipEntry $qtZip $entry.FullName) ('qtbase-notices/'+$relative) $qtUrl $entry.FullName
        }
    }
}finally{$qtZip.Dispose()}
$manifest=[ordered]@{
    schemaVersion=1;obsVersion='32.2.2';collectedAtUtc=[DateTime]::UtcNow.ToString('o')
    cef=[ordered]@{version='127.145.7+g2b7d20b+chromium-127.0.6533.120';sourceCommit=$cefCommit;repositoryDeclaredByBinary='https://github.com/pkviet/cef.git';sourceMirror='https://github.com/chromiumembedded/cef';chromiumCommit='c51f2916e4a14157c276fd33c8bfb24155aac1ad';chromiumTag='127.0.6533.120';chromiumRepository='https://chromium.googlesource.com/chromium/src.git';depotToolsCommit='22f6c9d8e9';ffmpegCommit='bc1ee906cede137c061bea5a5e8d0743374392a5';resourcePakSha256=$pakHash;preparedRuntimeResourcePakMatches=$preparedPakMatches;credits=$resourceRecords}
    qt=[ordered]@{version='6.11.1';runtimeLibraries=@('Qt6Core.dll','Qt6Gui.dll','Qt6Widgets.dll');sourceModule='qtbase';obsDepsCommit='8683107a02300923abe4f293920f4b5edc8cb624';obsDepsArchive='release/source-cache/obs-deps-8683107a02300923abe4f293920f4b5edc8cb624.tar.gz';windowsPatch='0001-QTBUG-146665.patch';noticeScope='All qtbase LICENSES and third-party attribution/license files, broader than enabled Windows features; exact inclusion audit still required.'}
    files=@($files)
    correspondingSourceComplete=$false;publicDistributionApproved=$false
    remainingChecks=@('Acquire/reproduce exact Chromium checkout plus its DEPS and CEF patch application/build arguments for the shipped binary, including relevant bundled third-party sources.','Reconcile Qt enabled feature/static dependency attribution against the exact compiled Qt DLLs, apply the pinned patch and verify build instructions; complete the selected license compliance route.','Rebuild or otherwise establish correspondence between installed-fallback signed native binaries and pinned upstream source/build inputs; hashes alone do not establish corresponding source.','Verify source distribution/relinking requirements, notices presentation and Microsoft redistributable terms for the final Store/standalone package.')
}
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $destination 'manifest.json') -Encoding utf8
Write-Output "Collected $($files.Count) verified browser source/notice inputs. Chromium credits extracted from the pinned CEF distribution; prepared-runtime match is '$preparedPakMatches' (blank means not present). Release approval remains false."
