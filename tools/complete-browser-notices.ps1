[CmdletBinding()]
param([string]$RuntimeDirectory=(Join-Path (Split-Path -Parent $PSScriptRoot) 'vendor\obs-browser\32.2.2'))
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$runtime=[IO.Path]::GetFullPath($RuntimeDirectory).TrimEnd('\')
if(-not $runtime.StartsWith($workspace+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Prepared browser component must be inside the workspace.'}
$inventoryPath=Join-Path $runtime 'browser-runtime-inventory.json'
$inventory=Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
if($inventory.runtimeVersion -ne '32.2.2'){throw 'Unexpected browser runtime version.'}
foreach($file in $inventory.files){
    $path=[IO.Path]::GetFullPath((Join-Path $runtime $file.path))
    if(-not $path.StartsWith($runtime+'\',[StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $file.sha256){throw 'Prepared browser inventory verification failed before notice update.'}
}
$manifestPath=Join-Path $workspace 'release\browser-source-inputs\manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$actualPak=(Get-FileHash -LiteralPath (Join-Path $runtime 'bin\resources.pak') -Algorithm SHA256).Hash
if($manifest.obsVersion -ne '32.2.2' -or $actualPak -ine $manifest.cef.resourcePakSha256){throw 'Browser credits do not match the prepared Chromium resources.'}
$plan=[Collections.Generic.List[object]]::new()
foreach($file in $manifest.files){
    $relative=[string]$file.path
    $prefix='release/browser-source-inputs/'
    if(-not $relative.StartsWith($prefix,[StringComparison]::Ordinal)){continue}
    $notice=$relative.Substring($prefix.Length)
    if(-not ($notice.StartsWith('cef-notices/',[StringComparison]::Ordinal) -or $notice.StartsWith('qtbase-notices/',[StringComparison]::Ordinal))){continue}
    $source=[IO.Path]::GetFullPath((Join-Path $workspace $relative))
    $target=[IO.Path]::GetFullPath((Join-Path $runtime ('licenses/'+$notice)))
    if(-not $source.StartsWith($workspace+'\',[StringComparison]::OrdinalIgnoreCase) -or -not $target.StartsWith($runtime+'\licenses\',[StringComparison]::OrdinalIgnoreCase)){throw 'Notice path escaped its root.'}
    if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ine $file.sha256){throw "Notice SHA-256 mismatch: $relative"}
    foreach($path in @($source,$target)){$parent=$path;while($parent){if((Test-Path -LiteralPath $parent) -and ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Notice update path contains a reparse point.'};$parent=Split-Path -Parent $parent}}
    $plan.Add([pscustomobject]@{source=$source;target=$target;path='licenses/'+$notice;sha256=$file.sha256;size=$file.size;origin='verified-pinned-browser-release-notice'})
}
if($plan.Count -lt 5){throw 'Matching browser notice collection is incomplete.'}
foreach($item in $plan){[IO.Directory]::CreateDirectory((Split-Path -Parent $item.target))|Out-Null;Copy-Item -LiteralPath $item.source -Destination $item.target -Force}
$noticeManifest=[ordered]@{cef=$manifest.cef;qt=$manifest.qt;files=@($plan|ForEach-Object{[ordered]@{path=$_.path;sha256=$_.sha256;size=$_.size}});sourceManifestSha256=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant();correspondingSourceComplete=$false;publicDistributionApproved=$false;remainingChecks=$manifest.remainingChecks}
$noticeManifestPath=Join-Path $runtime 'licenses\browser-notices-manifest.json'
$noticeManifest|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $noticeManifestPath -Encoding utf8
$provenancePath=Join-Path $runtime 'provenance.json'
$provenance=Get-Content -LiteralPath $provenancePath -Raw|ConvertFrom-Json
$provenance|Add-Member -NotePropertyName matchedChromiumCreditsIncluded -NotePropertyValue $true -Force
$provenance|Add-Member -NotePropertyName qtbaseSourceNoticesIncluded -NotePropertyValue $true -Force
$provenance.outstandingLicensing=$manifest.remainingChecks
$provenance|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $provenancePath -Encoding utf8
$origins=@{};foreach($file in $inventory.files){$origins[[string]$file.path]=[string]$file.origin};foreach($item in $plan){$origins[$item.path]=$item.origin};$origins['licenses/browser-notices-manifest.json']='generated-matched-browser-notice-manifest'
$inventory.files=@(Get-ChildItem -LiteralPath $runtime -Recurse -File|Where-Object{$_.FullName -ne $inventoryPath}|Sort-Object FullName|ForEach-Object{$relative=$_.FullName.Substring($runtime.Length+1).Replace('\','/');if(-not $origins.ContainsKey($relative)){throw "Uninventoried browser file: $relative"};[ordered]@{path=$relative;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant();size=$_.Length;origin=$origins[$relative]}})
$inventory|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $inventoryPath -Encoding utf8
Write-Output "Added $($plan.Count) verified notice/header files. Browser component inventory now $($inventory.files.Count) files. Corresponding-source and release approval flags remain false."
