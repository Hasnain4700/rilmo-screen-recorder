#requires -Version 7.0
[CmdletBinding()]
param([switch]$NoDownload, [ValidateRange(1,8)][int]$ThrottleLimit=4, [string[]]$Components=@())
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$lock=Get-Content (Join-Path $root 'release/source-inputs.lock.json') -Raw | ConvertFrom-Json
$amfInput=$lock.inputs|Where-Object component -eq 'amf'
if($amfInput -and $amfInput.file -like '*-headers-v1.zip' -and !(Test-Path -LiteralPath (Join-Path $root $amfInput.file)) -and !$NoDownload -and ($Components.Count -eq 0 -or 'amf' -in $Components)){
 & (Join-Path $PSScriptRoot 'fetch-amf-release-inputs.ps1')
}
$manifestPath=Join-Path $root 'release/source-inputs.manifest.json'
$previous=@{}
if(Test-Path -LiteralPath $manifestPath){foreach($item in (Get-Content $manifestPath -Raw|ConvertFrom-Json).files){if($item.status -eq 'verified'){$previous[$item.file]=$item.sha256}}}
$fixed=@(
 [pscustomobject]@{component='OBS complete source with submodules';version='32.2.2';commit='ba2f32bdf791005443988a4955e963663e16b1ed';file='release/source-cache/OBS-Studio-32.2.2-Sources.tar.gz';sourceUrl='https://github.com/obsproject/obs-studio/releases/download/32.2.2/OBS-Studio-32.2.2-Sources.tar.gz';expectedSha256='ec81fb66b03e75ddb3076b576f62679c39262e0e9960cef3e17a40dc5d68e6b4';pinEvidence='official-release-asset-sha256'},
 [pscustomobject]@{component='OBS dependency recipes, patches and licenses';version='2026-07-15';commit='8683107a02300923abe4f293920f4b5edc8cb624';file='release/source-cache/obs-deps-8683107a02300923abe4f293920f4b5edc8cb624.tar.gz';sourceUrl='https://codeload.github.com/obsproject/obs-deps/tar.gz/8683107a02300923abe4f293920f4b5edc8cb624';expectedSha256='14a95cbdba57e3cbc0f46a156d045f3574e7e1a90b85afb353ef3eaf44ae7e6e';pinEvidence='pinned-git-commit-and-locally-recorded-sha256'},
 [pscustomobject]@{component='Zig compiler and static libc++/libc++abi/libunwind/MinGW source';version='0.14.1';commit=$null;file='.tools/downloads/zig-0.14.1.zip';sourceUrl='https://ziglang.org/download/0.14.1/zig-x86_64-windows-0.14.1.zip';expectedSha256='554f5378228923ffd558eac35e21af020c73789d87afeabf4bfd16f2e6feed2c';pinEvidence='official-release-sha256'}
)
$results=@(($fixed+@($lock.inputs)) | ForEach-Object -ThrottleLimit $ThrottleLimit -Parallel {
 $item=$_; $workspace=$using:root; $prior=$using:previous; $offline=$using:NoDownload; $selection=$using:Components
 $record=[ordered]@{component=$item.component;version=$item.version;commit=$item.commit;file=$item.file;sourceUrl=$item.sourceUrl;pinEvidence=$item.pinEvidence;status='missing';sha256=$null;bytes=0;error=$null}
 try{
  $path=[IO.Path]::GetFullPath((Join-Path $workspace $item.file))
  if(!$path.StartsWith($workspace+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Input path escapes workspace.'}
  $expected=$item.expectedSha256
  if(!$expected -and $prior.ContainsKey($item.file)){$expected=$prior[$item.file]}
  if(!$expected -and $item.commit -notmatch '^[a-f0-9]{40}$'){throw 'Unpinned source input.'}
  if(!(Test-Path -LiteralPath $path)){
   if($offline -or ($selection.Count -gt 0 -and $item.component -notin $selection)){throw 'Not cached; download this component on a subsequent acquisition run.'}
   [IO.Directory]::CreateDirectory((Split-Path $path))|Out-Null
   $partial=$path+'.partial'
   $tlsOptions=@()
   # The pinned Theora mirror can have an unavailable Windows CRL endpoint.
   # Keep certificate/hostname verification and the mandatory upstream SHA-256;
   # only allow an unavailable revocation server, never a revoked certificate.
   if($item.component -eq 'libtheora' -and $expected){$tlsOptions=@('--ssl-revoke-best-effort','--http1.1','--ipv4')}
   & curl.exe @tlsOptions --fail --location --silent --show-error --retry 1 --retry-max-time 330 --connect-timeout 25 --max-time 300 --output $partial $item.sourceUrl 2>&1 | Out-Null
   if($LASTEXITCODE -ne 0){throw ('Download failed with curl exit '+$LASTEXITCODE)}
   $hash=(Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash.ToLowerInvariant()
   if($expected -and $hash -ine $expected){throw 'Downloaded source hash does not match pin; partial preserved.'}
   # Check archive structure without executing or extracting any downloaded code.
   if($item.file -match '\.xz$') {
    $python=Get-Command python -ErrorAction SilentlyContinue
    if(!$python){throw 'Python with tarfile/lzma is required to validate this XZ source archive on Windows.'}
    & $python.Source -c 'import sys,tarfile; t=tarfile.open(sys.argv[1],"r:xz"); t.getmembers(); t.close()' $partial 2>&1 | Out-Null
   } else { & tar.exe -tf $partial 2>&1 | Out-Null }
   if($LASTEXITCODE -ne 0){throw 'Downloaded input is not a readable archive.'}
   Move-Item -LiteralPath $partial -Destination $path
  }
  $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
  if($expected -and $hash -ine $expected){throw 'Cached source hash mismatch; file preserved for investigation.'}
  $record.status='verified';$record.sha256=$hash;$record.bytes=(Get-Item -LiteralPath $path).Length
 } catch {$record.error=$_.Exception.Message}
 [pscustomobject]$record
} | Sort-Object component)
$manifest=[ordered]@{schemaVersion=1;createdUtc=[DateTime]::UtcNow.ToString('o');correspondingSourceComplete=$false;publicDistributionApproved=$false;files=$results;remainingReview=@('Map the exact shipped native build configuration to these source inputs, including every transitive Git submodule and static dependency.','Review FFmpeg GPLv3 configuration and the combined distribution license; recipes enable GPL and version3.','Include optional browser-source-inputs manifest, CEF/Chromium notices and Qt rebuild/relink requirements.','Verify Microsoft runtime redistribution rights and notices; local System32 provenance is not redistribution approval.','Build the complete release from the delivered source and publish corresponding source alongside binaries before distribution.')}
$manifest|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $manifestPath -Encoding utf8
$verified=@($results|Where-Object status -eq 'verified')
Write-Output ('Source inputs verified: '+$verified.Count+'/'+$results.Count+'; '+[math]::Round(($verified|Measure-Object bytes -Sum).Sum/1MB,1)+' MiB. Manifest: '+$manifestPath)
$results|Where-Object status -ne 'verified'|Select-Object component,error|Format-Table -AutoSize
if($verified.Count -ne $results.Count){Write-Warning 'Some source inputs remain missing. Manifest is incomplete and is not a distribution approval.'}
