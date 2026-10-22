param([switch]$SkipBuild,[switch]$SkipCrash,[switch]$ClientCrashOnly,[switch]$HostCrashOnly)
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
$project=Join-Path $workspace 'artifacts\isolated-engine'
$output=Join-Path $project 'out'
$records=Join-Path $project ('results-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$dotnet=Join-Path $workspace '.tools\dotnet\dotnet.exe'
New-Item -ItemType Directory -Force -Path $project,$output,$records | Out-Null
if(!$SkipBuild){
$projectSource=@'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><PlatformTarget>x64</PlatformTarget><RuntimeIdentifier>win-x64</RuntimeIdentifier><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>disable</ImplicitUsings><AssemblyName>IsolatedEngineSmoke</AssemblyName></PropertyGroup><ItemGroup><Compile Include="..\..\src\Engine\*.cs" Exclude="..\..\src\Engine\MuxRelay.cs"/><Compile Include="IsolatedEngineSmoke.cs"/></ItemGroup></Project>
'@
Set-Content -LiteralPath (Join-Path $project 'IsolatedEngineSmoke.csproj') -Value $projectSource -Encoding UTF8
$source=@'
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using Rilmo.Engine;
using Forms=System.Windows.Forms;
class IsolatedEngineSmoke
{
    static SourceTransform Full()=>new SourceTransform {Width=640,Height=360};
    static SceneCollection ColorOnly()
    {
        var value=new SceneCollection {CanvasWidth=640,CanvasHeight=360,SelectedSceneId="red"};
        var scene=new SceneDefinition {Id="red",Name="Red"};scene.Sources.Add(new SceneSource {Id="red",Name="Red",Kind=SceneSourceKind.Color,Color=0xFFFF0000,Transform=Full()});value.Scenes.Add(scene);return value;
    }
    static SceneCollection Browser(string html,IntPtr target)
    {
        var value=ColorOnly();var scene=new SceneDefinition {Id="browser",Name="Local browser"};
        scene.Sources.Add(new SceneSource {Id="page",Name="Local test HTML",Kind=SceneSourceKind.Browser,FilePath=html,BrowserWidth=640,BrowserHeight=360,BrowserFps=15,Transform=Full()});
        if(target!=IntPtr.Zero)
        {
            scene.Sources.Add(new SceneSource {Id="owned-window",Name="Owned window overlay",Kind=SceneSourceKind.Window,WindowHandle=target,Transform=new SourceTransform {Width=80,Height=45}});
            value.Scenes[0].Sources.Add(new SceneSource {Id="same-window",Name="Same owned window",Kind=SceneSourceKind.Window,WindowHandle=target,Transform=new SourceTransform {Width=80,Height=45}});
        }
        value.Scenes.Add(scene);value.SelectedSceneId="browser";return value;
    }
    static RecorderOptions Options(string directory,SceneCollection scenes)=>new RecorderOptions {Width=640,Height=360,Fps=30,SystemAudio=false,SceneCollection=scenes,OutputDirectory=directory};
    static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    static void WaitColor(RecorderClient client,bool green)
    {
        var wait=Stopwatch.StartNew();byte[] pixel=null;
        do
        {
            var frame=client.CapturePreview(320,180);int at=90*frame.Stride+160*4;pixel=new[]{frame.BgraPixels[at+2],frame.BgraPixels[at+1],frame.BgraPixels[at]};
            if(green ? pixel[1]>180&&pixel[0]<60&&pixel[2]<60 : pixel[0]>220&&pixel[1]<40&&pixel[2]<40){Console.WriteLine("PIXEL="+String.Join(",",pixel));return;}
            Thread.Sleep(200);
        }while(wait.ElapsedMilliseconds<10000);
        throw new Exception("Program pixels were not ready: "+String.Join(",",pixel));
    }
    static void AssertNoBrowserInParent()
    {
        foreach(ProcessModule module in Process.GetCurrentProcess().Modules)
        {string name=module.ModuleName.ToLowerInvariant();Assert(!name.Contains("libcef")&&!name.Contains("qt6")&&!name.Contains("obs-browser"),"Browser module leaked into parent: "+name);}
    }
    static int OwnedBrowserHelpers()
    {
        int count=0;foreach(var process in Process.GetProcessesByName("obs-browser-page"))using(process)
            try{if(process.MainModule.FileName.StartsWith(AppContext.BaseDirectory,StringComparison.OrdinalIgnoreCase))count++;}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
        return count;
    }
    static void WaitNoHelpers()
    {var wait=Stopwatch.StartNew();while(OwnedBrowserHelpers()>0 && wait.ElapsedMilliseconds<10000)Thread.Sleep(100);Assert(OwnedBrowserHelpers()==0,"Owned browser helpers remained after session stop.");}
    static void TestClientCrash(string directory,string html)
    {
        var start=new ProcessStartInfo(Environment.ProcessPath){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        start.ArgumentList.Add("--client-crash");start.ArgumentList.Add(directory);start.ArgumentList.Add(html);
        using(var parent=Process.Start(start))
        try
        {
            string marker=Path.Combine(directory,"client-crash-ready.txt");var wait=Stopwatch.StartNew();
            while(!File.Exists(marker) && wait.ElapsedMilliseconds<25000 && !parent.HasExited)Thread.Sleep(100);
            Assert(File.Exists(marker),"Owned client-crash fixture was not ready.");string[] details=File.ReadAllLines(marker);int hostId=int.Parse(details[0]);string path=details[1];
            using(var host=Process.GetProcessById(hostId))
            {parent.Kill();Assert(parent.WaitForExit(5000),"Owned client fixture did not exit.");Assert(host.WaitForExit(25000),"Capture host did not finish after its client died.");}
            WaitNoHelpers();Assert(File.Exists(path) && new FileInfo(path).Length>1024,"Client crash did not gracefully finalize native output.");
            Console.WriteLine("CLIENT_CRASH_RECORDING="+path);Console.WriteLine("CLIENT_CRASH_CLEANUP=passed");
        }
        finally{if(!parent.HasExited){parent.Kill();parent.WaitForExit(5000);}}
    }
    static void TestHostCrash(RecorderClient client,string directory,string html)
    {
        client.StartSession(Options(directory,Browser(html,IntPtr.Zero)));WaitColor(client,true);client.StartRecording();Thread.Sleep(800);string path=client.OutputPath;int host=client.IsolatedProcessId.Value;
        using(var process=Process.GetProcessById(host)){process.Kill();Assert(process.WaitForExit(5000),"Owned crash host did not terminate.");}
        bool failed=false;try{client.GetStats();}catch(IOException){failed=true;}Assert(failed&&!client.IsSessionActive&&!client.IsRecording,"Host crash did not update cached lifecycle state.");Assert(client.OutputPath==path,"Crash recovery path was lost.");
        bool unconfirmed=false;try{client.StopRecording();}catch(IOException){unconfirmed=true;}Assert(unconfirmed,"Host-crashed recording was falsely reported finalized.");Assert(client.OutputPath==path,"Failed finalize lost retained path.");client.Stop();Assert(client.OutputPath==path,"Cleanup discarded retained path.");
        Console.WriteLine("CRASH_RECOVERY_PATH="+path);WaitNoHelpers();client.StartSession(Options(directory,Browser(html,IntPtr.Zero)));WaitColor(client,true);client.Stop();WaitNoHelpers();Console.WriteLine("HOST_CRASH_RECOVERY=passed");
    }
    [STAThread]static int Main(string[] args)
    {
        if(RecorderHost.IsHostRequest(args))return RecorderHost.Run(args);
        if(args[0]=="--window-target")
        {Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);using(var form=new Forms.Form {Text="Rilmo IPC Target "+Environment.ProcessId,ClientSize=new Size(640,360),BackColor=Color.DarkOrange,StartPosition=Forms.FormStartPosition.Manual,Location=new Point(80,80)})Forms.Application.Run(form);return 0;}
        if(args[0]=="--client-crash")
        {
            using(var client=new RecorderClient())
            {client.StartSession(Options(args[1],Browser(args[2],IntPtr.Zero)));WaitColor(client,true);client.StartRecording();Thread.Sleep(1800);File.WriteAllLines(Path.Combine(args[1],"client-crash-ready.txt"),new[]{client.IsolatedProcessId.Value.ToString(),client.OutputPath});Thread.Sleep(Timeout.Infinite);}
            return 0;
        }
        try
        {
            string directory=args[0],html=Path.Combine(directory,"local-green.html");File.WriteAllText(html,"<!doctype html><html><head><meta charset='utf-8'><style>html,body{margin:0;width:100%;height:100%;background:#00e000}</style></head><body>LOCAL TEST</body></html>");
            if(args[1]=="client-crash-only"){TestClientCrash(directory,html);AssertNoBrowserInParent();return 0;}
            if(args[1]=="host-crash-only"){using(var client=new RecorderClient())TestHostCrash(client,directory,html);AssertNoBrowserInParent();return 0;}
            using(var target=Process.Start(new ProcessStartInfo(Environment.ProcessPath,"--window-target"){UseShellExecute=false}))
            try
            {
                var wait=Stopwatch.StartNew();IntPtr handle=IntPtr.Zero;while(wait.ElapsedMilliseconds<10000){target.Refresh();handle=target.MainWindowHandle;if(handle!=IntPtr.Zero)break;Thread.Sleep(50);}Assert(handle!=IntPtr.Zero,"Owned target failed to open.");Thread.Sleep(200);
                using(var client=new RecorderClient())
                {
                    client.StartSession(Options(directory,ColorOnly()));Assert(!client.IsIsolated,"Plain color canvas unnecessarily spawned a host.");WaitColor(client,false);
                    bool rejected=false;try{client.SetSceneCollection(Browser(html,IntPtr.Zero));}catch(InvalidOperationException){rejected=true;}Assert(rejected&&client.IsSessionActive,"Adding browser to direct session did not preserve capture.");client.Stop();AssertNoBrowserInParent();
                    int previousHost=0;
                    for(int run=0;run<2;run++)
                    {
                        client.StartSession(Options(directory,Browser(html,handle)));Assert(client.IsIsolated&&client.IsSessionActive,"Browser capture did not use isolated host.");
                        int host=client.IsolatedProcessId.Value;Assert(host!=previousHost,"A CEF process was reused across full session shutdown.");previousHost=host;Console.WriteLine("HOST="+host);Console.WriteLine("LOG="+client.LogPath);
                        var received=client.GetSceneCollection();Assert(received.Scenes[1].Sources[1].WindowHandle==handle,"HWND was lost in IPC.");
                        WaitColor(client,true);client.StartRecording();string recording=client.OutputPath;Thread.Sleep(700);client.SetPaused(true);Thread.Sleep(100);client.SetPaused(false);
                        client.StartReplayBuffer(new ReplayOptions {Seconds=5,MaximumMegabytes=32});Thread.Sleep(600);
                        client.SwitchScene("red",SceneTransition.Fade,300);Thread.Sleep(400);WaitColor(client,false);client.SwitchScene("browser");WaitColor(client,true);
                        var invalid=client.GetSceneCollection();invalid.Scenes[1].Sources[0].BrowserWidth=0;rejected=false;try{client.SetSceneCollection(invalid);}catch(ArgumentException){rejected=true;}Assert(rejected&&client.IsRecording&&client.IsReplayBufferActive,"RPC error corrupted output state.");
                        var levels=client.GetAudioLevels();Assert(levels.SystemPeakDb<=-90,"Disabled audio meter incorrect.");Thread.Sleep(500);
                        Console.WriteLine("REPLAY="+client.SaveReplay());client.StopReplayBuffer();client.StopRecording();int frames=client.GetStats().TotalFrames;Assert(frames>0,"Recording had no frames.");
                        client.Stop();Assert(!client.IsSessionActive&&!client.IsIsolated,"Browser host remained active after Stop.");Assert(client.GetStats().TotalFrames==frames,"Final stats were lost when host exited.");Assert(client.OutputPath==recording,"Final output path was lost after host exit.");
                        client.Stop();WaitNoHelpers();AssertNoBrowserInParent();Console.WriteLine("RECORDING="+recording);Console.WriteLine("HOST_RUN="+run+" passed");
                    }
                    if(args[1]!="skip-crash")
                    {
                        TestHostCrash(client,directory,html);
                    }
                }
            }
            finally{if(!target.HasExited){target.CloseMainWindow();if(!target.WaitForExit(2000))target.Kill();}}
            AssertNoBrowserInParent();Console.WriteLine("ISOLATED_ENGINE_SMOKE=passed");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
'@
Set-Content -LiteralPath (Join-Path $project 'IsolatedEngineSmoke.cs') -Value $source -Encoding UTF8
& $dotnet build (Join-Path $project 'IsolatedEngineSmoke.csproj') -c Release -o $output --nologo
if($LASTEXITCODE -ne 0){throw 'Isolated harness compilation failed.'}
& (Join-Path $PSScriptRoot 'stage-runtime.ps1') -AppDirectory $output
& (Join-Path $PSScriptRoot 'stage-browser-runtime.ps1') -AppDirectory $output
}
$env:DOTNET_ROOT=Split-Path -Parent $dotnet
$mode=if($ClientCrashOnly){'client-crash-only'}elseif($HostCrashOnly){'host-crash-only'}elseif($SkipCrash){'skip-crash'}else{'crash'}
& (Join-Path $output 'IsolatedEngineSmoke.exe') $records $mode
if($LASTEXITCODE -ne 0){throw "Isolated smoke failed ($LASTEXITCODE). Results: $records"}
Write-Output "RESULTS=$records"
