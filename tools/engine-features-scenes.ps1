param([int]$Runs=2,[ValidateSet('Scenes','WindowClose','Validation','OverlayGuard')][string]$Mode='Scenes')
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
$project=Join-Path $workspace 'artifacts\engine-scenes'
$output=Join-Path $project 'out'
$recordings=Join-Path $workspace 'artifacts\test-recordings\scenes'
$dotnet=Join-Path $workspace '.tools\dotnet\dotnet.exe'
New-Item -ItemType Directory -Force -Path $project,$output,$recordings | Out-Null
$projectSource=@'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><PlatformTarget>x64</PlatformTarget><RuntimeIdentifier>win-x64</RuntimeIdentifier><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>disable</ImplicitUsings><AssemblyName>EngineScenes</AssemblyName></PropertyGroup><ItemGroup><Compile Include="..\..\src\Engine\*.cs" Exclude="..\..\src\Engine\MuxRelay.cs"/><Compile Include="EngineScenes.cs"/></ItemGroup></Project>
'@
Set-Content -LiteralPath (Join-Path $project 'EngineScenes.csproj') -Value $projectSource -Encoding UTF8
$source=@'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Rilmo.Engine;
using Forms=System.Windows.Forms;
class EngineScenes
{
    static SourceTransform Full() {return new SourceTransform {Width=640,Height=360};}
    static SceneSource ColorSource(string id,uint color) {return new SceneSource {Id=id,Name=id,Kind=SceneSourceKind.Color,Color=color,Transform=Full()};}
    static void Assert(bool okay,string message) {if(!okay) throw new Exception(message);}
    static void ManagedValidation()
    {
        var library=new SceneCollection {SelectedSceneId="one"};var scene=new SceneDefinition {Id="one",Name="One"};library.Scenes.Add(scene);scene.Sources.Add(ColorSource("color",0xFFFF0000));
        void Bad(Action change,string name) {var saved=scene.Sources[0];var original=saved.Transform;int canvas=library.CanvasWidth;change();bool rejected=false;try{RecorderEngine.ValidateSceneCollectionDefinition(library,false);}catch(ArgumentException){rejected=true;}Assert(rejected,"Malformed collection accepted: "+name);saved.Transform=original;library.CanvasWidth=canvas;}
        Bad(()=>scene.Sources[0].Transform=new SourceTransform {X=float.NaN},"NaN");
        Bad(()=>scene.Sources[0].Transform=new SourceTransform {X=17000},"coordinate");
        Bad(()=>scene.Sources[0].Transform=new SourceTransform {Width=0},"zero width");
        Bad(()=>library.CanvasWidth=0,"zero canvas");
        scene.Sources.Add(new SceneSource {Id="offline",Name="Offline",Kind=SceneSourceKind.Window,WindowTitle="Closed window"});
        RecorderEngine.ValidateSceneCollectionDefinition(library,false);
        bool unavailable=false;try{RecorderEngine.ValidateSceneCollectionDefinition(library);}catch(ArgumentException){unavailable=true;}Assert(unavailable,"Unavailable window passed live validation.");
        scene.Sources.RemoveAt(1);scene.Sources.Add(ColorSource("color",0));
        bool duplicate=false;try{RecorderEngine.ValidateSceneCollectionDefinition(library,false);}catch(ArgumentException){duplicate=true;}Assert(duplicate,"Duplicate source ID accepted.");
        Console.WriteLine("MANAGED_VALIDATION=passed");
    }
    static void WindowClose(string directory,int runs)
    {
        for(int run=0;run<runs;run++)
        {
            using(var target=Process.Start(new ProcessStartInfo(Environment.ProcessPath,"--window-target") {UseShellExecute=false}))
            try
            {
                var wait=Stopwatch.StartNew();IntPtr handle=IntPtr.Zero;
                while(wait.ElapsedMilliseconds<10000){target.Refresh();handle=target.MainWindowHandle;if(handle!=IntPtr.Zero)break;Thread.Sleep(50);}
                Assert(handle!=IntPtr.Zero,"Owned target window did not start.");Thread.Sleep(400);
                var library=new SceneCollection {CanvasWidth=640,CanvasHeight=360,SelectedSceneId="window"};
                var window=new SceneDefinition {Id="window",Name="Owned window"};window.Sources.Add(new SceneSource {Id="capture",Name="Owned test window",Kind=SceneSourceKind.Window,WindowHandle=handle,Transform=Full()});
                var red=new SceneDefinition {Id="red",Name="Safe color"};red.Sources.Add(ColorSource("red",0xFFFF0000));library.Scenes.Add(window);library.Scenes.Add(red);
                using(var engine=new RecorderEngine())
                {
                    engine.StartSession(new RecorderOptions {Width=640,Height=360,SystemAudio=false,SceneCollection=library,OutputDirectory=directory});
                    engine.StartRecording();Thread.Sleep(1200);var pixel=Pixel(engine.CapturePreview(320,180),160,90);Console.WriteLine("WINDOW_RGB="+String.Join(",",pixel));Assert(pixel[0]>200 && pixel[1]>80 && pixel[1]<180 && pixel[2]<40,"Selected window pixels were not captured.");
                    if(run%2==1) {engine.SwitchScene("red");Check(engine,"red");}
                    target.CloseMainWindow();Assert(target.WaitForExit(5000),"Owned window did not close.");Thread.Sleep(120);
                    if(run%2==0)
                    {
                        bool stopped=false;try{engine.GetStats();}catch(InvalidOperationException ex){stopped=ex.Message.Contains("closed or minimized");}
                        Assert(stopped && !engine.IsSessionActive && !engine.IsRecording,"Closed selected scene window left an active output.");
                        int frames=engine.GetStats().TotalFrames;engine.Stop();Assert(frames>0 && engine.GetStats().TotalFrames==frames,"Final stats lost after selected window close.");
                    }
                    else
                    {
                        bool rejected=false;try{engine.SwitchScene("window");}catch(ArgumentException){rejected=true;}
                        Assert(rejected && engine.SelectedSceneId=="red" && engine.IsRecording,"Unavailable scene switch changed output.");
                        rejected=false;try{engine.SetSceneCollection(engine.GetSceneCollection());}catch(ArgumentException){rejected=true;}
                        Assert(rejected && engine.SelectedSceneId=="red" && engine.IsRecording,"Unavailable scene replacement changed output.");
                        engine.RemoveScene("window");Check(engine,"red");Thread.Sleep(500);engine.Stop();
                    }
                    Console.WriteLine("WINDOW_RECORDING="+engine.OutputPath);Console.WriteLine("WINDOW_LOG="+engine.LogPath);Console.WriteLine("WINDOW_RUN="+run+" passed");
                }
            }
            finally {if(!target.HasExited){target.CloseMainWindow();if(!target.WaitForExit(2000))target.Kill();}}
        }
    }
    static byte[] Pixel(ScenePreviewFrame frame,int x,int y) {int at=y*frame.Stride+x*4;return new byte[]{frame.BgraPixels[at+2],frame.BgraPixels[at+1],frame.BgraPixels[at]};}
    static void Check(RecorderEngine engine,string expected)
    {
        Thread.Sleep(100);var p=Pixel(engine.CapturePreview(320,180),160,90);
        Console.WriteLine("PIXEL_"+expected+"="+String.Join(",",p));
        Assert(expected=="red" ? p[0]>220 && p[1]<20 && p[2]<20 : p[2]>220 && p[0]<20 && p[1]<20,"Unexpected program color "+expected);
    }
    static void SavePreview(ScenePreviewFrame frame,string path)
    {
        using(var bitmap=new Bitmap(frame.Width,frame.Height,PixelFormat.Format32bppArgb))
        {
            var data=bitmap.LockBits(new Rectangle(0,0,frame.Width,frame.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
            try {for(int y=0;y<frame.Height;y++) Marshal.Copy(frame.BgraPixels,y*frame.Stride,IntPtr.Add(data.Scan0,y*data.Stride),frame.Stride);}
            finally {bitmap.UnlockBits(data);}bitmap.Save(path,ImageFormat.Png);
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            if(args[0]=="--window-target")
            {
                Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
                using(var form=new Forms.Form {Text="Rilmo Scene Test "+Environment.ProcessId,ClientSize=new Size(640,360),StartPosition=Forms.FormStartPosition.Manual,Location=new Point(100,100),BackColor=Color.DarkOrange})
                    Forms.Application.Run(form);
                return 0;
            }
            string directory=args[0];int runs=Int32.Parse(args[1]);
            ManagedValidation();if(args[2]=="Validation")return 0;
            if(args[2]=="WindowClose"){WindowClose(directory,runs);Console.WriteLine("WINDOW_SCENE_SMOKE=passed");return 0;}
            if(args[2]=="OverlayGuard")
            {
                var collection=new SceneCollection {CanvasWidth=640,CanvasHeight=360,SelectedSceneId="one"};var one=new SceneDefinition {Id="one",Name="One"};one.Sources.Add(ColorSource("background",0xFFFF0000));collection.Scenes.Add(one);
                using(var engine=new RecorderEngine())
                {engine.StartSession(new RecorderOptions {Width=640,Height=360,SystemAudio=false,SceneCollection=collection,OutputDirectory=directory});bool rejected=false;try{engine.AddOverlay(new SceneOverlay {Id="legacy",Kind=OverlayKind.Text,Text="Invalid owner"});}catch(InvalidOperationException ex){rejected=ex.Message.Contains("scene source operations");}Assert(rejected,"Legacy overlay was accepted into a named graph.");engine.SetSceneCollection(engine.GetSceneCollection());Check(engine,"red");engine.Stop();Assert(!engine.IsSessionActive,"Guarded graph failed to release.");}
                Console.WriteLine("OVERLAY_LIFETIME_GUARD=passed");return 0;
            }
            string image=Path.Combine(directory,"crop-pattern.png");
            using(var bitmap=new Bitmap(160,90)) {using(var g=Graphics.FromImage(bitmap)) {g.Clear(Color.Yellow);g.FillRectangle(Brushes.Cyan,80,0,80,90);}bitmap.Save(image);}
            for(int run=0;run<runs;run++)
            {
                var collection=new SceneCollection {CanvasWidth=640,CanvasHeight=360,SelectedSceneId="red"};
                var red=new SceneDefinition {Id="red",Name="Red scene"};red.Sources.Add(ColorSource("red-bg",0xFFFF0000));
                var blue=new SceneDefinition {Id="blue",Name="Blue scene"};blue.Sources.Add(ColorSource("blue-bg",0xFF0000FF));collection.Scenes.Add(red);collection.Scenes.Add(blue);
                RecorderEngine.ValidateSceneCollectionDefinition(collection);
                using(var engine=new RecorderEngine())
                {
                    engine.StartSession(new RecorderOptions {Width=640,Height=360,Fps=30,SystemAudio=false,SceneCollection=collection,OutputDirectory=directory});
                    Console.WriteLine("LOG="+engine.LogPath);var first=Pixel(engine.CapturePreview(320,180),160,90);Assert(first[0]>220 && first[1]<20,"Immediate first preview was not ready.");Check(engine,"red");
                    var returned=engine.GetSceneCollection();returned.Scenes[0].Sources[0].Color=0;
                    Assert(engine.GetSceneCollection().Scenes[0].Sources[0].Color==0xFFFF0000,"Collection alias escaped.");
                    var invalid=engine.GetSceneCollection();invalid.Scenes[1].Sources.Add(new SceneSource {Id="bad",Name="Missing",Kind=SceneSourceKind.Image,FilePath=Path.Combine(directory,"missing.png")});
                    bool rejected=false;try{engine.SetSceneCollection(invalid);}catch(FileNotFoundException){rejected=true;}
                    Assert(rejected && engine.SelectedSceneId=="red","Invalid graph did not roll back.");Check(engine,"red");
                    // Native creation failure after the first new scene also preserves program.
                    invalid=engine.GetSceneCollection();invalid.Scenes[1].Sources.Add(new SceneSource {Id="bad-camera",Name="Unavailable",Kind=SceneSourceKind.Camera,DeviceId="missing"});
                    rejected=false;try{engine.SetSceneCollection(invalid);}catch(InvalidOperationException){rejected=true;}
                    Assert(rejected && engine.SelectedSceneId=="red","Native graph failure did not roll back.");Check(engine,"red");
                    engine.StartReplayBuffer(new ReplayOptions {Seconds=5,MaximumMegabytes=32});engine.StartRecording();
                    string recording=engine.OutputPath;Thread.Sleep(700);
                    engine.SwitchScene("blue",SceneTransition.Fade,1000);Thread.Sleep(430);
                    var mid=Pixel(engine.CapturePreview(320,180),160,90);Console.WriteLine("FADE_RGB="+String.Join(",",mid));
                    Assert(mid[0]>60 && mid[2]>60 && mid[1]<25,"Fade did not produce mixed red/blue pixels.");
                    Thread.Sleep(800);Check(engine,"blue");engine.SwitchScene("red",SceneTransition.Cut);Check(engine,"red");
                    var layer=new SceneSource {Id="image",Name="Cropped image",Kind=SceneSourceKind.Image,FilePath=image,Crop=new SourceCrop {Left=80},Transform=new SourceTransform {Width=160,Height=180}};
                    engine.AddSceneSource("red",layer);Thread.Sleep(100);
                    var cropped=Pixel(engine.CapturePreview(320,180),40,45);Assert(cropped[0]<20 && cropped[1]>220 && cropped[2]>220,"Native source crop failed.");
                    engine.MoveSceneSource("red","image",0);Check(engine,"red");var back=Pixel(engine.CapturePreview(320,180),40,45);Assert(back[0]>220 && back[1]<20,"Layer reorder failed.");
                    engine.MoveSceneSource("red","image",1);engine.SetSceneSourceTransform("red","image",new SourceTransform {Width=160,Height=180,Visible=false});
                    Thread.Sleep(60);var hidden=Pixel(engine.CapturePreview(320,180),40,45);Assert(hidden[0]>220 && hidden[1]<20,"Layer visibility failed.");
                    engine.AddSceneSource("red",new SceneSource {Id="accent",Name="Tall color",Kind=SceneSourceKind.Color,Color=0xFF00FF00,Transform=new SourceTransform {X=600,Width=20,Height=300}});
                    Thread.Sleep(60);var accent=Pixel(engine.CapturePreview(320,180),305,20);Assert(accent[1]>220 && accent[0]<20,"Solid color rectangle retained an unwanted aspect ratio.");engine.RemoveSceneSource("red","accent");
                    engine.SetSceneSourceCrop("red","image",new SourceCrop {Right=80});engine.SetSceneSourceTransform("red","image",new SourceTransform {X=320,Y=0,Width=160,Height=180,Rotation=0});
                    Thread.Sleep(100);var moved=Pixel(engine.CapturePreview(320,180),200,45);Assert(moved[0]>220 && moved[1]>220 && moved[2]<20,"Crop/position edit failed.");
                    engine.AddSceneSource("red",new SceneSource {Id="title",Name="Title",Kind=SceneSourceKind.Text,Text="RILMO SCENE TEST",Transform=new SourceTransform {Y=280,Width=500,Height=60}});
                    var title=engine.GetSceneCollection().Scenes[0].Sources.Find(s=>s.Id=="title");title.Text="LIVE SOURCE UPDATE";engine.UpdateSceneSource("red",title);
                    SavePreview(engine.CapturePreview(),Path.Combine(directory,"scene-preview-"+run+".png"));
                    var extra=new SceneDefinition {Id="extra",Name="Temporary"};extra.Sources.Add(ColorSource("extra-bg",0xFF00FF00));engine.AddScene(extra);engine.RemoveScene("extra");
                    engine.RemoveSceneSource("red","title");engine.RemoveSceneSource("red","image");
                    // A second transition while the first is running exercises libobs' retarget path.
                    engine.SwitchScene("blue",SceneTransition.Fade,700);Thread.Sleep(150);engine.SwitchScene("red",SceneTransition.Fade,300);Thread.Sleep(600);Check(engine,"red");
                    Assert(engine.IsRecording && engine.IsReplayBufferActive,"Scene edit stopped an output.");Thread.Sleep(400);
                    Console.WriteLine("REPLAY="+engine.SaveReplay());engine.StopReplayBuffer();engine.Stop();
                    Assert(!engine.IsSessionActive && !engine.IsRecording,"Scene session did not stop.");
                    Console.WriteLine("RECORDING="+recording);Console.WriteLine("FRAMES="+engine.GetStats().TotalFrames);Console.WriteLine("RUN="+run+" passed");
                }
            }
            Console.WriteLine("SCENE_SMOKE=passed");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
'@
Set-Content -LiteralPath (Join-Path $project 'EngineScenes.cs') -Value $source -Encoding UTF8
& $dotnet build (Join-Path $project 'EngineScenes.csproj') -c Release -o $output --nologo
if($LASTEXITCODE -ne 0){throw 'Scene harness compilation failed.'}
& (Join-Path $PSScriptRoot 'stage-runtime.ps1') -AppDirectory $output
$env:DOTNET_ROOT=Split-Path -Parent $dotnet
& (Join-Path $output 'EngineScenes.exe') $recordings $Runs $Mode
if($LASTEXITCODE -ne 0){throw "Scene smoke failed ($LASTEXITCODE)."}
