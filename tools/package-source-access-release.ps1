# SPDX-License-Identifier: GPL-3.0-or-later
# Source delivery is the ZIP plus its pinned public HTTPS/VCS inputs. Large
# external sources are deliberately not misrepresented as offline ZIP entries.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublicCheckout,[string]$OutputPath)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$checkout=[IO.Path]::GetFullPath($PublicCheckout)
if(@(git -C $checkout status --porcelain).Count){throw 'Commit the reviewed public source before packaging.'}
$commit=(git -C $checkout rev-parse HEAD).Trim()
if(-not $OutputPath){$OutputPath=Join-Path $root 'release/Rilmo-0.4.2-source-and-build-inputs.zip'}
$OutputPath=[IO.Path]::GetFullPath($OutputPath)
if(-not $OutputPath.StartsWith((Join-Path $root 'release')+'\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $OutputPath)){throw 'Choose a new workspace release ZIP; existing output is preserved.'}
$files=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
$external=[Collections.Generic.List[object]]::new()
function Include-File([string]$Relative,[string]$Path,[string]$ExpectedSha){
 $full=[IO.Path]::GetFullPath($Path)
 if(-not $full.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Input escaped the workspace.'}
 $file=Get-Item -LiteralPath $full
 if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked source input is not supported.'}
 $sha=(Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
 if($ExpectedSha -and $ExpectedSha -ine $sha){throw ('Source changed: '+$Relative)}
 $files[$Relative.Replace('\','/')]=[pscustomobject]@{path=$Relative.Replace('\','/');file=$full;sha256=$sha;bytes=$file.Length}
}
foreach($relative in @(git -C $checkout -c core.quotepath=false ls-files)){
 if($relative -match '\.(pfx|p12|cer|exe|dll|zip|7z|gz|xz|log)$'){throw ('Unexpected binary/private tracked file: '+$relative)}
 Include-File $relative (Join-Path $checkout $relative) $null
}
$core=Get-Content -LiteralPath (Join-Path $root 'release/source-inputs.manifest.json') -Raw|ConvertFrom-Json
foreach($item in $core.files){
 if($item.status -cne 'verified'){throw ('Unverified core source: '+$item.component)}
 $full=Join-Path $root $item.file
 if((Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash -ine $item.sha256){throw 'Core input digest changed.'}
 if($item.component -in @('OBS complete source with submodules','OBS dependency recipes, patches and licenses','amf')){Include-File $item.file $full $item.sha256}
 else{
  if($item.sourceUrl -notmatch '^https://'){throw 'External source needs public HTTPS.'}
  $external.Add([pscustomobject]@{component=$item.component;path=$item.file;url=$item.sourceUrl;sha256=$item.sha256;bytes=$item.bytes;acquiredAndHashVerified=$true})
 }
}
$browser=Get-Content -LiteralPath (Join-Path $root 'release/browser-source-inputs/manifest.json') -Raw|ConvertFrom-Json
foreach($item in $browser.files){
 $full=Join-Path $root $item.path
 if((Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash -ine $item.sha256){throw ('Browser input digest changed: '+$item.path)}
 if($item.kind -eq 'upstream-binary-distribution-with-wrapper-sources'){continue} # Full CEF source, including wrapper, is included instead.
 if($item.kind -in @('complete-qtbase-source-archive','chromium-root-source-without-external-DEPS')){
  $external.Add([pscustomobject]@{component=$item.kind;path=$item.path;url=$item.sourceUrl;sha256=$item.sha256;bytes=$item.size;acquiredAndHashVerified=$true})
 }else{Include-File $item.path $full $item.sha256}
}
$access=Get-Content -LiteralPath (Join-Path $root 'release/browser-source-inputs/source-access-verification.json') -Raw|ConvertFrom-Json
if($access.failed -ne 0 -or $access.verified -ne 121 -or $access.inputExecuted -ne $false){throw 'Windows Chromium source-access review did not pass.'}
if(@($access.dependencies|Where-Object recursiveDeps).Count -ne 4){throw 'All four selected recursive DEPS must be supplied.'}
foreach($required in @('docs/SOURCE-RELEASE-0.4.2.md','tools/fetch-cef-corresponding-source.ps1','tools/prepare-release-sources.ps1','LICENSE','THIRD-PARTY-NOTICES.md','third_party/supplemental/GPL-3.0.txt')){
 if(-not $files.ContainsKey($required)){throw ('Required source-delivery instruction/license missing: '+$required)}
}
$manifest=[ordered]@{schemaVersion=2;version='0.4.2';storePackageVersion='1.0.0.0';sourceCommit=$commit;scope='First camera-free MSIX; separate Microsoft C++ framework';deliveryModel='included archives plus equivalent pinned public HTTPS/Git source access';correspondingSourceComplete=$true;publicSourceReleaseApproved=$true;publicDistributionApproved=$false;binaryReleaseApproval='Separate package, test and Store review; this is a source release.';offlineDependencyCheckoutComplete=$false;nativeRebuildPerformed=$false;upstreamProducerGnArgsRecovered=$false;sourceInstructions='docs/SOURCE-RELEASE-0.4.2.md';externalSourceInputs=@($external);chromiumGitSourceIndex='release/browser-source-inputs/source-access-verification.json';files=@($files.Values|Sort-Object path|Select-Object path,sha256,bytes)}
$stream=[IO.File]::Open($OutputPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$false)
try{
 foreach($item in $files.Values|Sort-Object path){
  $level=if($item.path -match '\.(zip|gz|xz)$'){[IO.Compression.CompressionLevel]::NoCompression}else{[IO.Compression.CompressionLevel]::Optimal}
  $input=[IO.File]::OpenRead($item.file);$entry=$zip.CreateEntry($item.path,$level).Open()
  try{$input.CopyTo($entry)}finally{$entry.Dispose();$input.Dispose()}
 }
 $writer=[IO.StreamWriter]::new($zip.CreateEntry('release-source-manifest.json').Open(),[Text.UTF8Encoding]::new($false))
 try{$writer.Write(($manifest|ConvertTo-Json -Depth 12))}finally{$writer.Dispose()}
}finally{$zip.Dispose();$stream.Dispose()}
$result=[ordered]@{path=$OutputPath;sha256=(Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant();bytes=(Get-Item -LiteralPath $OutputPath).Length;sourceCommit=$commit;files=$files.Count+1;externalSourceArchives=$external.Count;verifiedChromiumGitInputs=$access.verified;correspondingSourceComplete=$true;publicSourceReleaseApproved=$true;binaryReleaseApproved=$false}
$result|ConvertTo-Json|Set-Content -LiteralPath ($OutputPath+'.json') -Encoding utf8
$result|ConvertTo-Json
