param(
    [int]$Seconds = 3,
    [string]$Encoder = 'auto',
    [switch]$Microphone,
    [switch]$NoAudio,
    [switch]$SkipPause,
    [int]$Runs = 2,
    [ValidateSet('Display', 'Window', 'Region', 'WindowClose', 'WindowCloseStats')][string]$Mode = 'Display'
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$project = Join-Path $workspace 'artifacts\engine-smoke'
$output = Join-Path $project 'out'
$recordings = Join-Path $workspace 'artifacts\test-recordings'
New-Item -ItemType Directory -Force -Path $output, $recordings | Out-Null
$dotnet = Join-Path $workspace '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet)) { throw 'Install the pinned SDK with tools/install-dotnet.ps1 first.' }
$source = @'
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Rilmo.Engine;
internal static class EngineSmoke
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [STAThread] public static int Main(string[] args)
    {
        SetProcessDPIAware();
        if (args.Length > 0 && args[0] == "--window-target")
        {
            Form target = new Form(); target.Text = "Rilmo engine capture validation #1: test";
            target.FormBorderStyle = FormBorderStyle.None; target.StartPosition = FormStartPosition.Manual;
            target.Location = new Point(100, 100); target.ClientSize = new Size(800, 450); target.TopMost = true;
            int phase = 0;
            var animation = new System.Windows.Forms.Timer { Interval = 50 };
            animation.Tick += delegate { phase = (phase + 8) % 680; target.Invalidate(); };
            target.Shown += delegate { animation.Start(); };
            target.FormClosed += delegate { animation.Dispose(); };
            target.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.Clear(Color.FromArgb(25, 35, 60));
                e.Graphics.FillRectangle(Brushes.Coral, 0, 0, 400, 450);
                e.Graphics.FillRectangle(Brushes.CornflowerBlue, 400, 0, 400, 450);
                using (Font font = new Font("Segoe UI", 24)) e.Graphics.DrawString("RILMO CAPTURE TEST", font, Brushes.White, 180, 200);
                e.Graphics.FillRectangle(Brushes.Gold, phase, 350, 100, 60);
            };
            Application.Run(target); return 0;
        }
        int seconds = Int32.Parse(args[1]), runs = Int32.Parse(args[5]);
        Process targetProcess = null;
        try
        {
            if (args[7] != "Display")
            {
                targetProcess = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--window-target") { UseShellExecute = false });
                Stopwatch targetWait = Stopwatch.StartNew();
                while (targetWait.ElapsedMilliseconds < 5000) { Thread.Sleep(100); targetProcess.Refresh(); if (targetProcess.MainWindowHandle != IntPtr.Zero) break; }
                if (targetProcess.MainWindowHandle == IntPtr.Zero) throw new Exception("Test window did not open.");
            }
            using (RecorderEngine engine = new RecorderEngine())
            {
                for (int run = 1; run <= runs; run++)
                {
                    RecorderOptions options = new RecorderOptions();
                    options.OutputDirectory = args[0]; options.Fps = 30; options.Width = 1280; options.Height = 720;
                    options.EncoderPreference = args[2]; options.Microphone = args[3] == "true"; options.SystemAudio = args[4] == "true";
                    if (args[7].StartsWith("Window")) options.WindowHandle = targetProcess.MainWindowHandle;
                    if (args[7] == "Region") { options.CropX = 260; options.CropY = 190; options.CropWidth = 320; options.CropHeight = 180; }
                    Console.WriteLine("START run=" + run);
                    Stopwatch startup = Stopwatch.StartNew(); engine.Start(options); startup.Stop();
                    Console.WriteLine("ENCODER=" + engine.EncoderName + "; ID=" + engine.EncoderId + "; STARTUP_MS=" + startup.ElapsedMilliseconds);
                    Console.WriteLine("LOG=" + engine.LogPath);
                    Process process = Process.GetCurrentProcess(); process.Refresh(); TimeSpan before = process.TotalProcessorTime;
                    Stopwatch elapsed = Stopwatch.StartNew();
                    for (int i = 0; i < seconds; i++)
                    {
                        Thread.Sleep(1000);
                        EngineStats stats = engine.GetStats(); process.Refresh();
                        Console.WriteLine("STATS frames=" + stats.TotalFrames + " dropped=" + stats.DroppedFrames + " encoder_skipped=" + stats.EncoderSkippedFrames + " fps=" + stats.ActiveFps.ToString("F2") + " render_ms=" + stats.RenderMilliseconds.ToString("F3") + " bytes=" + stats.OutputBytes + " working_mb=" + (process.WorkingSet64 / 1048576));
                    }
                    if (args[7].StartsWith("WindowClose"))
                    {
                        targetProcess.CloseMainWindow(); targetProcess.WaitForExit(5000);
                        if (args[7] == "WindowCloseStats")
                        {
                            bool expected = false;
                            try { engine.GetStats(); } catch (InvalidOperationException) { expected = true; }
                            if (!expected) throw new Exception("Closed window was not detected by GetStats.");
                        }
                        else engine.Stop(); // Regression: Stop -> GetStats -> Stop must preserve the final counters.
                        int preservedFrames = engine.GetStats().TotalFrames;
                        if (engine.IsRecording || preservedFrames < seconds * 10) throw new Exception("Closed-window finalization lost recording state or counters.");
                        engine.Stop();
                        if (engine.GetStats().TotalFrames != preservedFrames) throw new Exception("Repeated Stop changed finalized counters.");
                        Console.WriteLine("WINDOW_CLOSE=passed; FINAL_FRAMES=" + preservedFrames);
                        Console.WriteLine("OUTPUT=" + engine.OutputPath);
                        break;
                    }
                    if (args[6] == "true")
                    {
                        engine.SetPaused(true); if (!engine.IsPaused) throw new Exception("Pause did not take effect.");
                        Thread.Sleep(700); engine.SetPaused(false); if (engine.IsPaused) throw new Exception("Resume did not take effect.");
                        Thread.Sleep(1000); Console.WriteLine("PAUSE_RESUME=passed");
                    }
                    process.Refresh();
                    double cpu = (process.TotalProcessorTime - before).TotalMilliseconds / elapsed.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
                    engine.Stop(); EngineStats final = engine.GetStats();
                    Console.WriteLine("OUTPUT=" + engine.OutputPath);
                    Console.WriteLine("FINAL frames=" + final.TotalFrames + " bytes=" + new FileInfo(engine.OutputPath).Length + " process_cpu_percent=" + cpu.ToString("F2"));
                    if (final.TotalFrames < seconds * 10) throw new Exception("Too few frames were recorded.");
                    foreach (ProcessModule module in process.Modules)
                    {
                        string name = module.ModuleName.ToLowerInvariant();
                        if (name.Contains("qt6") || name.Contains("libcef") || name.Contains("obs-browser")) throw new Exception("Unexpected frontend module: " + name);
                    }
                    Console.WriteLine("NO_QT_CEF=passed");
                }
            }
            Thread.Sleep(500);
            Console.WriteLine("SMOKE=passed"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (targetProcess != null) { if (!targetProcess.HasExited) { targetProcess.CloseMainWindow(); targetProcess.WaitForExit(5000); } targetProcess.Dispose(); } }
    }
}
'@
$harness = Join-Path $project 'EngineSmoke.cs'
Set-Content -LiteralPath $harness -Value $source -Encoding UTF8
$projectSource = @'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><PlatformTarget>x64</PlatformTarget><RuntimeIdentifier>win-x64</RuntimeIdentifier><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>disable</ImplicitUsings><AssemblyName>EngineSmoke</AssemblyName></PropertyGroup><ItemGroup><Compile Include="..\..\src\Engine\*.cs" Exclude="..\..\src\Engine\MuxRelay.cs"/><Compile Include="EngineSmoke.cs"/></ItemGroup></Project>
'@
Set-Content -LiteralPath (Join-Path $project 'EngineSmoke.csproj') -Value $projectSource -Encoding UTF8
& $dotnet build (Join-Path $project 'EngineSmoke.csproj') -c Release -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'Engine compilation failed.' }
& (Join-Path $PSScriptRoot 'stage-runtime.ps1') -AppDirectory $output
$env:DOTNET_ROOT = Split-Path -Parent $dotnet
$exe = Join-Path $output 'EngineSmoke.exe'
& $exe $recordings $Seconds $Encoder $Microphone.IsPresent.ToString().ToLowerInvariant() (!$NoAudio).ToString().ToLowerInvariant() $Runs (!$SkipPause).ToString().ToLowerInvariant() $Mode
if ($LASTEXITCODE -ne 0) { throw "Engine smoke test failed ($LASTEXITCODE)." }
