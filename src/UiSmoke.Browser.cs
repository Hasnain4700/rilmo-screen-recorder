#if RILMO_TEST
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Rilmo.Engine;

namespace Rilmo
{
    public partial class MainWindow
    {
        private async Task<bool> ValidateBrowserUi(string directory)
        {
            if(!RecorderEngine.BrowserRuntimeAvailable)return false;
            string page=Path.Combine(directory,"ui-browser.html");File.WriteAllText(page,"<!doctype html><html><body style='margin:0;background:#19d260;overflow:hidden'><div style='position:absolute;left:12px;top:12px;color:white;font:32px sans-serif'>Rilmo browser test</div></body></html>");
            var original=SceneLibrary.Clone(sceneLibrary);
            var browser=new SceneCollection{CanvasWidth=1280,CanvasHeight=720,SelectedSceneId="browser-ui",Scenes=new List<SceneDefinition>{new SceneDefinition{Id="browser-ui",Name="Browser UI test",Sources=new List<SceneSource>{new SceneSource{Id="page",Name="Local test page",Kind=SceneSourceKind.Browser,FilePath=page,BrowserWidth=1280,BrowserHeight=720,BrowserFps=30,Transform=new SourceTransform{Width=1280,Height=720,Visible=true}}}}}};
            await ApplySceneCollection(browser,SceneTransition.Cut,300);
            var processIds=new HashSet<int>();
            for(int iteration=0;iteration<2;iteration++)
            {
                await StartRecording();if(!recording||!recorder.IsIsolated||recorder.IsolatedProcessId==null)throw new Exception("Browser UI did not start an isolated engine.");
                int id=recorder.IsolatedProcessId.Value;if(!processIds.Add(id))throw new Exception("Browser session reused its engine process.");
                var loading=Stopwatch.StartNew();bool pageVisible=false;ScenePreviewFrame frame=null;
                while(loading.Elapsed<TimeSpan.FromSeconds(15))
                {
                    await Task.Delay(250);frame=await CaptureScenePreview();int offset=((frame.Height/2)*frame.Stride)+(frame.Width/2)*4;
                    if(frame.BgraPixels[offset+1]>=160&&frame.BgraPixels[offset+2]<=80){pageVisible=true;break;}
                }
                if(frame!=null)
                {
                    var image=System.Windows.Media.Imaging.BitmapSource.Create(frame.Width,frame.Height,96,96,System.Windows.Media.PixelFormats.Bgra32,null,frame.BgraPixels,frame.Stride);
                    var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                    using var file=File.Create(Path.Combine(directory,"browser-preview-"+iteration+".png"));png.Save(file);
                }
                if(!pageVisible)throw new Exception("Browser UI preview did not contain the local HTML page within 15 seconds.");
                if(!recording||!recorder.IsRecording)throw new Exception("Browser preview interrupted active recording.");
                await StopRecording();if(recorder.IsIsolated||HasActiveSession)throw new Exception("Browser session remained active after Stop.");
                try{using var child=Process.GetProcessById(id);if(!child.HasExited)throw new Exception("Browser engine process survived Stop.");}catch(ArgumentException){}
            }
            if(Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Any(m=>m.ModuleName.Contains("libcef",StringComparison.OrdinalIgnoreCase)))throw new Exception("Browser runtime leaked into the UI process.");
            sceneLibrary=original;captureMode="display";RefreshScenePickers();UpdateCaptureMode();UpdateOptions();return true;
        }
    }
}
#endif
