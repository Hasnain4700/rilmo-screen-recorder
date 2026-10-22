[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$AppDirectory,
    [string]$RuntimeDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'vendor\obs\32.2.2')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$app = [IO.Path]::GetFullPath($AppDirectory).TrimEnd('\')
$runtime = [IO.Path]::GetFullPath($RuntimeDirectory).TrimEnd('\')
if (-not (Test-Path -LiteralPath $app -PathType Container)) { throw "Application output directory is missing: $app" }
$inventoryFile = Join-Path $runtime 'runtime-inventory.json'
if (-not (Test-Path -LiteralPath $inventoryFile -PathType Leaf)) { throw 'Audited native runtime is missing. Run tools/prepare-runtime.ps1 first.' }
$inventory = Get-Content -LiteralPath $inventoryFile -Raw | ConvertFrom-Json
if ($inventory.runtimeVersion -ne '32.2.2' -or @($inventory.files).Count -eq 0) { throw 'Native runtime inventory does not match the supported OBS 32.2.2 baseline.' }
if ((Get-Item -LiteralPath $runtime).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Runtime directory cannot be a reparse point.' }
$plan = [Collections.Generic.List[object]]::new()
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $inventory.files) {
    $relative = [string]$entry.path
    if ([IO.Path]::IsPathRooted($relative) -or $relative -match '(^|[\\/])\.\.([\\/]|$)') { throw "Invalid runtime inventory path: $relative" }
    $source = [IO.Path]::GetFullPath((Join-Path $runtime $relative))
    if (-not $source.StartsWith($runtime + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime source escaped its root.' }
    if (-not $seen.Add($source)) { throw "Duplicate runtime inventory path: $relative" }
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Runtime file is missing: $relative" }
    if ((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Runtime file cannot be a reparse point: $relative" }
    if ($entry.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ine $entry.sha256) { throw "Runtime SHA-256 mismatch: $relative" }
    $placement = if ($relative.StartsWith('bin/64bit/', [StringComparison]::OrdinalIgnoreCase)) { $relative.Substring(10) } else { 'runtime/obs/' + $relative }
    if ($placement -match '^(?:bin|\.\.)[/\\]') { throw 'Unexpected native binary placement.' }
    $destination = [IO.Path]::GetFullPath((Join-Path $app $placement))
    if (-not $destination.StartsWith($app + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime destination escaped application output.' }
    $plan.Add([ordered]@{ source=$source; destination=$destination; path=$placement; sha256=$entry.sha256; origin=$entry.origin })
}
foreach ($file in Get-ChildItem -LiteralPath $runtime -Recurse -File) {
    if ($file.FullName -ne $inventoryFile -and -not $seen.Contains($file.FullName)) { throw "Uninventoried runtime file: $($file.FullName)" }
}
# Validate the complete source set before changing application output. Publish uses
# a new folder; ordinary builds can reuse identical native files without recopying.
foreach ($item in $plan) {
    [IO.Directory]::CreateDirectory((Split-Path -Parent $item.destination)) | Out-Null
    if (Test-Path -LiteralPath $item.destination) {
        if ((Get-Item -LiteralPath $item.destination).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Destination cannot be a reparse point: $($item.path)" }
        if ((Get-FileHash -LiteralPath $item.destination -Algorithm SHA256).Hash -ieq $item.sha256) { continue }
    }
    Copy-Item -LiteralPath $item.source -Destination $item.destination -Force
    if ((Get-FileHash -LiteralPath $item.destination -Algorithm SHA256).Hash -ine $item.sha256) { throw "Staged runtime verification failed: $($item.path)" }
}
$stagedInventory = Join-Path $app 'runtime\obs\runtime-inventory.json'
Copy-Item -LiteralPath $inventoryFile -Destination $stagedInventory -Force
$receipt = [ordered]@{
    runtimeVersion='32.2.2'; layoutVersion=1; acquisitionMode=$inventory.acquisitionMode
    sourceInventorySha256=(Get-FileHash -LiteralPath $inventoryFile -Algorithm SHA256).Hash.ToLowerInvariant()
    stagedAtUtc=[DateTime]::UtcNow.ToString('o'); files=@($plan | ForEach-Object { [ordered]@{path=$_.path;sha256=$_.sha256;origin=$_.origin} })
    correspondingSourceComplete=$inventory.correspondingSourceComplete; publicDistributionApproved=$inventory.publicDistributionApproved
}
$receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $app 'runtime-staging.json') -Encoding utf8
Write-Output "Staged $($plan.Count) audited runtime files into $app. Native helpers and their DLLs are app-local; no installed OBS is used."
