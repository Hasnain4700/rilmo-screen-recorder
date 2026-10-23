# SPDX-License-Identifier: GPL-3.0-or-later
# Verify the checked-out license bytes, not normalized representations.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$noticeRoot=Join-Path $root 'third_party/obs-32.2.2'
$manifest=Get-Content -LiteralPath (Join-Path $noticeRoot 'source-notices.json') -Raw|ConvertFrom-Json
$count=0
foreach($entry in $manifest.files){
 $file=[IO.Path]::GetFullPath((Join-Path $noticeRoot $entry.path))
 if(-not $file.StartsWith($noticeRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Notice path escaped its collection.'}
 if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $entry.sha256){throw ('Pinned notice checkout bytes changed: '+$entry.path)}
 $count++
}
$supplementalRoot=Join-Path $root 'third_party/supplemental'
$supplemental=Get-Content -LiteralPath (Join-Path $supplementalRoot 'provenance.json') -Raw|ConvertFrom-Json
if((Get-FileHash -LiteralPath (Join-Path $supplementalRoot $supplemental.file) -Algorithm SHA256).Hash -ine $supplemental.sha256){throw 'Supplemental GPL license checkout bytes changed.'}
Write-Output ('PASS: '+$count+' pinned OBS/dependency notices and the supplemental GPL3 text retain their original SHA-256 digests.')
