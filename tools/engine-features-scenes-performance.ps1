param([ValidateRange(30,90)][int]$Seconds=45)
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
$project=Join-Path $workspace 'artifacts\scene-performance'
$output=Join-Path $project 'out'
$results=Join-Path $project ('results-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$dotnet=Join-Path $workspace '.tools\dotnet\dotnet.exe'
New-Item -ItemType Directory -Force -Path $project,$output,$results | Out-Null
$xml=@'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><PlatformTarget>x64</PlatformTarget><RuntimeIdentifier>win-x64</RuntimeIdentifier><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>disable</ImplicitUsings><AssemblyName>ScenePerformance</AssemblyName></PropertyGroup><ItemGroup><Compile Include="..\..\src\Engine\*.cs" Exclude="..\..\src\Engine\MuxRelay.cs"/><Compile Include="ScenePerformance.cs"/></ItemGroup></Project>
'@
Set-Content -LiteralPath (Join-Path $project 'ScenePerformance.csproj') -Value $xml -Encoding utf8
$source=@'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using Rilmo.Engine;
using Forms=System.Windows.Forms;
class ScenePerformance
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Entry {public uint Size,Usage,Pid;public UIntPtr Heap;public uint Module,Threads,Parent;public int Priority;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Exe;}
    [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint id);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool Process32First(IntPtr snapshot,ref Entry entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool Process32Next(IntPtr snapshot,ref Entry entry);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    sealed class Sample {public string Phase{get;set;}public double Seconds{get;set;}public double CpuPercent{get;set;}public double WorkingSetMiB{get;set;}public double RenderMs{get;set;}public double ActiveFps{get;set;}public int Dropped{get;set;}public int Skipped{get;set;}public int Frames{get;set;}public string Scene{get;set;}}
    static readonly List<Sample> samples=new List<Sample>();
    static (double Cpu,long Ram) Totals()
    {
        var ids=new List<int>{Environment.ProcessId};IntPtr snapshot=CreateToolhelp32Snapshot(2,0);
        if(snapshot!=new IntPtr(-1))try{var item=new Entry {Size=(uint)Marshal.SizeOf<Entry>()};if(Process32First(snapshot,ref item))do{if(item.Parent==Environment.ProcessId && item.Exe.Equals("obs-ffmpeg-mux.exe",StringComparison.OrdinalIgnoreCase))ids.Add((int)item.Pid);}while(Process32Next(snapshot,ref item));}finally{CloseHandle(snapshot);}
        double cpu=0;long ram=0;foreach(int id in ids)try{using(var process=Process.GetProcessById(id)){cpu+=process.TotalProcessorTime.TotalSeconds;ram+=process.WorkingSet64;}}catch(ArgumentException){}
        return(cpu,ram);
    }
    static object Summarize(string phase)
    {
        var selected=samples.Where(s=>s.Phase==phase).Skip(phase=="Recording"?4:1).ToArray();
        return new {Samples=selected.Length,CpuMeanPercent=selected.Average(s=>s.CpuPercent),CpuMaximumPercent=selected.Max(s=>s.CpuPercent),WorkingSetMeanMiB=selected.Average(s=>s.WorkingSetMiB),WorkingSetMaximumMiB=selected.Max(s=>s.WorkingSetMiB),RenderMeanMs=selected.Average(s=>s.RenderMs),Frames=selected.Last().Frames,Dropped=selected.Last().Dropped,Skipped=selected.Last().Skipped};
    }
    static SceneCollection Collection(IntPtr handle)
    {
        var result=new SceneCollection {CanvasWidth=1280,CanvasHeight=720,SelectedSceneId="screen"};
        var screen=new SceneDefinition {Id="screen",Name="Full window"};screen.Sources.Add(new SceneSource {Id="capture",Name="Owned animation",Kind=SceneSourceKind.Window,WindowHandle=handle,Transform=new SourceTransform {Width=1280,Height=720}});
        var presenter=new SceneDefinition {Id="presenter",Name="Window and title"};presenter.Sources.Add(new SceneSource {Id="background",Name="Background",Kind=SceneSourceKind.Color,Color=0xFF141420,Transform=new SourceTransform {Width=1280,Height=720}});presenter.Sources.Add(new SceneSource {Id="same-capture",Name="Shared window",Kind=SceneSourceKind.Window,WindowHandle=handle,Transform=new SourceTransform {X=32,Y=100,Width=1000,Height=562}});presenter.Sources.Add(new SceneSource {Id="title",Name="Title",Kind=SceneSourceKind.Text,Text="RILMO · THREE SCENE CHECK",FontSize=40,Transform=new SourceTransform {X=32,Y=20,Width=1000,Height=60}});
        var slate=new SceneDefinition {Id="slate",Name="Static slate"};slate.Sources.Add(new SceneSource {Id="background",Name="Background",Kind=SceneSourceKind.Color,Color=0xFF202038,Transform=new SourceTransform {Width=1280,Height=720}});slate.Sources.Add(new SceneSource {Id="title",Name="Title",Kind=SceneSourceKind.Text,Text="CAPTURE READY",FontSize=70,Transform=new SourceTransform {X=100,Y=250,Width=1080,Height=200}});
        result.Scenes.Add(screen);result.Scenes.Add(presenter);result.Scenes.Add(slate);return result;
    }
    [STAThread]static int Main(string[] args)
    {
        if(RecorderHost.IsHostRequest(args))return RecorderHost.Run(args);
        if(args[0]=="--window-target")
        {
            Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);using(var form=new Target())Forms.Application.Run(form);return 0;
        }
        try
        {
            string directory=args[0];int seconds=int.Parse(args[1]);
            using(var client=new RecorderClient())
            {
                var time=Stopwatch.StartNew();var baseline=Totals();double stamp=time.Elapsed.TotalSeconds;
                for(int i=0;i<10;i++){Thread.Sleep(1000);var totals=Totals();double now=time.Elapsed.TotalSeconds;samples.Add(new Sample {Phase="Idle",Seconds=now,CpuPercent=Math.Max(0,100*(totals.Cpu-baseline.Cpu)/(now-stamp)/Environment.ProcessorCount),WorkingSetMiB=totals.Ram/1048576.0});baseline=totals;stamp=now;}
                foreach(ProcessModule module in Process.GetCurrentProcess().Modules)if(module.ModuleName.Equals("obs.dll",StringComparison.OrdinalIgnoreCase))throw new Exception("Idle client unexpectedly initialized libobs.");
                Console.WriteLine("IDLE_NO_OBS=passed");
                using(var target=Process.Start(new ProcessStartInfo(Environment.ProcessPath,"--window-target"){UseShellExecute=false}))
                try
                {
                    var wait=Stopwatch.StartNew();IntPtr handle=IntPtr.Zero;while(wait.ElapsedMilliseconds<10000){target.Refresh();handle=target.MainWindowHandle;if(handle!=IntPtr.Zero)break;Thread.Sleep(50);}if(handle==IntPtr.Zero)throw new Exception("Owned target failed.");Thread.Sleep(200);
                    var startup=Stopwatch.StartNew();client.StartSession(new RecorderOptions {Width=1280,Height=720,Fps=30,SystemAudio=false,SceneCollection=Collection(handle),OutputDirectory=directory});client.StartRecording();startup.Stop();Console.WriteLine("ENCODER="+client.EncoderId+"; STARTUP_MS="+startup.ElapsedMilliseconds);Console.WriteLine("LOG="+client.LogPath);
                    var recordingTime=Stopwatch.StartNew();baseline=Totals();stamp=recordingTime.Elapsed.TotalSeconds;
                    for(int i=0;i<seconds;i++)
                    {
                        if(i==seconds/3)client.SwitchScene("presenter",SceneTransition.Fade,300);if(i==2*seconds/3)client.SwitchScene("slate",SceneTransition.Fade,300);
                        Thread.Sleep(1000);var stats=client.GetStats();var totals=Totals();double now=recordingTime.Elapsed.TotalSeconds;
                        samples.Add(new Sample {Phase="Recording",Seconds=now,CpuPercent=Math.Max(0,100*(totals.Cpu-baseline.Cpu)/(now-stamp)/Environment.ProcessorCount),WorkingSetMiB=totals.Ram/1048576.0,Frames=stats.TotalFrames,Dropped=stats.DroppedFrames,Skipped=stats.EncoderSkippedFrames,RenderMs=stats.RenderMilliseconds,ActiveFps=stats.ActiveFps,Scene=client.SelectedSceneId});baseline=totals;stamp=now;
                        if(i%10==9)Console.WriteLine("PROGRESS="+(i+1)+"; FRAMES="+stats.TotalFrames+"; DROPPED="+stats.DroppedFrames+"; SKIPPED="+stats.EncoderSkippedFrames);
                    }
                    string path=client.OutputPath;client.Stop();Console.WriteLine("RECORDING="+path);
                    foreach(ProcessModule module in Process.GetCurrentProcess().Modules){string name=module.ModuleName.ToLowerInvariant();if(name.Contains("libcef")||name.Contains("obs-browser")||name.Contains("qt6"))throw new Exception("Core scenes loaded browser/frontend modules.");}
                    var summary=new {Scope="Console RecorderClient plus its native mux helper; excludes WPF front end, animated test target, DWM, drivers and unrelated processes.",RequestedRecordingSeconds=seconds,Encoder=client.EncoderId,Recording=path,StartupMs=startup.ElapsedMilliseconds,Idle=Summarize("Idle"),RecordingSamples=Summarize("Recording"),Caveat="No competing owned capture tests; existing user application and desktop workloads left untouched. Simple synthetic animation, not a gameplay or OBS comparison."};
                    var json=new JsonSerializerOptions {WriteIndented=true};File.WriteAllText(Path.Combine(directory,"summary.json"),JsonSerializer.Serialize(summary,json));File.WriteAllText(Path.Combine(directory,"samples.json"),JsonSerializer.Serialize(samples,json));Console.WriteLine(JsonSerializer.Serialize(summary,json));
                }
                finally{if(!target.HasExited){target.CloseMainWindow();if(!target.WaitForExit(2000))target.Kill();}}
            }
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    sealed class Target:Forms.Form
    {
        readonly Forms.Timer timer=new Forms.Timer {Interval=33};int phase;
        internal Target(){Text="Rilmo Scene Performance Target "+Environment.ProcessId;ClientSize=new Size(1280,720);StartPosition=Forms.FormStartPosition.Manual;Location=new Point(0,0);DoubleBuffered=true;timer.Tick+=(s,e)=>{phase=(phase+11)%1160;Invalidate();};Shown+=(s,e)=>timer.Start();FormClosed+=(s,e)=>timer.Dispose();}
        protected override void OnPaint(Forms.PaintEventArgs e){base.OnPaint(e);e.Graphics.Clear(Color.FromArgb(25,35,60));e.Graphics.FillRectangle(Brushes.Coral,0,0,640,720);e.Graphics.FillRectangle(Brushes.CornflowerBlue,640,0,640,720);using(var font=new Font("Segoe UI",32))e.Graphics.DrawString("RILMO · OWNED WINDOW",font,Brushes.White,300,290);e.Graphics.FillRectangle(Brushes.Gold,phase,550,120,80);}
    }
}
'@
Set-Content -LiteralPath (Join-Path $project 'ScenePerformance.cs') -Value $source -Encoding utf8
& $dotnet build (Join-Path $project 'ScenePerformance.csproj') -c Release -o $output --nologo
if($LASTEXITCODE-ne 0){throw 'Scene performance harness build failed.'}
& (Join-Path $PSScriptRoot 'stage-runtime.ps1') -AppDirectory $output
$env:DOTNET_ROOT=Split-Path -Parent $dotnet
& (Join-Path $output 'ScenePerformance.exe') $results $Seconds
if($LASTEXITCODE-ne 0){throw "Scene performance validation failed ($LASTEXITCODE)."}
Write-Output "RESULTS=$results"
