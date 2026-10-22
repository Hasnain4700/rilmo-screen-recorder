#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$noticeRoot = Join-Path $workspace 'third_party\obs-32.2.2'
[IO.Directory]::CreateDirectory($noticeRoot) | Out-Null
$repositories = @(
    @{ repository='obsproject/obs-studio'; commit='ba2f32bdf791005443988a4955e963663e16b1ed'; prefix='obs'; paths=@('COPYING','deps/blake2/LICENSE.blake2','deps/json11/LICENSE.txt','deps/libcaption/LICENSE.txt','deps/w32-pthreads/COPYING','deps/w32-pthreads/COPYING.LIB','libobs/graphics/libnsgif/LICENSE.libnsgif','plugins/obs-filters/rnnoise/COPYING','plugins/obs-outputs/librtmp/COPYING','plugins/obs-qsv11/obs-qsv11-LICENSE.txt','plugins/obs-qsv11/QSV11-License-Clarification-Email.txt','shared/media-playback/LICENSE.media-playback','CMakePresets.json') },
    @{ repository='obsproject/obs-deps'; commit='8683107a02300923abe4f293920f4b5edc8cb624'; prefix='obs-deps'; paths=@() }
)
$requests = foreach ($repository in $repositories) {
    $tree = Invoke-RestMethod -Uri ('https://api.github.com/repos/' + $repository.repository + '/git/trees/' + $repository.commit + '?recursive=1')
    if ($tree.truncated) { throw 'Upstream source tree response was truncated.' }
    foreach ($entry in $tree.tree) {
        if ($entry.type -ne 'blob') { continue }
        $selected = if ($repository.prefix -eq 'obs') { $repository.paths -contains $entry.path } else { $entry.path.StartsWith('licenses/') -or $entry.path -eq 'LICENSE' }
        if ($selected) { [pscustomobject]@{ repository=$repository.repository;commit=$repository.commit;path=$entry.path;gitBlobSha1=$entry.sha;relative=$repository.prefix + '/' + $entry.path } }
    }
}
$results = $requests | ForEach-Object -Parallel {
    $ErrorActionPreference = 'Stop'
    $request = $_
    $destination = Join-Path $using:noticeRoot $request.relative
    [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
    $url = 'https://raw.githubusercontent.com/' + $request.repository + '/' + $request.commit + '/' + $request.path
    if (-not (Test-Path -LiteralPath $destination)) { Invoke-WebRequest -Uri $url -OutFile $destination -TimeoutSec 60 -MaximumRetryCount 2 | Out-Null }
    $bytes = [IO.File]::ReadAllBytes($destination)
    $header = [Text.Encoding]::ASCII.GetBytes(('blob ' + $bytes.Length + [char]0))
    $sha1 = [Security.Cryptography.SHA1]::Create()
    try {
        $null = $sha1.TransformBlock($header,0,$header.Length,$header,0)
        $null = $sha1.TransformFinalBlock($bytes,0,$bytes.Length)
        $actual = [Convert]::ToHexString($sha1.Hash).ToLowerInvariant()
        if ($actual -ne $request.gitBlobSha1) { throw "Upstream Git blob checksum mismatch: $($request.relative)" }
    } finally { $sha1.Dispose() }
    [pscustomobject]@{ path=$request.relative;sourceUrl=$url;gitBlobSha1=$actual;sha256=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() }
} -ThrottleLimit 8
if (@($results).Count -ne @($requests).Count) { throw 'One or more upstream notice fetches failed; do not use an incomplete collection.' }
[ordered]@{
    obsVersion='32.2.2';obsCommit=$repositories[0].commit;obsDepsVersion='2026-07-15';obsDepsCommit=$repositories[1].commit
    description='Verbatim upstream notice collection and OBS dependency selection manifest; broader than the selected binary runtime. This is not a complete corresponding-source archive or a declaration that every listed component is shipped.'
    files=@($results | Sort-Object path)
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $noticeRoot 'source-notices.json') -Encoding utf8
Write-Output "Verified $(@($results).Count) upstream notice/provenance files in $noticeRoot."
