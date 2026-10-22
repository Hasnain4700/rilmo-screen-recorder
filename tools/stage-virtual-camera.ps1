param([Parameter(Mandatory=$true)][string]$AppDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$native=Join-Path $root 'native/virtual-camera'
$manifestPath=Join-Path $native 'source-provenance.json'
if(-not(Test-Path -LiteralPath $manifestPath)){throw 'Build the independent virtual camera components first with tools/build-virtual-camera.ps1.'}
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if(@($manifest.binaries).Count -ne 2 -or @($manifest.binaries.name | Select-Object -Unique).Count -ne 2){throw 'Both unique camera architectures are required.'}
foreach($entry in $manifest.modifiedAndOriginalSources){
    if($entry.path -notmatch '^src/[A-Za-z0-9_.-]+$'){throw 'Invalid camera source manifest path.'}
    if((Get-FileHash -LiteralPath (Join-Path $native $entry.path) -Algorithm SHA256).Hash -ine $entry.sha256){throw 'Camera source has changed since its native build. Rebuild both architectures.'}
}
$destination=Join-Path $AppDirectory 'virtual-camera'
$toolsDestination=Join-Path $AppDirectory 'tools'
New-Item -ItemType Directory -Force -Path $destination,$toolsDestination | Out-Null
foreach($binary in $manifest.binaries){
    if($binary.name -notin @('rilmo-virtual-camera64.dll','rilmo-virtual-camera32.dll')){throw 'Unexpected camera component in manifest.'}
    $source=Join-Path (Join-Path $native 'bin') $binary.name
    if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ine $binary.sha256){throw 'Camera component provenance hash mismatch.'}
    $target=if($binary.name -eq 'rilmo-virtual-camera64.dll'){Join-Path $AppDirectory $binary.name}else{Join-Path $destination $binary.name}
    Copy-Item -LiteralPath $source -Destination $target -Force
}
foreach($file in @('source-provenance.json','README.md')){Copy-Item -LiteralPath (Join-Path $native $file) -Destination $destination -Force}
Copy-Item -LiteralPath (Join-Path $native 'LICENSES') -Destination $destination -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'tools/setup-virtual-camera.ps1') -Destination $toolsDestination -Force
Write-Output 'Staged independent Rilmo virtual camera components and reviewable setup. No device was registered.'
