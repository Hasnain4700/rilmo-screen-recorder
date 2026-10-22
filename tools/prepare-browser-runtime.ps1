[CmdletBinding()]
param(
    [switch]$AllowInstalledFallback,
    [switch]$NoDownload,
    [string]$InstalledRoot = (Join-Path $env:ProgramFiles 'obs-studio'),
    [string]$CoreRuntimeDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'vendor\obs\32.2.2'),
    [string]$VcRuntimeDirectory,
    [string]$Destination
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$workspace = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if (-not $Destination) { $Destination = Join-Path $workspace 'vendor\obs-browser\32.2.2' }
$Destination = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
if (-not $Destination.StartsWith($workspace.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Browser destination must stay inside this workspace.' }
if (Test-Path -LiteralPath $Destination) { throw "Destination already exists and is preserved: $Destination. Choose a new destination for reacquisition." }
$archiveUrl = 'https://github.com/obsproject/obs-studio/releases/download/32.2.2/OBS-Studio-32.2.2-Windows-x64.zip'
$archiveSha256 = '4d6e40e3ab155f56b30de517380566a206d74b63cdf5ad49aa596924768f97e1'
$downloads = Join-Path $workspace '.tools\downloads'
[IO.Directory]::CreateDirectory($downloads) | Out-Null
$archive = Join-Path $downloads 'OBS-Studio-32.2.2-Windows-x64.zip'
if (-not (Test-Path -LiteralPath $archive) -and -not $NoDownload) {
    $partial = $archive + '.partial'
    & curl.exe --fail --location --retry 3 --connect-timeout 30 --continue-at - --output $partial $archiveUrl
    if ($LASTEXITCODE -ne 0) { throw 'Official OBS download failed; the partial archive is preserved.' }
    if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ine $archiveSha256) { throw 'OBS archive SHA-256 verification failed; no files were extracted.' }
    Move-Item -LiteralPath $partial -Destination $archive
}
$mode = 'official-portable-archive'
if (Test-Path -LiteralPath $archive) {
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ine $archiveSha256) { throw 'Cached OBS archive SHA-256 verification failed.' }
    $source = Join-Path $downloads ('browser-extracted-' + [Guid]::NewGuid().ToString('N'))
    Expand-Archive -LiteralPath $archive -DestinationPath $source
} elseif ($AllowInstalledFallback) {
    $source = [IO.Path]::GetFullPath($InstalledRoot).TrimEnd('\')
    $mode = 'installed-runtime-fallback'
    Write-Output 'Using the explicitly selected installed OBS browser component. Its inventory records local provenance; it does not approve redistribution.'
} else { throw 'A verified OBS archive is required. For local development explicitly select -NoDownload -AllowInstalledFallback.' }
$obsPath = Join-Path $source 'bin\64bit\obs.dll'
if (-not (Test-Path -LiteralPath $obsPath) -or [Diagnostics.FileVersionInfo]::GetVersionInfo($obsPath).ProductVersion -notmatch '^32\.2\.2(?:\D|$)') { throw 'Browser runtime must come from OBS 32.2.2.' }
$core = [IO.Path]::GetFullPath($CoreRuntimeDirectory).TrimEnd('\')
$coreInventory = Get-Content -LiteralPath (Join-Path $core 'runtime-inventory.json') -Raw | ConvertFrom-Json
if ($coreInventory.runtimeVersion -ne '32.2.2') { throw 'Prepare the audited OBS 32.2.2 core runtime first.' }
$coreFiles = @{}
foreach ($entry in $coreInventory.files) { $coreFiles[[string]$entry.path] = $entry }
if (-not ('RilmoPeImports' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'PeImports.cs') }
$stage = $Destination + '.stage-' + [Guid]::NewGuid().ToString('N')
[IO.Directory]::CreateDirectory($stage) | Out-Null
$origins = @{}
$queue = [Collections.Generic.Queue[string]]::new()
$visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$systemImports = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$imports = [Collections.Generic.List[object]]::new()
function Copy-BrowserFile([string]$InputPath, [string]$Relative, [string]$Origin) {
    $relativeNormalized = $Relative.Replace('\','/')
    $target = [IO.Path]::GetFullPath((Join-Path $stage $Relative))
    if (-not $target.StartsWith($stage + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Browser file escaped staging.' }
    if ($origins.ContainsKey($relativeNormalized)) { return }
    if (-not (Test-Path -LiteralPath $InputPath -PathType Leaf)) { throw "Required browser file is missing: $InputPath" }
    if ((Get-Item -LiteralPath $InputPath).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Browser source cannot be a reparse point: $InputPath" }
    [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
    Copy-Item -LiteralPath $InputPath -Destination $target
    $origins[$relativeNormalized] = $Origin
    if ([IO.Path]::GetExtension($target) -in @('.dll','.exe')) { $queue.Enqueue($relativeNormalized) }
}
$pluginBin = Join-Path $source 'obs-plugins\64bit'
# These are the files shipped together by the pinned upstream obs-browser bundle.
# chrome_elf and ANGLE are dynamically loaded and therefore explicit seeds.
foreach ($name in @('obs-browser.dll','obs-browser-page.exe','libcef.dll','chrome_elf.dll','libEGL.dll','libGLESv2.dll','chrome_100_percent.pak','chrome_200_percent.pak','icudtl.dat','resources.pak','v8_context_snapshot.bin')) {
    Copy-BrowserFile (Join-Path $pluginBin $name) ('bin/' + $name) $mode
}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $pluginBin 'locales') -File) {
    Copy-BrowserFile $file.FullName ('bin/locales/' + $file.Name) $mode
}
$browserData = Join-Path $source 'data\obs-plugins\obs-browser'
foreach ($file in Get-ChildItem -LiteralPath $browserData -Recurse -File) {
    Copy-BrowserFile $file.FullName ('data/obs-browser/' + $file.FullName.Substring($browserData.Length + 1)) $mode
}
while ($queue.Count -gt 0) {
    $relative = $queue.Dequeue()
    if (-not $visited.Add($relative)) { continue }
    $file = Join-Path $stage $relative
    if ([RilmoPeImports]::Machine($file) -ne 34404) { throw "Browser binary is not Windows x64: $relative" }
    $names = [RilmoPeImports]::Read($file)
    $imports.Add([ordered]@{path=$relative;imports=@($names)})
    foreach ($dll in $names) {
        if ($dll -ieq 'obs.dll') { continue } # Supplied by the host's already audited core runtime.
        if ($dll -match '^(api-ms-|ext-ms-)') { $null = $systemImports.Add($dll); continue }
        $pluginCandidate = Join-Path $pluginBin $dll
        $binCandidate = Join-Path $source ('bin\64bit\' + $dll)
        if (Test-Path -LiteralPath $pluginCandidate -PathType Leaf) { Copy-BrowserFile $pluginCandidate ('bin/' + $dll) $mode }
        elseif (Test-Path -LiteralPath $binCandidate -PathType Leaf) { Copy-BrowserFile $binCandidate ('bin/' + $dll) $mode }
        elseif ($dll -match '^(msvcp140|vcruntime140|concrt140).*\.dll$') {
            $key = 'bin/64bit/' + $dll
            $candidate = Join-Path $core $key
            if ($coreFiles.ContainsKey($key)) {
                if (-not (Test-Path -LiteralPath $candidate) -or (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -ine $coreFiles[$key].sha256) { throw "Audited Microsoft runtime dependency hash mismatch: $dll" }
                Copy-BrowserFile $candidate ('bin/' + $dll) 'audited-core-runtime'
            } else {
                if (-not $VcRuntimeDirectory) {
                    if ($AllowInstalledFallback) { $VcRuntimeDirectory = [Environment]::SystemDirectory }
                    else { throw "Supply -VcRuntimeDirectory containing an audited x64 Microsoft redist for $dll." }
                }
                $candidate = Join-Path $VcRuntimeDirectory $dll
                if (-not (Test-Path -LiteralPath $candidate) -or [RilmoPeImports]::Machine($candidate) -ne 34404) { throw "Missing x64 Microsoft runtime: $candidate" }
                $signature = Get-AuthenticodeSignature -LiteralPath $candidate
                if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw "Microsoft runtime signature is invalid: $candidate" }
                Copy-BrowserFile $candidate ('bin/' + $dll) ('signature-verified-microsoft-runtime:' + [IO.Path]::GetFullPath($VcRuntimeDirectory))
            }
        } elseif (Test-Path -LiteralPath (Join-Path ([Environment]::SystemDirectory) $dll) -PathType Leaf) { $null = $systemImports.Add($dll) }
        else { throw "Unresolved browser import '$dll' required by $relative." }
    }
}
Copy-BrowserFile (Join-Path $workspace 'LICENSE') 'licenses/OBS-GPL-2.0-or-later.txt' 'repository-license'
$noticeRoot=Join-Path $workspace 'third_party\obs-32.2.2'
$noticeCollection=Get-Content -LiteralPath (Join-Path $noticeRoot 'source-notices.json') -Raw | ConvertFrom-Json
$qtNotices=@($noticeCollection.files | Where-Object {$_.path -like 'obs-deps/licenses/qt6/*'})
if($qtNotices.Count -eq 0){throw 'The pinned Qt notice collection is missing. Run tools/fetch-runtime-notices.ps1 first.'}
foreach($notice in $qtNotices){
    $noticePath=[IO.Path]::GetFullPath((Join-Path $noticeRoot $notice.path))
    if(-not $noticePath.StartsWith($noticeRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash -LiteralPath $noticePath -Algorithm SHA256).Hash -ine $notice.sha256){throw 'Pinned Qt notice verification failed.'}
    Copy-BrowserFile $noticePath ('licenses/qt6/'+[IO.Path]::GetFileName($noticePath)) 'verified-upstream-qt-notice'
}
$qtNotices | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $stage 'licenses\qt6\source-notices.json') -Encoding utf8
$origins['licenses/qt6/source-notices.json']='generated-qt-notice-provenance'
$provenance = [ordered]@{
    runtimeVersion='32.2.2'; obsRevision='ba2f32bdf791005443988a4955e963663e16b1ed'
    browserRevision='3f0a2cdf378939ebe3c6f9ab36d4ea100c25aac2'; acquisitionMode=$mode
    sourceRoot=$source; archiveUrl=$archiveUrl; expectedArchiveSha256=$archiveSha256
    archiveVerified=($mode -eq 'official-portable-archive'); preparedAtUtc=[DateTime]::UtcNow.ToString('o')
    coreInventorySha256=(Get-FileHash -LiteralPath (Join-Path $core 'runtime-inventory.json') -Algorithm SHA256).Hash.ToLowerInvariant()
    browserSource='https://github.com/obsproject/obs-browser/tree/3f0a2cdf378939ebe3c6f9ab36d4ea100c25aac2'
    cefDependency='CEF 6533, upstream windows-x64 revision 2'; cefArchiveSha256='922efbda1f2f8be9e5b2754d878a14d90afc81f04e94fc9101a7513e2b5cecc1'
    correspondingSourceComplete=$false; publicDistributionApproved=$false
    outstandingLicensing=@('Complete corresponding browser/core build sources and scripts','CEF/Chromium third-party notices and source obligations','Qt third-party notices, source and relinking obligations','Microsoft redistributable distribution terms')
    noObsFrontendExecutable=$true; includesQtAndFrontendApiSupportLibraries=$true
}
$provenance | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $stage 'provenance.json') -Encoding utf8
$origins['provenance.json']='generated-provenance'
[ordered]@{imports=@($imports);systemImports=@($systemImports | Sort-Object);hostProvidedImports=@('obs.dll')} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stage 'native-imports.json') -Encoding utf8
$origins['native-imports.json']='generated-import-audit'
$files = @(Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($stage.Length + 1).Replace('\','/')
    [ordered]@{path=$relative;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant();size=$_.Length;origin=$origins[$relative]}
})
[ordered]@{runtimeVersion='32.2.2';browserRevision=$provenance.browserRevision;layoutVersion=1;acquisitionMode=$mode;correspondingSourceComplete=$false;publicDistributionApproved=$false;files=$files} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stage 'browser-runtime-inventory.json') -Encoding utf8
[IO.Directory]::CreateDirectory((Split-Path -Parent $Destination)) | Out-Null
Move-Item -LiteralPath $stage -Destination $Destination
if(Test-Path -LiteralPath (Join-Path $workspace 'release\browser-source-inputs\manifest.json')){
    & (Join-Path $PSScriptRoot 'complete-browser-notices.ps1') -RuntimeDirectory $Destination
}
Write-Output "Prepared $($files.Count) browser runtime files at $Destination. This optional component is not loaded until a browser source is requested."
