#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$siteDist=Join-Path $root 'sites/rilmo-screen-recorder/dist'
$output=Join-Path $siteDist 'downloads/rilmo-0.4.1-application-source.zip'
[IO.Directory]::CreateDirectory((Split-Path $output))|Out-Null
if(Test-Path -LiteralPath $output){throw 'Snapshot already exists; preserve the published artifact.'}
$files=[Collections.Generic.List[object]]::new()
foreach($directory in @('src','assets','native/virtual-camera','tools','packaging','third_party')){
 foreach($file in Get-ChildItem -LiteralPath (Join-Path $root $directory) -Recurse -File){
  $relative=[IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/')
  if($relative -match '^(src/(bin|obj)/|native/virtual-camera/(bin|build)/|packaging/out/)' -or $relative -match '\.(pfx|p12|cer|log|exe|dll|zip|7z|gz|xz)$' -or $relative -eq 'packaging/release-config.json'){continue}
  if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Source input is a reparse point.'}
  $files.Add([pscustomobject]@{path=$relative;fullPath=$file.FullName;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant();bytes=$file.Length})
 }
}
foreach($name in @('LICENSE','THIRD-PARTY-NOTICES.md','README.md','.gitignore','global.json','build.ps1','run.ps1','Start Rilmo.cmd')){
 $file=Get-Item -LiteralPath (Join-Path $root $name)
 $files.Add([pscustomobject]@{path=$name;fullPath=$file.FullName;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant();bytes=$file.Length})
}
# Review likely credential patterns before allowing this source-only snapshot out.
foreach($file in $files | Where-Object path -Match '\.(cs|xaml|json|ps1|py|md|txt|xml|nsi|nsh|mjs|cmd)$'){
 $content=[IO.File]::ReadAllText($file.fullPath)
 if($content -match 'gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|art_v2_[A-Za-z0-9_]{25,}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----') {throw ('Credential-like content requires review: '+$file.path)}
}
$manifest=[ordered]@{version='0.4.1';scope='Rilmo original application, native camera source, assets and build/packaging scripts. No binaries or complete native-dependency source.';correspondingSourceComplete=$false;combinedRuntimeDistributionApproved=$false;publisher='Muhammad Hasnain';files=@($files | Select-Object path,sha256,bytes)}
$stream=[IO.File]::Open($output,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
$archive=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$false)
try{
 foreach($file in $files){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.fullPath,$file.path,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}
 $writer=[IO.StreamWriter]::new($archive.CreateEntry('APPLICATION-SOURCE-SCOPE.txt').Open())
 try{$writer.WriteLine("Rilmo Screen Recorder 0.4.1 application-source snapshot.\nOriginal application code: GPL-2.0-or-later. Third-party assets retain their notices.\nThis is not a full corresponding-source release for the combined OBS/FFmpeg/CEF/Qt runtime.\nNo binary distribution is approved by this archive. Dependency correspondence and packaging remain under review.\nBuild prerequisites and commands: README.md, build.ps1 and global.json. Native build scripts are in tools/.\nContact: hasnainiqbal4665733@gmail.com")}finally{$writer.Dispose()}
 $writer=[IO.StreamWriter]::new($archive.CreateEntry('application-source-manifest.json').Open())
 try{$writer.Write(($manifest|ConvertTo-Json -Depth 6))}finally{$writer.Dispose()}
}finally{$archive.Dispose();$stream.Dispose()}
$hash=(Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
$sourcePage=Join-Path $siteDist 'source.html'
[IO.File]::WriteAllText($sourcePage,([IO.File]::ReadAllText($sourcePage).Replace('SOURCE_HASH_PENDING',$hash)))
Copy-Item -LiteralPath (Join-Path $root 'assets/rilmo-logo.png') -Destination (Join-Path $siteDist 'logo.png')
Copy-Item -LiteralPath (Join-Path $root 'artifacts/published-validation/20261022-085717/record.png') -Destination (Join-Path $siteDist 'recorder.png')
[ordered]@{path=$output;sha256=$hash;files=$files.Count;bytes=(Get-Item -LiteralPath $output).Length;scope=$manifest.scope} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'release/store-2026-10-23/application-source-receipt.json')
Get-Content -LiteralPath (Join-Path $root 'release/store-2026-10-23/application-source-receipt.json') -Raw
