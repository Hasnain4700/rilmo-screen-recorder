[CmdletBinding()]
param(
    [switch]$AllowInstalledFallback,
    [switch]$NoDownload,
    [string]$InstalledRoot = (Join-Path $env:ProgramFiles 'obs-studio'),
    [string]$VcRuntimeDirectory,
    [string]$Destination
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$workspace = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if (-not $Destination) { $Destination = Join-Path $workspace 'vendor\obs\32.2.2' }
$Destination = [IO.Path]::GetFullPath($Destination)
if (-not $Destination.StartsWith($workspace.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime destination must stay inside this workspace.' }
if (Test-Path -LiteralPath $Destination) { throw "Runtime destination already exists; it is preserved: $Destination. Choose a new destination for reacquisition." }
$downloads = Join-Path $workspace '.tools\downloads'
[IO.Directory]::CreateDirectory($downloads) | Out-Null
$version = '32.2.2'
$archiveName = 'OBS-Studio-32.2.2-Windows-x64.zip'
$archiveUrl = 'https://github.com/obsproject/obs-studio/releases/download/32.2.2/' + $archiveName
$archiveSha256 = '4d6e40e3ab155f56b30de517380566a206d74b63cdf5ad49aa596924768f97e1'
$archive = Join-Path $downloads $archiveName
$acquisitionMode = 'official-portable-archive'
if (-not (Test-Path -LiteralPath $archive) -and -not $NoDownload) {
    $partial = $archive + '.partial'
    Write-Output 'Downloading the pinned official OBS 32.2.2 Windows x64 portable archive (no installer runs).'
    & curl.exe --fail --location --retry 3 --connect-timeout 30 --continue-at - --output $partial $archiveUrl
    if ($LASTEXITCODE -ne 0) { throw 'Official OBS download failed. Partial download is preserved for resumption.' }
    if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ine $archiveSha256) { throw 'Official OBS archive failed pinned SHA-256 verification; nothing was extracted.' }
    Move-Item -LiteralPath $partial -Destination $archive
}
if (Test-Path -LiteralPath $archive) {
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ine $archiveSha256) { throw 'Cached OBS archive failed pinned SHA-256 verification.' }
    $source = Join-Path $downloads ('obs-extracted-' + [Guid]::NewGuid().ToString('N'))
    Expand-Archive -LiteralPath $archive -DestinationPath $source
} elseif ($AllowInstalledFallback) {
    $source = [IO.Path]::GetFullPath($InstalledRoot)
    $acquisitionMode = 'installed-runtime-fallback'
    Write-Output 'Using explicitly authorized local OBS fallback; every selected file receives a hash and provenance record. This does not approve public redistribution.'
} else { throw 'Verified official archive is unavailable. Download it, or explicitly select -NoDownload -AllowInstalledFallback for a local inventoried build.' }
$obsLibrary = Join-Path $source 'bin\64bit\obs.dll'
if (-not (Test-Path -LiteralPath $obsLibrary)) { throw 'OBS source is missing bin/64bit/obs.dll.' }
$obsFileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($obsLibrary).ProductVersion
if ($obsFileVersion -notmatch '^32\.2\.2(?:\D|$)') { throw "Runtime must be OBS 32.2.2, found '$obsFileVersion'." }
if (-not $VcRuntimeDirectory) {
    if ($AllowInstalledFallback) { $VcRuntimeDirectory = [Environment]::SystemDirectory; $vcMode = 'installed-system-fallback' }
    else { throw 'Supply -VcRuntimeDirectory pointing to an audited x64 Microsoft VC++ redistributable directory. The OBS portable ZIP does not supply the VC++ runtime.' }
} else { $vcMode = 'explicit-redist-directory' }
$VcRuntimeDirectory = [IO.Path]::GetFullPath($VcRuntimeDirectory)
if (-not ('RilmoPeImports' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'PeImports.cs') }
$modules = @('win-capture','win-wasapi','obs-ffmpeg','obs-x264','obs-nvenc','obs-qsv11','win-dshow','image-source','obs-text','obs-filters','rtmp-services','obs-outputs','obs-transitions')
$helpers = @('obs-ffmpeg-mux.exe','obs-amf-test.exe','obs-qsv-test.exe','obs-nvenc-test.exe')
$nativeSeeds = @('obs.dll','libobs-d3d11.dll','libobs-winrt.dll') + $helpers
$stage = $Destination + '.stage-' + [Guid]::NewGuid().ToString('N')
[IO.Directory]::CreateDirectory($stage) | Out-Null
$origins = @{}
$queue = [Collections.Generic.Queue[string]]::new()
$visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$systemImports = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$imports = [Collections.Generic.List[object]]::new()
function Copy-RuntimeFile([string]$InputPath, [string]$Relative, [string]$Origin) {
    $relativeNormalized = $Relative.Replace('\', '/')
    $target = [IO.Path]::GetFullPath((Join-Path $stage $Relative))
    if (-not $target.StartsWith($stage + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Runtime path escaped staging: $Relative" }
    if ($origins.ContainsKey($relativeNormalized)) { return }
    if (-not (Test-Path -LiteralPath $InputPath -PathType Leaf)) { throw "Required runtime file missing: $InputPath" }
    if ((Get-Item -LiteralPath $InputPath).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Runtime source contains a reparse point: $InputPath" }
    [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
    Copy-Item -LiteralPath $InputPath -Destination $target
    $origins[$relativeNormalized] = $Origin
    if ([IO.Path]::GetExtension($target) -in @('.dll','.exe')) { $queue.Enqueue($relativeNormalized) }
}
foreach ($name in $nativeSeeds) { Copy-RuntimeFile (Join-Path $source ('bin\64bit\' + $name)) ('bin/64bit/' + $name) $acquisitionMode }
foreach ($module in $modules) { Copy-RuntimeFile (Join-Path $source ('obs-plugins\64bit\' + $module + '.dll')) ('obs-plugins/64bit/' + $module + '.dll') $acquisitionMode }
foreach ($relativeDirectory in (@('data/libobs') + @($modules | ForEach-Object { 'data/obs-plugins/' + $_ }))) {
    $directory = Join-Path $source $relativeDirectory
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "Required runtime data missing: $relativeDirectory" }
    foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File) {
        if ($file.Extension -eq '.pdb') { continue }
        $relative = $file.FullName.Substring($source.TrimEnd('\').Length + 1).Replace('\','/')
        Copy-RuntimeFile $file.FullName $relative $acquisitionMode
    }
}
while ($queue.Count -gt 0) {
    $relative = $queue.Dequeue()
    if (-not $visited.Add($relative)) { continue }
    $file = Join-Path $stage $relative
    $machine = [RilmoPeImports]::Machine($file)
    $dllNames = [RilmoPeImports]::Read($file)
    $imports.Add([ordered]@{ path=$relative; machine=$machine; imports=@($dllNames) })
    foreach ($dll in $dllNames) {
        if ($dll -match '^(api-ms-|ext-ms-)') { $null = $systemImports.Add($dll); continue }
        $binCandidate = Join-Path $source ('bin\64bit\' + $dll)
        $pluginCandidate = Join-Path $source ('obs-plugins\64bit\' + $dll)
        if ($machine -eq 34404 -and (Test-Path -LiteralPath $binCandidate -PathType Leaf)) {
            if ($dll -match '^(Qt|libcef|chrome_|obs-frontend|obs-scripting|lua)') { throw "Unexpected UI/scripting dependency $dll in $relative." }
            Copy-RuntimeFile $binCandidate ('bin/64bit/' + $dll) $acquisitionMode
        } elseif ($machine -eq 34404 -and (Test-Path -LiteralPath $pluginCandidate -PathType Leaf)) {
            Copy-RuntimeFile $pluginCandidate ('obs-plugins/64bit/' + $dll) $acquisitionMode
        } elseif ($dll -match '^(msvcp140|vcruntime140|concrt140).*\.dll$') {
            if ($machine -ne 34404) { throw "An x86 VC++ dependency needs a separate audited redist layout: $dll in $relative" }
            $vcFile = Join-Path $VcRuntimeDirectory $dll
            if (-not (Test-Path -LiteralPath $vcFile -PathType Leaf)) { throw "Missing app-local VC++ runtime: $vcFile" }
            if ([RilmoPeImports]::Machine($vcFile) -ne 34404) { throw "VC++ dependency must be x64: $vcFile" }
            $signature = Get-AuthenticodeSignature -LiteralPath $vcFile
            if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw "VC++ runtime lacks a valid Microsoft signature: $vcFile ($($signature.Status))" }
            Copy-RuntimeFile $vcFile ('bin/64bit/' + $dll) $vcMode
        } else {
            $systemDirectory = if ($machine -eq 332) { Join-Path $env:WINDIR 'SysWOW64' } else { [Environment]::SystemDirectory }
            $systemFile = Join-Path $systemDirectory $dll
            if (-not (Test-Path -LiteralPath $systemFile -PathType Leaf)) { throw "Unresolved native import $dll in $relative; not supplied by the audited runtime or Windows." }
            $null = $systemImports.Add($dll)
        }
    }
}
Copy-RuntimeFile (Join-Path $workspace 'LICENSE') 'licenses/OBS-GPL-2.0.txt' 'repository-license-copied-verbatim-from-OBS-32.2.2'
Copy-RuntimeFile (Join-Path $workspace 'THIRD-PARTY-NOTICES.md') 'licenses/THIRD-PARTY-NOTICES.md' 'project-notices'
$noticeRoot = Join-Path $workspace 'third_party\obs-32.2.2'
$noticeManifest = Join-Path $noticeRoot 'source-notices.json'
if (-not (Test-Path -LiteralPath $noticeManifest -PathType Leaf)) { throw 'Fetch the pinned upstream notices first with tools/fetch-runtime-notices.ps1.' }
$noticeCollection = Get-Content -LiteralPath $noticeManifest -Raw | ConvertFrom-Json
foreach ($notice in $noticeCollection.files) {
    $noticeFile = [IO.Path]::GetFullPath((Join-Path $noticeRoot $notice.path))
    if (-not $noticeFile.StartsWith($noticeRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Notice path escaped the collection.' }
    if ((Get-FileHash -LiteralPath $noticeFile -Algorithm SHA256).Hash -ine $notice.sha256) { throw "Notice collection hash mismatch: $($notice.path)" }
    Copy-RuntimeFile $noticeFile ('licenses/upstream/' + $notice.path) 'verified-upstream-license-collection'
}
Copy-RuntimeFile $noticeManifest 'licenses/upstream/source-notices.json' 'verified-upstream-notice-provenance'
$provenance = [ordered]@{
    runtimeVersion=$version; acquisitionMode=$acquisitionMode; sourceDirectory=$source; vcRuntimeMode=$vcMode; vcRuntimeDirectory=$VcRuntimeDirectory
    releaseUrl='https://github.com/obsproject/obs-studio/releases/tag/32.2.2'; officialArchiveUrl=$archiveUrl; officialArchiveSha256=$archiveSha256
    officialArchiveVerified=($acquisitionMode -eq 'official-portable-archive'); sourceTagUrl='https://github.com/obsproject/obs-studio/tree/32.2.2'; sourceCommit='ba2f32bdf791005443988a4955e963663e16b1ed'
    obsDepsVersion='2026-07-15';obsDepsCommit='8683107a02300923abe4f293920f4b5edc8cb624'
    upstreamSourcesUrl='https://github.com/obsproject/obs-studio/releases/download/32.2.2/OBS-Studio-32.2.2-Sources.tar.gz'
    upstreamSourcesSha256='ec81fb66b03e75ddb3076b576f62679c39262e0e9960cef3e17a40dc5d68e6b4'
    preparedAtUtc=[DateTime]::UtcNow.ToString('o'); selectedModules=$modules; nativeHelpers=$helpers
    explicitDynamicLibraries=@('libobs-d3d11.dll','libobs-winrt.dll'); windowsImports=@($systemImports | Sort-Object)
    correspondingSourceComplete=$false; publicDistributionApproved=$false
    limitations=@('Pinned archive digest comes from the official GitHub release API; a local fallback is not asserted to match that archive.','Native static/delay import closure is recorded; GPU driver libraries remain supplied by Windows/vendor drivers.','Complete transitive license notices, corresponding dependency sources/build scripts, and VC++ redistribution entitlement must be reviewed before public distribution.','This runtime does not include OBS UI, Qt, CEF/browser, virtual camera registration, or third-party plugin installation.')
}
$provenance | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stage 'provenance.json') -Encoding utf8
$origins['provenance.json'] = 'generated-provenance'
$imports | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $stage 'native-imports.json') -Encoding utf8
$origins['native-imports.json'] = 'generated-static-and-delay-import-audit'
$files = @(Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($stage.Length + 1).Replace('\','/')
    [ordered]@{ path=$relative; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); size=$_.Length; origin=$origins[$relative] }
} | Sort-Object { $_.path })
$inventory = [ordered]@{ runtimeVersion=$version; buildRevision='ba2f32bdf791005443988a4955e963663e16b1ed'; acquisitionMode=$acquisitionMode; correspondingSourceComplete=$false; publicDistributionApproved=$false; files=$files; components=@([ordered]@{name='OBS Studio/libobs and selected modules';version=$version;license='GPL-2.0-or-later';sourceUrl='https://github.com/obsproject/obs-studio/tree/32.2.2';licenseFiles=@('licenses/OBS-GPL-2.0.txt')}) }
$inventory | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stage 'runtime-inventory.json') -Encoding utf8
if (Test-Path -LiteralPath $Destination) { throw 'Runtime destination appeared while preparing; staged files were preserved.' }
[IO.Directory]::CreateDirectory((Split-Path -Parent $Destination)) | Out-Null
Move-Item -LiteralPath $stage -Destination $Destination
$totalBytes = ($files | ForEach-Object { [long]$_['size'] } | Measure-Object -Sum).Sum
Write-Output ("Runtime prepared: {0}; {1} files, {2:N1} MiB; source mode {3}." -f $Destination,$files.Count,($totalBytes / 1MB),$acquisitionMode)
