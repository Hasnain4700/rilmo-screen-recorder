#requires -Version 7.0
[CmdletBinding()]
param([switch]$NoDownload)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$commit='afed28d37aca1938da2eedc50599bb3535a987ec'
$cache=Join-Path $root 'release/source-cache'
$treeFile=Join-Path $cache 'amf-tree.json'
$sourceRoot=Join-Path $root 'release/amf-source-inputs'
$treeUrl='https://api.github.com/repos/GPUOpen-LibrariesAndSDKs/AMF/git/trees/'+$commit+'?recursive=1'
[IO.Directory]::CreateDirectory($sourceRoot)|Out-Null
if(!(Test-Path $treeFile)){
 if($NoDownload){throw 'AMF Git tree not cached.'}
 Invoke-RestMethod $treeUrl -TimeoutSec 45|ConvertTo-Json -Depth 9|Set-Content $treeFile -Encoding utf8
}
$tree=Get-Content $treeFile -Raw|ConvertFrom-Json
if($tree.truncated){throw 'AMF tree is incomplete.'}
# This is exactly the sparse source selection in pinned obs-deps 80-amf.ps1, plus licenses.
$selection=@($tree.tree|Where-Object {$_.type -eq 'blob' -and ($_.path -like 'amf/public/include/*' -or $_.path -match '(^|/)(LICENSE|COPYING)(\.\w+)?$')})
if($selection.Count -lt 40){throw 'Unexpected AMF header tree.'}
$files=@($selection|ForEach-Object -Parallel {
 $item=$_;$destination=$using:sourceRoot;$revision=$using:commit;$offline=$using:NoDownload;$workspace=$using:root
 $path=[IO.Path]::GetFullPath((Join-Path $destination $item.path))
 if(!$path.StartsWith($destination+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'AMF input escaped source root.'}
 [IO.Directory]::CreateDirectory((Split-Path $path))|Out-Null
 $url='https://raw.githubusercontent.com/GPUOpen-LibrariesAndSDKs/AMF/'+$revision+'/'+$item.path
 $downloaded=!(Test-Path -LiteralPath $path)
 $readPath=$path
 if($downloaded){
  if($offline){throw ('Missing AMF input '+$item.path)}
  $readPath=$path+'.partial'
  & curl.exe --fail --location --silent --show-error --connect-timeout 15 --max-time 60 --retry 1 --retry-max-time 65 --output $readPath $url 2>&1|Out-Null
  if($LASTEXITCODE -ne 0){throw ('AMF download failed: '+$item.path)}
 }
 $bytes=[IO.File]::ReadAllBytes($readPath)
 $prefix=[Text.Encoding]::UTF8.GetBytes('blob '+$bytes.Length+[char]0)
 $sha=[Security.Cryptography.SHA1]::Create()
 try{$gitHash=[Convert]::ToHexString($sha.ComputeHash($prefix+$bytes)).ToLowerInvariant()}finally{$sha.Dispose()}
 if($gitHash -ine $item.sha){throw ('AMF Git blob mismatch: '+$item.path)}
 if($downloaded){Move-Item -LiteralPath $readPath -Destination $path}
 [pscustomobject]@{path=[IO.Path]::GetRelativePath($workspace,$path).Replace('\','/');sha256=(Get-FileHash $path).Hash.ToLowerInvariant();gitBlobSha1=$gitHash;sourceUrl=$url;bytes=$bytes.Length}
} -ThrottleLimit 8)
if($files.Count -ne $selection.Count){throw 'AMF source collection incomplete.'}
$manifest=[ordered]@{schemaVersion=1;commit=$commit;treeUrl=$treeUrl;treeSha256=(Get-FileHash $treeFile).Hash.ToLowerInvariant();scope='Pinned obs-deps sparse AMF public headers plus upstream licenses; no SDK binary or sample archive claimed.';files=@($files|Sort-Object path)}
$manifest|ConvertTo-Json -Depth 7|Set-Content (Join-Path $sourceRoot 'manifest.json') -Encoding utf8
$archive=Join-Path $cache ('amf-'+$commit+'-headers-v1.zip')
if(!(Test-Path -LiteralPath $archive)){
 $stream=[IO.File]::Open($archive,[IO.FileMode]::CreateNew)
 $zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$false)
 try{
  foreach($source in Get-ChildItem -LiteralPath $sourceRoot -File -Recurse|Sort-Object FullName){
   $entry=$zip.CreateEntry([IO.Path]::GetRelativePath($sourceRoot,$source.FullName).Replace('\','/'),[IO.Compression.CompressionLevel]::NoCompression)
   $entry.LastWriteTime=[DateTimeOffset]::new(1980,1,1,0,0,0,[TimeSpan]::Zero);$entry.ExternalAttributes=0
   $output=$entry.Open();$input=[IO.File]::OpenRead($source.FullName)
   try{$input.CopyTo($output)}finally{$input.Dispose();$output.Dispose()}
  }
 }finally{$zip.Dispose();$stream.Dispose()}
}
Write-Output ('Verified '+$files.Count+' pinned AMF source/license blobs; sparse source archive '+$archive)
