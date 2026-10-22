#requires -Version 7.0
[CmdletBinding()]
param([string]$Version='0.3.0',[string]$OutputPath)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if($Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Version must have three numeric components.'}
if(!$OutputPath){$OutputPath=Join-Path $root ('release/source-review-bundle-'+$Version+'-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss')+'.zip')}
$OutputPath=[IO.Path]::GetFullPath($OutputPath)
if(!$OutputPath.StartsWith((Join-Path $root 'release')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Bundle must remain within workspace release directory.'}
if(Test-Path -LiteralPath $OutputPath){throw 'Output already exists; preserving it.'}
$corePath=Join-Path $root 'release/source-inputs.manifest.json'
$browserPath=Join-Path $root 'release/browser-source-inputs/manifest.json'
if(!(Test-Path $corePath) -or !(Test-Path $browserPath)){throw 'Run both source acquisition scripts first.'}
$core=Get-Content $corePath -Raw|ConvertFrom-Json
$browser=Get-Content $browserPath -Raw|ConvertFrom-Json
$files=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
function Add-BundleFile([string]$Relative,[string]$ExpectedHash,[string]$Kind){
 $relativePath=$Relative.Replace('\','/')
 $full=[IO.Path]::GetFullPath((Join-Path $root $Relative))
 if(!$full.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Source path escaped workspace.'}
 if(!(Test-Path -LiteralPath $full -PathType Leaf)){throw ('Source input missing: '+$Relative)}
 $info=Get-Item -LiteralPath $full
 if($info.Attributes -band [IO.FileAttributes]::ReparsePoint){throw ('Symlink input needs explicit review: '+$Relative)}
 $sha=(Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
 if($ExpectedHash -and $sha -ine $ExpectedHash){throw ('Source input hash mismatch: '+$Relative)}
 if(!$files.ContainsKey($relativePath)){$files.Add($relativePath,[pscustomobject]@{path=$relativePath;sha256=$sha;bytes=$info.Length;kind=$Kind})}
}
foreach($directory in @('src','assets','docs','tools','packaging','third_party','native/virtual-camera','release/evidence')){
 $directoryPath=Join-Path $root $directory
 if(!(Test-Path -LiteralPath $directoryPath)){continue}
 foreach($file in Get-ChildItem -LiteralPath $directoryPath -Recurse -File){
  $relative=[IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/')
  if($relative -match '^(src/(bin|obj)/|native/virtual-camera/(bin|build)/|packaging/out/)' -or $relative -match '\.(pfx|p12|cer|log)$' -or $relative -eq 'packaging/release-config.json'){continue}
  Add-BundleFile $relative $null 'project-source-or-notice'
 }
}
foreach($name in @('LICENSE','THIRD-PARTY-NOTICES.md','README.md','.gitignore','global.json','build.ps1','run.ps1','Start Rilmo.cmd')){if(Test-Path -LiteralPath (Join-Path $root $name)){Add-BundleFile $name $null 'project-source-or-notice'}}
foreach($name in @('release/source-inputs.lock.json','release/source-inputs.manifest.json','release/browser-source-inputs/manifest.json')){Add-BundleFile $name $null 'acquisition-manifest'}
if(Test-Path -LiteralPath (Join-Path $root 'release/source-cache/ffmpeg-8.1.2-tag.json')){Add-BundleFile 'release/source-cache/ffmpeg-8.1.2-tag.json' $null 'official-release-tag-to-commit-evidence'}
foreach($name in @('vendor/obs/32.2.2/runtime-inventory.json','vendor/obs/32.2.2/provenance.json','vendor/obs/32.2.2/native-imports.json','vendor/obs-browser/32.2.2/browser-runtime-inventory.json','vendor/obs-browser/32.2.2/provenance.json','artifacts/ffmpeg-license-evidence.json','artifacts/runtime-official-comparison.json','artifacts/browser-official-comparison.json')){
 if(Test-Path -LiteralPath (Join-Path $root $name)){Add-BundleFile $name $null 'selected-native-provenance-evidence'}
}
foreach($item in @($core.files|Where-Object status -eq 'verified')){Add-BundleFile $item.file $item.sha256 'core-source-input'}
foreach($item in $browser.files){Add-BundleFile $item.path $item.sha256 $item.kind}
if(Test-Path -LiteralPath (Join-Path $root 'release/amf-source-inputs/manifest.json')){
 Add-BundleFile 'release/amf-source-inputs/manifest.json' $null 'sparse-git-source-manifest'
 $amf=Get-Content (Join-Path $root 'release/amf-source-inputs/manifest.json') -Raw|ConvertFrom-Json
 foreach($item in $amf.files){Add-BundleFile $item.path $item.sha256 'git-blob-verified-amf-header-or-license'}
 Add-BundleFile 'release/source-cache/amf-tree.json' $amf.treeSha256 'pinned-git-tree-evidence'
}
$manifest=[ordered]@{
 schemaVersion=1;version=$Version;createdUtc=[DateTime]::UtcNow.ToString('o');description='Local source-review bundle. This is not a complete or approved corresponding-source release.'
 correspondingSourceComplete=$false;publicDistributionApproved=$false
 missingCoreInputs=@($core.files|Where-Object status -ne 'verified'|Select-Object component,file,error)
 remainingReview=@($core.remainingReview)+@('Complete Chromium source and transitive DEPS correspondence for the exact CEF binary.','Confirm Qt feature/license and relinking requirements for the shipped three libraries.','Rebuild native binary set from the supplied source/build inputs; an upstream source archive plus matching version is not proof of correspondence.','Review build tools and every static/transitive component, then finalize source delivery and published notices.')
 files=@($files.Values|Sort-Object path)
}
[IO.Directory]::CreateDirectory((Split-Path $OutputPath))|Out-Null
$stream=[IO.File]::Open($OutputPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$false)
try{
 foreach($item in $manifest.files){
  $level=if($item.path -match '\.(zip|gz|xz|7z)$'){[IO.Compression.CompressionLevel]::NoCompression}else{[IO.Compression.CompressionLevel]::Optimal}
  $input=[IO.File]::Open((Join-Path $root $item.path),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
  try {
   $hashAlgorithm=[Security.Cryptography.SHA256]::Create()
   try{$currentHash=[Convert]::ToHexString($hashAlgorithm.ComputeHash($input)).ToLowerInvariant()}finally{$hashAlgorithm.Dispose()}
   if($currentHash -ine $item.sha256){throw ('Source changed while bundling; rerun after edits finish: '+$item.path)}
   $input.Position=0
   $entryStream=$zip.CreateEntry($item.path,$level).Open()
   try{$input.CopyTo($entryStream)}finally{$entryStream.Dispose()}
  }finally{$input.Dispose()}
 }
 $entry=$zip.CreateEntry('release-source-manifest.json')
 $writer=[IO.StreamWriter]::new($entry.Open(),[Text.UTF8Encoding]::new($false))
 try{$writer.Write(($manifest|ConvertTo-Json -Depth 9))}finally{$writer.Dispose()}
}finally{$zip.Dispose();$stream.Dispose()}
$archive=Get-Item -LiteralPath $OutputPath
$result=[ordered]@{path=$archive.FullName;sha256=(Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant();bytes=$archive.Length;files=$files.Count+1;correspondingSourceComplete=$false;publicDistributionApproved=$false}
$result|ConvertTo-Json|Set-Content -LiteralPath ($OutputPath+'.json') -Encoding utf8
$result|ConvertTo-Json
