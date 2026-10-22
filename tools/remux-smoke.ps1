[CmdletBinding()]
param(
    [string]$InputFile,
    [string]$FfprobePath
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
if (-not $InputFile) {
    $fixture = Get-ChildItem -LiteralPath (Join-Path $workspace 'artifacts\test-recordings') -Filter '*.mkv' -File | Where-Object Length -gt 1024 | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $fixture) { throw 'Supply -InputFile pointing to a finished test MKV, or run engine-smoke.ps1 first.' }
    $InputFile = $fixture.FullName
}
$InputFile = [System.IO.Path]::GetFullPath($InputFile)
if (-not (Test-Path -LiteralPath $InputFile -PathType Leaf)) { throw 'Input fixture does not exist.' }
if (-not $FfprobePath) {
    $command = Get-Command ffprobe.exe -ErrorAction SilentlyContinue
    if ($command) { $FfprobePath = $command.Source }
    else {
        $candidate = 'C:\REV\tools\ai\ffmpeg-lgpl\ffmpeg-master-latest-win64-lgpl\bin\ffprobe.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { $FfprobePath = $candidate }
    }
}
if (-not $FfprobePath -or -not (Test-Path -LiteralPath $FfprobePath -PathType Leaf)) { throw 'Supply an existing -FfprobePath for validation. This script downloads nothing; ffprobe is not an app dependency.' }
$runDirectory = Join-Path $workspace ('artifacts\remux-smoke\' + [Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($runDirectory) | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'The standalone test needs the installed x64 .NET Framework C# compiler.' }
$harnessText = @'
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Rilmo;

// The real package-relative RuntimeLocator is compiled without capture-engine code.
internal static class RemuxSmoke {
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static string Hash(string path) { using (SHA256 hash = SHA256.Create()) using (FileStream file = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(file)); }
    private static void ExpectFailure(Action operation, string name) { bool failed = false; try { operation(); } catch (IOException) { failed = true; } catch (InvalidDataException) { failed = true; } Require(failed, name + " was not rejected."); }
    public static int Main(string[] args) {
        try {
            string directory = args[1];
            string source = Path.Combine(directory, "Original caf\u00e9 \u0627\u0631\u062f\u0648.mkv");
            string output = Path.Combine(directory, "Export \u0646\u062a\u06cc\u062c\u06c1.mp4");
            File.Copy(args[0], source, false); string originalHash = Hash(source);
            double latest = -1; int progressCount = 0;
            Remuxer.RemuxAsync(source, output, delegate(double value) {
                Require(!Double.IsNaN(value) && !Double.IsInfinity(value) && value >= latest && value >= 0 && value <= 100, "Invalid progress value.");
                latest = value; progressCount++;
            }, CancellationToken.None).GetAwaiter().GetResult();
            Require(File.Exists(output) && new FileInfo(output).Length > 32, "Valid output was not produced.");
            Require(latest == 100 && progressCount > 0, "Completion progress missing.");
            Require(Hash(source) == originalHash, "The original recording changed.");
            Console.WriteLine("UNICODE_ASYNC_EXPORT=passed");

            string existing = Path.Combine(directory, "existing.mp4"); File.WriteAllText(existing, "KEEP THIS FILE");
            string sentinel = Hash(existing);
            ExpectFailure(delegate { Remuxer.Remux(source, existing, null, CancellationToken.None); }, "Existing output");
            Require(Hash(existing) == sentinel, "Existing output changed."); Console.WriteLine("EXISTING_OUTPUT_PRESERVED=passed");

            string cancelled = Path.Combine(directory, "cancelled.mp4");
            using (CancellationTokenSource cancel = new CancellationTokenSource()) {
                bool observed = false;
                try { Remuxer.Remux(source, cancelled, delegate(double percent) { cancel.Cancel(); }, cancel.Token); }
                catch (OperationCanceledException) { observed = true; }
                Require(observed && !File.Exists(cancelled), "In-flight cancellation created a final output.");
            }
            using (CancellationTokenSource cancel = new CancellationTokenSource()) {
                cancel.Cancel(); bool observed = false;
                try { Remuxer.Remux(source, cancelled, null, cancel.Token); } catch (OperationCanceledException) { observed = true; }
                Require(observed && !File.Exists(cancelled), "Pre-cancellation created output.");
            }
            Console.WriteLine("CANCELLATION_CLEANUP=passed");

            string invalid = Path.Combine(directory, "invalid.mkv"); File.WriteAllText(invalid, "This is not a media file.");
            string invalidOutput = Path.Combine(directory, "invalid.mp4");
            ExpectFailure(delegate { Remuxer.Remux(invalid, invalidOutput, null, CancellationToken.None); }, "Invalid input");
            Require(!File.Exists(invalidOutput), "Invalid input created final output."); Console.WriteLine("FAILED_CREATE_NO_DOUBLE_FREE=passed");

            string race = Path.Combine(directory, "destination-race.mp4"); bool wroteSentinel = false, readOnlyVerified = false;
            ExpectFailure(delegate { Remuxer.Remux(source, race, delegate(double value) {
                if (!wroteSentinel) {
                    File.WriteAllText(race, "CREATED DURING EXPORT"); wroteSentinel = true;
                    try { using (FileStream write = new FileStream(source, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { } }
                    catch (IOException) { readOnlyVerified = true; }
                }
            }, CancellationToken.None); }, "Destination race");
            Require(File.ReadAllText(race) == "CREATED DURING EXPORT", "Destination race overwrote a file.");
            Require(readOnlyVerified, "Source did not deny a simultaneous writer.");
            Console.WriteLine("DESTINATION_RACE_AND_SOURCE_LOCK=passed");

            string callbackOutput = Path.Combine(directory, "callback-error.mp4"); bool callbackFailed = false;
            try { Remuxer.Remux(source, callbackOutput, delegate(double value) { throw new InvalidOperationException("Intentional callback failure"); }, CancellationToken.None); }
            catch (InvalidOperationException) { callbackFailed = true; }
            Require(callbackFailed && !File.Exists(callbackOutput), "Callback failure created final output.");
            Require(Directory.GetFiles(directory, ".rilmo-remux-*.mp4").Length == 0, "Partial remux files remained.");
            Require(Hash(source) == originalHash, "A failure/cancellation changed the source.");
            Console.WriteLine("CALLBACK_EXCEPTION_AND_PARTIAL_CLEANUP=passed");

            foreach (ProcessModule module in Process.GetCurrentProcess().Modules) {
                string name = module.ModuleName.ToLowerInvariant();
                Require(name != "win-capture.dll" && name != "win-wasapi.dll" && !name.Contains("qt6") && !name.Contains("libcef"), "Unexpected capture/frontend module: " + name);
                Require(module.FileName.IndexOf("obs-studio", StringComparison.OrdinalIgnoreCase) < 0, "An installed OBS module was loaded: " + module.FileName);
            }
            Console.WriteLine("NO_CAPTURE_FRONTEND_MODULES=passed");
            Console.WriteLine("SOURCE=" + source); Console.WriteLine("OUTPUT=" + output); Console.WriteLine("REMUX_SMOKE=passed"); return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
'@
$harnessPath = Join-Path $runDirectory 'RemuxSmoke.cs'
[System.IO.File]::WriteAllText($harnessPath, $harnessText, [System.Text.UTF8Encoding]::new($false))
$executable = Join-Path $runDirectory 'RemuxSmoke.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ ('/out:' + $executable) (Join-Path $workspace 'src\Remuxer.cs') (Join-Path $workspace 'src\Engine\RuntimeLocator.cs') $harnessPath
if ($LASTEXITCODE -ne 0) { throw 'Remux smoke harness did not compile.' }
& (Join-Path $workspace 'tools\stage-runtime.ps1') -AppDirectory $runDirectory
$runOutput = & $executable $InputFile $runDirectory 2>&1
$harnessExitCode = $LASTEXITCODE
$runOutput | ForEach-Object { Write-Output $_ }
$runOutput | Out-File -LiteralPath (Join-Path $runDirectory 'harness.log') -Encoding utf8
if ($harnessExitCode -ne 0) { throw "Remux smoke failed ($harnessExitCode). Evidence is retained at $runDirectory" }
$source = Join-Path $runDirectory ('Original caf' + [char]0x00e9 + ' ' + [char]0x0627 + [char]0x0631 + [char]0x062f + [char]0x0648 + '.mkv')
$output = Get-ChildItem -LiteralPath $runDirectory -Filter 'Export *.mp4' -File | Select-Object -First 1 -ExpandProperty FullName
function Read-MediaEvidence([string]$Path) {
    $json = & $FfprobePath -v error -show_streams -show_packets -show_data_hash sha256 -show_entries 'stream=index,codec_name,codec_type:packet=stream_index,data_hash' -of json $Path
    if ($LASTEXITCODE -ne 0) { throw "ffprobe validation failed for $Path" }
    return ($json -join "`n" | ConvertFrom-Json)
}
$inputEvidence = Read-MediaEvidence $source
$outputEvidence = Read-MediaEvidence $output
if (@($inputEvidence.streams).Count -ne @($outputEvidence.streams).Count) { throw 'Output lost a media stream.' }
foreach ($stream in $inputEvidence.streams) {
    $matching = $outputEvidence.streams | Where-Object index -eq $stream.index | Select-Object -First 1
    if (-not $matching -or $matching.codec_name -ne $stream.codec_name -or $matching.codec_type -ne $stream.codec_type) { throw 'Output codec or stream identity changed.' }
    $before = @($inputEvidence.packets | Where-Object stream_index -eq $stream.index | ForEach-Object data_hash)
    $after = @($outputEvidence.packets | Where-Object stream_index -eq $stream.index | ForEach-Object data_hash)
    if ($before.Count -eq 0 -or $before.Count -ne $after.Count -or ($before -join ',') -cne ($after -join ',')) { throw "Compressed packet payload mismatch in stream $($stream.index)." }
    Write-Output ("LOSSLESS_PACKET_PAYLOADS stream={0} codec={1} packets={2}: passed" -f $stream.index, $stream.codec_name, $before.Count)
}
@{ input = $source; output = $output; inputSha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash; outputSha256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash; streams = $outputEvidence.streams; packetPayloadsIdentical = $true } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $runDirectory 'validation.json') -Encoding utf8
Write-Output "Remux validation passed. Evidence: $runDirectory"
