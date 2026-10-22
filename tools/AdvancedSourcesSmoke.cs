// SPDX-License-Identifier: GPL-2.0-or-later
// Local-only native integration test. The game hook targets this test's child.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Rilmo.Engine;

internal static class AdvancedSourcesSmoke
{
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window,int command);
    [STAThread] static int Main(string[] args)
    {
        SetProcessDPIAware();
        if(RecorderHost.IsHostRequest(args))return RecorderHost.Run(args);
        if(args.Length!=0 && args[0]=="--game-target") { Application.Run(new D3DTarget());return 0; }
        try
        {
            Directory.CreateDirectory(args[0]);
            if(args[1]=="Browser") TestBrowser(args[0]); else TestGame(args[0]);
            Console.WriteLine("ADVANCED_SMOKE=passed");return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
    static RecorderOptions Options(string directory,SceneCollection collection) => new RecorderOptions {
        Width=640,Height=360,Fps=30,SystemAudio=false,Microphone=false,OutputDirectory=directory,SceneCollection=collection
    };
    static SceneCollection Collection(SceneSource source) => new SceneCollection {
        CanvasWidth=640,CanvasHeight=360,SelectedSceneId="active",
        Scenes={new SceneDefinition {Id="active",Name="Active",Sources={source}},
            new SceneDefinition {Id="off",Name="Off",Sources={new SceneSource {Id="red",Name="Red",Kind=SceneSourceKind.Color,Color=0xffd02030,Transform=new SourceTransform{Width=640,Height=360}}}}}
    };
    static void Assert(bool value,string message) {if(!value)throw new Exception(message);}
    static void SavePreview(ScenePreviewFrame frame,string path)
    {
        using(var bitmap=new Bitmap(frame.Width,frame.Height,PixelFormat.Format32bppArgb)) {
            var data=bitmap.LockBits(new Rectangle(0,0,frame.Width,frame.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
            try {for(int y=0;y<frame.Height;y++)Marshal.Copy(frame.BgraPixels,y*frame.Stride,IntPtr.Add(data.Scan0,y*data.Stride),frame.Stride);} finally {bitmap.UnlockBits(data);}
            bitmap.Save(path,ImageFormat.Png);
        }
    }
    static bool Green(ScenePreviewFrame frame) {int p=frame.Stride*(frame.Height/2)+frame.Width/2*4;return frame.BgraPixels[p+1]>150 && frame.BgraPixels[p+2]<100 && frame.BgraPixels[p]<130;}
    static ScenePreviewFrame WaitGreen(RecorderClient engine)
    {
        var clock=Stopwatch.StartNew();ScenePreviewFrame frame=null;
        do {Thread.Sleep(150);frame=engine.CapturePreview(640,360);if(Green(frame))return frame;} while(clock.ElapsedMilliseconds<15000);
        throw new Exception("The browser did not render the expected local HTML pixels within 15 seconds.");
    }
    static int OwnBrowserProcesses()
    {
        string expected=Path.Combine(AppContext.BaseDirectory,"runtime","browser","bin","obs-browser-page.exe");int count=0;
        foreach(var process in Process.GetProcessesByName("obs-browser-page")) using(process) {
            try {if(String.Equals(process.MainModule.FileName,expected,StringComparison.OrdinalIgnoreCase))count++;}catch(Exception){}
        }
        return count;
    }
    static void TestBrowser(string directory)
    {
        Assert(RecorderEngine.BrowserRuntimeAvailable,"Optional browser component is missing.");
        Assert(!Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Any(m=>m.ModuleName.Equals("libcef.dll",StringComparison.OrdinalIgnoreCase)),"CEF was loaded before browser request.");
        using(var heartbeat=new Heartbeat())
        {
            string html=Path.Combine(directory,"browser-local.html");
            File.WriteAllText(html,"<!doctype html><html><head><meta charset='utf-8'><style>html,body{margin:0;width:100%;height:100%;background:rgb(40,210,80)}</style></head><body><script>setInterval(()=>{let i=new Image();i.src='http://127.0.0.1:"+heartbeat.Port+"/tick?'+Date.now()},200)</script></body></html>");
            for(int run=0;run<3;run++)
            {
                using(var engine=new RecorderClient())
                {
                    var source=new SceneSource {Id="html",Name="Local HTML",Kind=SceneSourceKind.Browser,FilePath=html,BrowserWidth=640,BrowserHeight=360,BrowserFps=30,Transform=new SourceTransform {Width=640,Height=360}};
                    engine.StartSession(Options(directory,Collection(source)));Console.WriteLine("BROWSER_LOG="+engine.LogPath);
                    SavePreview(WaitGreen(engine),Path.Combine(directory,"browser-preview-"+run+".png"));
                    int active=heartbeat.Count;Thread.Sleep(1000);Assert(heartbeat.Count>active,"Local HTML heartbeat did not run while active.");
                    Console.WriteLine("BROWSER_RENDERED=passed; HELPERS="+OwnBrowserProcesses());
                    PrintBrowserMemory(engine.IsolatedProcessId);
                    engine.StartRecording();Thread.Sleep(1300);engine.StopRecording();Console.WriteLine("BROWSER_RECORDING="+engine.OutputPath);
                    engine.SwitchScene("off");Thread.Sleep(700);int stopped=heartbeat.Count;Thread.Sleep(1100);
                    Assert(heartbeat.Count==stopped,"Inactive browser continued executing local HTML timers.");
                    Console.WriteLine("BROWSER_INACTIVE_SHUTDOWN=passed; HELPERS="+OwnBrowserProcesses());
                    engine.SwitchScene("active");WaitGreen(engine);int resumed=heartbeat.Count;Thread.Sleep(700);Assert(heartbeat.Count>resumed,"Browser did not resume on scene activation.");
                    engine.Stop();
                }
                var cleanup=Stopwatch.StartNew();while(OwnBrowserProcesses()!=0 && cleanup.ElapsedMilliseconds<10000)Thread.Sleep(100);
                Assert(OwnBrowserProcesses()==0,"Browser helper processes remained after session shutdown.");
                Console.WriteLine("BROWSER_HELPER_CLEANUP=passed; SESSION="+run);
                Assert(!Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Any(m=>m.ModuleName.Equals("libcef.dll",StringComparison.OrdinalIgnoreCase)),"CEF leaked into the UI/client process.");
            }
        }
    }
    static void PrintBrowserMemory(int? hostId)
    {
        long working=0,privateBytes=0;int count=0;
        string expected=Path.Combine(AppContext.BaseDirectory,"runtime","browser","bin","obs-browser-page.exe");
        foreach(var process in Process.GetProcessesByName("obs-browser-page"))using(process)try{if(String.Equals(process.MainModule.FileName,expected,StringComparison.OrdinalIgnoreCase)){working+=process.WorkingSet64;privateBytes+=process.PrivateMemorySize64;count++;}}catch(Exception){}
        if(hostId.HasValue)using(var process=Process.GetProcessById(hostId.Value)){working+=process.WorkingSet64;privateBytes+=process.PrivateMemorySize64;count++;}
        Console.WriteLine("BROWSER_PROCESS_MEMORY_SAMPLE=processes:"+count+",summedWorkingSetBytes:"+working+",privateBytes:"+privateBytes+"; not a benchmark, shared pages may be double-counted");
    }
    static void TestGame(string directory)
    {
        var start=new ProcessStartInfo(Environment.ProcessPath,"--game-target") {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        using(var target=Process.Start(start))
        {
            try
            {
                CaptureWindow window=null;var ready=Stopwatch.StartNew();
                while(window==null && ready.ElapsedMilliseconds<10000) {Thread.Sleep(100);window=RecorderEngine.GetCaptureWindows().FirstOrDefault(w=>w.Title==D3DTarget.TitleText && GetWindowProcess(w.Handle)==target.Id);}
                Assert(window!=null,"The owned D3D11 test window did not open.");
                using(var engine=new RecorderClient())
                {
                    var source=new SceneSource {Id="game",Name="Owned D3D11 test",Kind=SceneSourceKind.Game,WindowHandle=window.Handle,Transform=new SourceTransform {Width=640,Height=360}};
                    engine.StartSession(Options(directory,Collection(source)));Console.WriteLine("GAME_LOG="+engine.LogPath);
                    string hook=target.Modules.Cast<ProcessModule>().FirstOrDefault(module=>module.ModuleName.Equals("graphics-hook64.dll",StringComparison.OrdinalIgnoreCase))?.FileName;
                    string expectedHook=Path.Combine(AppContext.BaseDirectory,"runtime","obs","data","obs-plugins","win-capture","graphics-hook64.dll");
                    Assert(String.Equals(hook,expectedHook,StringComparison.OrdinalIgnoreCase),"Game capture did not use the packaged app-local hook: "+hook);
                    Console.WriteLine("GAME_APP_LOCAL_HOOK="+hook);
                    Thread.Sleep(500);var first=engine.CapturePreview(640,360);Thread.Sleep(350);var second=engine.CapturePreview(640,360);
                    int p=second.Stride*180+320*4;
                    Assert(second.BgraPixels[p+1]>100,"Game capture produced no expected animated D3D pixels.");
                    Assert(Math.Abs(first.BgraPixels[p+2]-second.BgraPixels[p+2])>4,"Game capture did not update animated frames.");
                    SavePreview(second,Path.Combine(directory,"game-preview.png"));engine.StartRecording();Thread.Sleep(1500);engine.Stop();Console.WriteLine("GAME_RECORDING="+engine.OutputPath);
                    Console.WriteLine("GAME_D3D11_CAPTURE=passed");
                }
            }
            finally {if(!target.HasExited){target.CloseMainWindow();if(!target.WaitForExit(3000))target.Kill();}}
        }
    }
    static uint GetWindowProcess(IntPtr window){GetWindowThreadProcessId(window,out uint process);return process;}
    sealed class Heartbeat:IDisposable
    {
        readonly TcpListener listener=new TcpListener(IPAddress.Loopback,0);readonly Thread thread;int count;volatile bool running=true;
        internal int Port {get;}internal int Count=>Volatile.Read(ref count);
        internal Heartbeat() {listener.Start();Port=((IPEndPoint)listener.LocalEndpoint).Port;thread=new Thread(Serve){IsBackground=true};thread.Start();}
        void Serve(){while(running)try{using(var client=listener.AcceptTcpClient()){client.ReceiveTimeout=1000;using(var stream=client.GetStream()){byte[] b=new byte[4096];int size=stream.Read(b,0,b.Length);if(Encoding.ASCII.GetString(b,0,size).StartsWith("GET /tick?",StringComparison.Ordinal))Interlocked.Increment(ref count);byte[] response=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Private-Network: true\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");stream.Write(response);}}}catch(Exception){if(!running)return;}}
        public void Dispose(){running=false;listener.Stop();thread.Join(2000);}
    }
    sealed class D3DTarget:Form
    {
        internal const string TitleText="Rilmo owned Direct3D11 capture verification";
        [StructLayout(LayoutKind.Sequential)]struct SwapDesc{internal uint Width,Height,RefreshNumerator,RefreshDenominator,Format,Scanline,Scaling,SampleCount,SampleQuality,Usage,BufferCount;internal IntPtr Window;internal int Windowed;internal uint Effect,Flags;}
        [DllImport("d3d11.dll")]static extern int D3D11CreateDeviceAndSwapChain(IntPtr adapter,int driver,IntPtr software,uint flags,IntPtr levels,uint count,uint sdk,ref SwapDesc description,out IntPtr swap,out IntPtr device,out int level,out IntPtr context);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int GetBuffer(IntPtr self,uint index,ref Guid id,out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int CreateView(IntPtr self,IntPtr resource,IntPtr description,out IntPtr view);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void Clear(IntPtr self,IntPtr view,float[] color);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int Present(IntPtr self,uint sync,uint flags);
        IntPtr swap,device,context,view;readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=16};readonly Stopwatch time=Stopwatch.StartNew();Clear clear;Present present;
        static T Method<T>(IntPtr obj,int slot)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj),slot*IntPtr.Size));
        internal D3DTarget(){Text=TitleText;ClientSize=new Size(640,360);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;Shown+=(_,_)=>{Initialize();ShowWindow(Handle,5);};timer.Tick+=(_,_)=>Draw();}
        void Initialize(){var desc=new SwapDesc{Width=640,Height=360,RefreshNumerator=60,RefreshDenominator=1,Format=28,SampleCount=1,Usage=0x20,BufferCount=2,Window=Handle,Windowed=1};Marshal.ThrowExceptionForHR(D3D11CreateDeviceAndSwapChain(IntPtr.Zero,1,IntPtr.Zero,0,IntPtr.Zero,0,7,ref desc,out swap,out device,out int level,out context));Guid textureId=new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");Marshal.ThrowExceptionForHR(Method<GetBuffer>(swap,9)(swap,0,ref textureId,out IntPtr texture));try{Marshal.ThrowExceptionForHR(Method<CreateView>(device,9)(device,texture,IntPtr.Zero,out view));}finally{Marshal.Release(texture);}clear=Method<Clear>(context,50);present=Method<Present>(swap,8);timer.Start();}
        void Draw(){clear(context,view,new[]{(float)(0.5+0.4*Math.Sin(time.Elapsed.TotalSeconds*2)),0.8f,0.2f,1f});Marshal.ThrowExceptionForHR(present(swap,1,0));}
        protected override void Dispose(bool disposing){timer.Stop();timer.Dispose();foreach(IntPtr value in new[]{view,context,device,swap})if(value!=IntPtr.Zero)Marshal.Release(value);base.Dispose(disposing);}
    }
}
