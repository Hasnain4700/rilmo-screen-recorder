param([ValidateSet('CompositionReplay','Streaming','Failure','Audio')][string]$Mode = 'CompositionReplay', [string]$Encoder = 'auto')
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$project = Join-Path $workspace 'artifacts\engine-features'
$output = Join-Path $project 'out'
$recordings = Join-Path $workspace 'artifacts\test-recordings\features'
$dotnet = Join-Path $workspace '.tools\dotnet\dotnet.exe'
$runtime = Join-Path $workspace 'vendor\obs\32.2.2'
New-Item -ItemType Directory -Force -Path $project,$output,$recordings | Out-Null
if (!(Test-Path -LiteralPath (Join-Path $runtime 'bin\64bit\obs.dll'))) { throw 'Prepare the pinned runtime first.' }
$projectSource = @'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><PlatformTarget>x64</PlatformTarget><RuntimeIdentifier>win-x64</RuntimeIdentifier><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>disable</ImplicitUsings><AssemblyName>EngineFeatures</AssemblyName></PropertyGroup><ItemGroup><Compile Include="..\..\src\Engine\*.cs" Exclude="..\..\src\Engine\MuxRelay.cs"/><Compile Include="EngineFeatures.cs"/></ItemGroup></Project>
'@
Set-Content -LiteralPath (Join-Path $project 'EngineFeatures.csproj') -Value $projectSource -Encoding UTF8
$source = @'
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using Rilmo.Engine;
internal static class EngineFeatures
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [STAThread] public static int Main(string[] args)
    {
        SetProcessDPIAware();
        try
        {
            string directory = args[0], mode = args[1];
            RecorderOptions options = new RecorderOptions { OutputDirectory = directory, Width = 1280, Height = 720, SystemAudio = true, EncoderPreference = args[2] };
            if (mode == "CompositionReplay")
            {
                string image = Path.Combine(directory,"overlay-validation.png");
                using (var bitmap = new Bitmap(160,90)) { using(var g=Graphics.FromImage(bitmap)) g.Clear(Color.Lime); bitmap.Save(image); }
                options.Overlays.Add(new SceneOverlay { Id="image", Kind=OverlayKind.Image, FilePath=image, Transform=new SourceTransform { X=40,Y=40,Width=320,Height=180 } });
                options.Overlays.Add(new SceneOverlay { Id="title",Kind=OverlayKind.Text,Text="RILMO OVERLAY",FontSize=48,Transform=new SourceTransform { X=420,Y=40,Width=700,Height=120 } });
                options.SystemAudioSettings = new AudioMixSettings { Volume=0.7f,NoiseSuppression=true,NoiseGate=true,Compressor=true,Limiter=true };
            }
            using (var engine = new RecorderEngine())
            {
                int filesBefore = Directory.GetFiles(directory,"*.mkv").Length;
                engine.StartSession(options);
                if (!engine.IsSessionActive || engine.IsRecording || engine.OutputPath != null) throw new Exception("Session-only lifecycle incorrect.");
                if (Directory.GetFiles(directory,"*.mkv").Length != filesBefore) throw new Exception("Session unexpectedly created a recording.");
                Console.WriteLine("SESSION_WITHOUT_RECORDING=passed; LOG="+engine.LogPath);
                if (mode == "CompositionReplay")
                {
                    Console.WriteLine("CAMERA_COUNT="+engine.GetCameraDevices().Length);
                    bool invalidCamera=false;
                    try { engine.AddOverlay(new SceneOverlay { Id="invalid",Kind=OverlayKind.Camera,DeviceId="missing:missing" }); } catch(InvalidOperationException) { invalidCamera=true; }
                    if (!invalidCamera) throw new Exception("Unavailable camera accepted.");
                    engine.StartReplayBuffer(new ReplayOptions { Seconds=5,MaximumMegabytes=32 });
                    Thread.Sleep(3000);
                    string first=engine.SaveReplay(); Console.WriteLine("REPLAY_ONLY="+first);
                    engine.StartRecording(); string recording = engine.OutputPath;
                    bool rejected=false; try { engine.SetPaused(true); } catch(InvalidOperationException) { rejected=true; }
                    if(!rejected) throw new Exception("Shared encoder pause was accepted.");
                    Thread.Sleep(1200);
                    engine.UpdateOverlay("image",new SourceTransform { X=700,Y=400,Width=320,Height=180,Visible=false });
                    engine.SetAudioControls(AudioChannel.System,new AudioMixSettings { Volume=0.5f,Muted=true,Limiter=true });
                    Thread.Sleep(400);
                    engine.UpdateOverlay("image",new SourceTransform { X=700,Y=400,Width=320,Height=180,Visible=true });
                    engine.SetAudioControls(AudioChannel.System,new AudioMixSettings { Volume=1,Compressor=true });
                    Thread.Sleep(1000);
                    Console.WriteLine("METER_DB="+engine.GetAudioLevels().SystemPeakDb);
                    Console.WriteLine("FRAMES="+engine.GetStats().TotalFrames);
                    engine.StopRecording();
                    if(engine.IsRecording || !engine.IsReplayBufferActive || !engine.IsSessionActive) throw new Exception("Independent recording stop failed.");
                    Console.WriteLine("RECORDING_WITH_REPLAY="+recording);
                    Thread.Sleep(700); Console.WriteLine("REPLAY_AFTER_RECORDING="+engine.SaveReplay());
                    engine.StopReplayBuffer(); engine.RemoveOverlay("image");
                    engine.StartRecording(); Thread.Sleep(700); engine.SetPaused(true); Thread.Sleep(200); engine.SetPaused(false); Thread.Sleep(500);
                    engine.Stop(); Console.WriteLine("RECORDING_AFTER_REPLAY="+engine.OutputPath);
                }
                else if(mode == "Streaming")
                {
                    string capture=Path.Combine(directory,"local-rtmp-"+Guid.NewGuid().ToString("N")+".mkv");
                    string ffmpeg=args[3];
                    var start=new ProcessStartInfo(ffmpeg,"-hide_banner -loglevel error -listen 1 -i rtmp://127.0.0.1:19359/live/rilmo-local-secret -c copy -y \""+capture+"\"") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true };
                    using(var sink=Process.Start(start))
                    {
                        sink.BeginOutputReadLine(); sink.ErrorDataReceived += delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null) Console.WriteLine("SINK="+e.Data);}; sink.BeginErrorReadLine();
                        try
                        {
                            Thread.Sleep(600);
                            engine.StartStreaming(new StreamOptions { ServerUrl="rtmp://127.0.0.1:19359/live",StreamKey="rilmo-local-secret",VideoBitrateKbps=2500 });
                            if(!engine.IsStreaming || engine.IsRecording) throw new Exception("Stream-only lifecycle incorrect.");
                            Thread.Sleep(2000); engine.StartReplayBuffer(new ReplayOptions { Seconds=5,MaximumMegabytes=32 }); engine.StartRecording();
                            Thread.Sleep(2500);
                            Console.WriteLine("STREAM_BYTES="+engine.GetOutputStatus().StreamBytes);
                            Console.WriteLine("STREAM_REPLAY="+engine.SaveReplay());
                            engine.StopRecording(); Console.WriteLine("STREAM_RECORDING="+engine.OutputPath);
                            if(!engine.IsStreaming) throw new Exception("Recording stop ended stream.");
                            engine.Stop();
                            if(!sink.WaitForExit(10000)) throw new Exception("Local sink did not finish.");
                            Console.WriteLine("LOCAL_RTMP="+capture);
                            if(File.ReadAllText(engine.LogPath).Contains("rilmo-local-secret")) throw new Exception("Stream secret leaked into log.");
                        }
                        finally { if(!sink.HasExited) sink.Kill(); }
                    }
                }
                else if(mode == "Audio")
                {
                    string tone=Path.Combine(directory,"quiet-meter-tone.wav");
                    using(var writer=new BinaryWriter(File.Create(tone)))
                    {
                        int samples=48000*2; writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36+samples*2); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(48000); writer.Write(96000); writer.Write((short)2); writer.Write((short)16); writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples*2);
                        for(int i=0;i<samples;i++) writer.Write((short)(Math.Sin(i*2*Math.PI*440/48000)*500));
                    }
                    using(var player=new System.Media.SoundPlayer(tone))
                    {
                        player.Load(); player.PlayLooping();
                        try
                        {
                            engine.StartRecording(); Thread.Sleep(1200); float signal=engine.GetAudioLevels().SystemPeakDb;
                            if(signal<=-90) throw new Exception("Native meter detected no known loopback signal.");
                            Console.WriteLine("AUDIO_SIGNAL_DB="+signal);
                            engine.SetAudioControls(AudioChannel.System,new AudioMixSettings { Volume=0.1f }); Thread.Sleep(700);
                            float quiet=engine.GetAudioLevels().SystemPeakDb; Console.WriteLine("AUDIO_ATTENUATED_DB="+quiet);
                            if(quiet>signal-15) throw new Exception("Volume did not attenuate native meter.");
                            engine.SetAudioControls(AudioChannel.System,new AudioMixSettings { Muted=true }); Thread.Sleep(700);
                            float muted=engine.GetAudioLevels().SystemPeakDb; Console.WriteLine("AUDIO_MUTED_DB="+muted);
                            if(muted>-90) throw new Exception("Muted channel meter did not report silence.");
                            engine.StopRecording(); Console.WriteLine("AUDIO_GAIN_MUTE_RECORDING="+engine.OutputPath);
                            engine.SetAudioControls(AudioChannel.System,new AudioMixSettings { NoiseSuppression=true,NoiseGate=true,Compressor=true,Limiter=true });
                            engine.StartRecording(); Thread.Sleep(1200); engine.Stop(); Console.WriteLine("AUDIO_RECORDING="+engine.OutputPath);
                        }
                        finally { player.Stop(); }
                    }
                }
                else
                {
                    bool failed=false;
                    try { engine.StartStreaming(new StreamOptions { ServerUrl="rtmp://127.0.0.1:19358/live",StreamKey="rilmo-failure-secret" }); } catch(InvalidOperationException) { failed=true; }
                    if(!failed || engine.IsStreaming || !engine.IsSessionActive) throw new Exception("Stream failure lifecycle incorrect.");
                    engine.StartRecording(); Thread.Sleep(1300); engine.Stop();
                    if(File.ReadAllText(engine.LogPath).Contains("rilmo-failure-secret")) throw new Exception("Failed stream leaked secret.");
                    Console.WriteLine("RECOVERY_RECORDING="+engine.OutputPath);
                }
                if(engine.IsSessionActive || engine.IsStreaming || engine.IsRecording || engine.IsReplayBufferActive) throw new Exception("Outputs remained active after Stop.");
                engine.Stop();
                foreach(ProcessModule module in Process.GetCurrentProcess().Modules) { string name=module.ModuleName.ToLowerInvariant(); if(name.Contains("qt6")||name.Contains("libcef")||name.Contains("obs-browser")) throw new Exception("Unexpected frontend module "+name); }
            }
            Console.WriteLine("FEATURE_SMOKE=passed"); return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
'@
Set-Content -LiteralPath (Join-Path $project 'EngineFeatures.cs') -Value $source -Encoding UTF8
& $dotnet build (Join-Path $project 'EngineFeatures.csproj') -c Release -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'Feature harness compilation failed.' }
& (Join-Path $PSScriptRoot 'stage-runtime.ps1') -AppDirectory $output
$env:DOTNET_ROOT = Split-Path -Parent $dotnet
$ffmpeg = 'C:\REV\tools\ai\ffmpeg-lgpl\ffmpeg-master-latest-win64-lgpl\bin\ffmpeg.exe'
& (Join-Path $output 'EngineFeatures.exe') $recordings $Mode $Encoder $ffmpeg
if ($LASTEXITCODE -ne 0) { throw "Feature smoke failed ($LASTEXITCODE)." }
