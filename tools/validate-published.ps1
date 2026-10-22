[CmdletBinding()]
param([string]$AppDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$workspace=Split-Path $PSScriptRoot -Parent
if (!$AppDirectory) { $AppDirectory=Split-Path (& (Join-Path $PSScriptRoot 'launch-published.ps1') -ResolveOnly) -Parent }
$AppDirectory=[IO.Path]::GetFullPath($AppDirectory).TrimEnd('\')
$exe=Join-Path $AppDirectory 'Rilmo Recorder.exe'
$expectedVersion = ([xml](Get-Content -LiteralPath (Join-Path $workspace 'src\Rilmo.Recorder.csproj') -Raw)).SelectSingleNode('/Project/PropertyGroup/Version').InnerText
$expectedFileVersion = ([Version]($expectedVersion + '.0')).ToString()
if ((Get-Item -LiteralPath $exe).VersionInfo.FileVersion -ne $expectedFileVersion) { throw "Expected Rilmo $expectedFileVersion." }
$assemblyBytes=[IO.File]::ReadAllBytes((Join-Path $AppDirectory 'Rilmo Recorder.dll'))
$metadataText=[Text.Encoding]::ASCII.GetString($assemblyBytes)
foreach($testMethod in @('RunMatchedBenchmark','RunAutomatedSmoke','ValidatePreferencesAndLayout')) { if($metadataText.Contains($testMethod)){throw "Test-only method shipped: $testMethod"} }
if((Get-FileHash -LiteralPath (Join-Path $AppDirectory 'licenses\DoppioOne-OFL.txt')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $workspace 'assets\fonts\OFL.txt')).Hash){throw 'Bundled font license differs.'}
$out=Join-Path $workspace ('artifacts\published-validation\'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $out -Force | Out-Null
function Verify-Payload($Entries,[string]$RootPath) {
    $RootPath=[IO.Path]::GetFullPath($RootPath).TrimEnd('\')
    foreach($entry in $Entries) {
        $file=[IO.Path]::GetFullPath((Join-Path $RootPath ([string]$entry.path)))
        if (!$file.StartsWith($RootPath+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Inventory path escaped its root.' }
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $entry.sha256) { throw "Published payload mismatch: $file" }
    }
}
$core=Get-Content -LiteralPath (Join-Path $AppDirectory 'runtime-staging.json') -Raw | ConvertFrom-Json
$browser=Get-Content -LiteralPath (Join-Path $AppDirectory 'runtime\browser\browser-runtime-inventory.json') -Raw | ConvertFrom-Json
Verify-Payload $core.files $AppDirectory
Verify-Payload $browser.files (Join-Path $AppDirectory 'runtime\browser')
$camera=Get-Content -LiteralPath (Join-Path $AppDirectory 'virtual-camera\source-provenance.json') -Raw | ConvertFrom-Json
foreach($binary in $camera.binaries) {
    $relative=if($binary.name -eq 'rilmo-virtual-camera64.dll'){$binary.name}else{'virtual-camera\'+$binary.name}
    Verify-Payload @([pscustomobject]@{path=$relative;sha256=$binary.sha256}) $AppDirectory
}
$renders=foreach($page in @('record','scene','camera')) {
    $image=Join-Path $out ($page+'.png')
    $start=[Diagnostics.ProcessStartInfo]::new($exe)
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true; $start.WindowStyle='Hidden'; $start.WorkingDirectory=$AppDirectory
    foreach($argument in @('--render-ui',$image,'1040','720',$page)) { $start.ArgumentList.Add($argument) }
    $start.Environment['DOTNET_ROOT']=Join-Path $out 'no-external-dotnet'
    $start.Environment['DOTNET_ROOT_X64']=$start.Environment['DOTNET_ROOT']
    $start.Environment['PATH']=Join-Path $env:WINDIR 'System32'
    $process=[Diagnostics.Process]::Start($start)
    $runtimePath=$null; $deadline=[DateTime]::UtcNow.AddSeconds(20)
    try {
        while (!$process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
            if (!$runtimePath) {
                try { $process.Refresh(); $runtimePath=($process.Modules | Where-Object ModuleName -ieq 'coreclr.dll' | Select-Object -First 1).FileName } catch {}
            }
            $null=$process.WaitForExit(50)
        }
        if (!$process.HasExited) { $process.Kill($true); throw "Published $page render timed out." }
        if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $image)) { throw "Published $page render failed." }
        if (!$runtimePath -or [IO.Path]::GetFullPath($runtimePath) -ine (Join-Path $AppDirectory 'coreclr.dll')) { throw 'The bundled .NET runtime was not observed.' }
        [ordered]@{page=$page;image=$image;exitCode=$process.ExitCode;coreClr=$runtimePath;externalDotNetRootDisabled=$true}
    } finally {$process.Dispose()}
}
$size=(Get-ChildItem -LiteralPath $AppDirectory -File -Recurse | Measure-Object Length -Sum).Sum
[ordered]@{passed=$true;appDirectory=$AppDirectory;version=$expectedFileVersion;coreFiles=@($core.files).Count;browserFiles=@($browser.files).Count;cameraComponents=@($camera.binaries).Count;folderBytes=$size;renders=@($renders);scope='Production apphost, bundled .NET runtime, exact staged native hashes and read-only UI renders. Does not certify clean-machine installation, recording hardware, or Store approval.'} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $out 'result.json') -Encoding utf8
Write-Output "Published validation passed: $out"
