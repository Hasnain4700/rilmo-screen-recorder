[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$AppDirectory,
    [string]$RuntimeDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'vendor\obs-browser\32.2.2')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$app = [IO.Path]::GetFullPath($AppDirectory).TrimEnd('\')
$runtime = [IO.Path]::GetFullPath($RuntimeDirectory).TrimEnd('\')
if (-not (Test-Path -LiteralPath $app -PathType Container)) { throw 'Application output directory is missing.' }
$inventoryFile = Join-Path $runtime 'browser-runtime-inventory.json'
$inventory = Get-Content -LiteralPath $inventoryFile -Raw | ConvertFrom-Json
if ($inventory.runtimeVersion -ne '32.2.2' -or $inventory.browserRevision -ne '3f0a2cdf378939ebe3c6f9ab36d4ea100c25aac2' -or @($inventory.files).Count -eq 0) { throw 'Browser inventory does not match the supported OBS 32.2.2 baseline.' }
function Assert-NoReparseAncestor([string]$Path) {
    $current = $Path
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "A staging path contains a reparse point: $current" }
        }
        $next = Split-Path -Parent $current
        if ($next -eq $current) { break }
        $current = $next
    }
}
Assert-NoReparseAncestor $app
Assert-NoReparseAncestor $runtime
$plan = [Collections.Generic.List[object]]::new()
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $inventory.files) {
    $relative = [string]$entry.path
    if ([IO.Path]::IsPathRooted($relative) -or $relative -match '(^|[\\/])\.\.([\\/]|$)') { throw "Invalid browser inventory path: $relative" }
    $source = [IO.Path]::GetFullPath((Join-Path $runtime $relative))
    $destination = [IO.Path]::GetFullPath((Join-Path $app ('runtime/browser/' + $relative)))
    if (-not $source.StartsWith($runtime + '\', [StringComparison]::OrdinalIgnoreCase) -or -not $destination.StartsWith($app + '\runtime\browser\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Browser staging path escaped its root.' }
    if (-not $seen.Add($source)) { throw "Duplicate browser inventory path: $relative" }
    Assert-NoReparseAncestor $source
    Assert-NoReparseAncestor $destination
    if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or $entry.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ine $entry.sha256) { throw "Browser SHA-256 mismatch or missing file: $relative" }
    $plan.Add([ordered]@{source=$source;destination=$destination;sha256=$entry.sha256})
}
foreach ($file in Get-ChildItem -LiteralPath $runtime -Recurse -File) {
    if ($file.FullName -ne $inventoryFile -and -not $seen.Contains($file.FullName)) { throw "Uninventoried browser file: $($file.FullName)" }
}
# Validate the complete component before any output mutation. Ordinary rebuilds
# reuse identical files. No machine registration or installed OBS path is used.
foreach ($item in $plan) {
    [IO.Directory]::CreateDirectory((Split-Path -Parent $item.destination)) | Out-Null
    if ((Test-Path -LiteralPath $item.destination) -and (Get-FileHash -LiteralPath $item.destination -Algorithm SHA256).Hash -ieq $item.sha256) { continue }
    Copy-Item -LiteralPath $item.source -Destination $item.destination -Force
    if ((Get-FileHash -LiteralPath $item.destination -Algorithm SHA256).Hash -ine $item.sha256) { throw 'Staged browser verification failed.' }
}
Copy-Item -LiteralPath $inventoryFile -Destination (Join-Path $app 'runtime\browser\browser-runtime-inventory.json') -Force
Write-Output "Staged $($plan.Count) audited optional browser files at $app\runtime\browser."
