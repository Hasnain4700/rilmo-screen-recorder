#if RILMO_TEST
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rilmo.Engine;

namespace Rilmo
{
    public partial class MainWindow
    {
        private async Task<string> ValidateSceneUi(string directory)
        {
            var original=SceneLibrary.Clone(sceneLibrary);
            var slate=SceneLibrary.Clone(sceneLibrary);slate.SelectedSceneId=slate.Scenes[1].Id;
            await ApplySceneCollection(slate,SceneTransition.Cut,300);
            if(captureMode!="scene"||HasActiveSession||recorder.IsSessionActive)throw new Exception("Applying idle scene unexpectedly started capture.");
            string libraryFile=Path.Combine(directory,"scene-roundtrip.json");SceneLibrary.Save(sceneLibrary,libraryFile);var restored=SceneLibrary.Read(libraryFile);
            if(restored.CanvasWidth!=1920||restored.Scenes.Count!=2||restored.SelectedSceneId!=slate.SelectedSceneId)throw new Exception("Scene persistence lost document state.");
            var malformed=SceneLibrary.Clone(restored);malformed.CanvasWidth=0;
            bool rejected=false;try{RecorderEngine.ValidateSceneCollectionDefinition(malformed,false);}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Malformed scene canvas accepted.");
            var frame=await CaptureScenePreview();
            if(recorder.IsSessionActive||HasActiveSession||busy)throw new Exception("One-shot scene preview left capture active.");
            if(frame.Width!=640||frame.Height!=360||!frame.BgraPixels.Where((b,i)=>i%4==2).Any(v=>v>150))throw new Exception("Native scene preview has no expected red or white content.");
            var bitmap=BitmapSource.Create(frame.Width,frame.Height,96,96,PixelFormats.Bgra32,null,frame.BgraPixels,frame.Stride);bitmap.Freeze();var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(directory,"scene-native-preview.png")))encoder.Save(file);
            await StartRecording();if(!recording||recorder.SelectedSceneId!=slate.SelectedSceneId)throw new Exception("Scene recording did not start on selected slate.");await Task.Delay(1100);
            var changed=SceneLibrary.Clone(sceneLibrary);changed.Scenes[1].Sources.First(s=>s.Kind==SceneSourceKind.Text).Text="Recording in progress";
            await ApplySceneCollection(changed,SceneTransition.Cut,300);if(!recording||!recorder.IsRecording)throw new Exception("Live layer edit interrupted recording.");
            changed=SceneLibrary.Clone(sceneLibrary);changed.SelectedSceneId=changed.Scenes[0].Id;await ApplySceneCollection(changed,SceneTransition.Fade,300);await Task.Delay(900);
            if(recorder.SelectedSceneId!=changed.SelectedSceneId||!recording)throw new Exception("Scene picker did not switch the native output.");
            int fixedWidth=sessionWidth,fixedHeight=sessionHeight,fixedFps=sessionFps;
            var portrait=new SceneCollection{CanvasWidth=1080,CanvasHeight=1920,SelectedSceneId="portrait-regression",Scenes=new List<SceneDefinition>{
                new SceneDefinition{Id="portrait-regression",Name="Portrait import",Sources=new List<SceneSource>{
                    new SceneSource{Id="portrait-background",Name="Background",Kind=SceneSourceKind.Color,Color=0xFF2060D0,Transform=new SourceTransform{Width=1080,Height=1920,Visible=true}},
                    new SceneSource{Id="portrait-marker",Name="Right edge marker",Kind=SceneSourceKind.Color,Color=0xFFFFFFFF,Transform=new SourceTransform{X=810,Width=270,Height=1920,Visible=true}}
                }}
            }};
            string portraitFile=Path.Combine(directory,"portrait-import.json");SceneLibrary.Save(portrait,portraitFile);
            await ApplySceneCollection(SceneLibrary.Read(portraitFile),SceneTransition.Cut,300);
            portrait=SceneLibrary.Clone(sceneLibrary);portrait.Scenes[0].Sources[0].Color=0xFF30C060;
            await ApplySceneCollection(portrait,SceneTransition.Cut,300);
            var applied=await worker.Run(()=>recorder.GetSceneCollection());var nativeBackground=applied.Scenes[0].Sources[0].Transform;var nativeMarker=applied.Scenes[0].Sources[1].Transform;
            if(Math.Abs(nativeBackground.Width-fixedWidth)>0.1||Math.Abs(nativeBackground.Height-fixedHeight)>0.1||Math.Abs(nativeMarker.X-fixedWidth*0.75f)>0.1)throw new Exception("Editing an imported portrait scene changed the fixed native canvas scaling.");
            var portraitFrame=await CaptureScenePreview();
            int left=portraitFrame.Height/2*portraitFrame.Stride+portraitFrame.Width/4*4,right=portraitFrame.Height/2*portraitFrame.Stride+portraitFrame.Width*7/8*4;
            if(portraitFrame.BgraPixels[left+1]<160||portraitFrame.BgraPixels[left+2]>90||portraitFrame.BgraPixels[right]<220||portraitFrame.BgraPixels[right+1]<220||portraitFrame.BgraPixels[right+2]<220)throw new Exception("Live portrait import did not retain the full-width background and right-edge marker.");
            await ToggleReplay();if(!replaying||!recording)throw new Exception("Replay did not join the scene recording.");
            await StopRecording();string sceneOutput=lastOutput;
            if(!replaying||!recorder.IsSessionActive||sessionWidth!=fixedWidth||sessionHeight!=fixedHeight)throw new Exception("Stopping one output discarded shared session dimensions.");
            await StartRecording();
            if(!recording||!replaying||!RecordingDetail.Text.Contains(fixedWidth+" × "+fixedHeight)||BuildOptions().Fps!=fixedFps)throw new Exception("A second output used edited document dimensions instead of the active session dimensions.");
            if((await worker.Run(()=>recorder.GetStats())).TotalFrames<2)throw new Exception("Recording was reported started before encoded video reached its output.");
            await Task.Delay(1000);await StopRecording();string portraitOutput=lastOutput;await ToggleReplay();
            if(HasActiveSession||recorder.IsSessionActive||sessionWidth!=0||sessionHeight!=0)throw new Exception("Scene outputs did not release their graph and retained dimensions.");
            var nextOptions=BuildOptions();var nextPreview=await CaptureScenePreview();
            if(nextOptions.Width>=nextOptions.Height||nextPreview.Width>=nextPreview.Height||recorder.IsSessionActive||sessionWidth!=0)throw new Exception("A new portrait session inherited the previous landscape output dimensions.");
            File.WriteAllText(Path.Combine(directory,"scene-regressions.json"),JsonSerializer.Serialize(new{livePortraitImport=true,repeatedLiveEditPixels=true,sharedOutputDimensions=true,dimensionsResetAfterStop=true,fixedWidth,fixedHeight,fixedFps,sceneOutput,portraitOutput,newSessionWidth=nextOptions.Width,newSessionHeight=nextOptions.Height},new JsonSerializerOptions{WriteIndented=true}));
            await ApplySceneCollection(slate,SceneTransition.Cut,300);
            EditScenes_Click(this,new RoutedEventArgs());await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);CaptureWindowImage(sceneEditor,Path.Combine(directory,"scene-editor.png"));sceneEditor.Width=820;sceneEditor.Height=600;sceneEditor.UpdateLayout();CaptureWindowImage(sceneEditor,Path.Combine(directory,"scene-editor-compact.png"));sceneEditor.ValidateSelectionExports(directory);sceneEditor.Close();
            var browserEditor=new SourceEditorWindow(new SceneSource{Id="browser-editor-test",Name="Stream alerts",Kind=SceneSourceKind.Browser,BrowserUrl="https://example.com",Transform=new SourceTransform{Width=1280,Height=720,Visible=true}},1920,1080){Owner=this};
            try{browserEditor.Show();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);CaptureWindowImage(browserEditor,Path.Combine(directory,"source-editor-browser.png"));}finally{browserEditor.Close();}
            Motion.Enabled=false;Navigate("studio");if(StudioPage.Opacity!=1)throw new Exception("Disabled motion did not leave the page fully visible.");Motion.Enabled=true;Motion.Reveal(StudioPage,true);if(StudioPage.HasAnimatedProperties)throw new Exception("Capture-active page animation was not suppressed.");Motion.Enabled=false;
            sceneLibrary=original;captureMode="display";RefreshScenePickers();UpdateCaptureMode();UpdateOptions();
            return sceneOutput;
        }
    }
    public partial class SceneEditorWindow
    {
        internal void ValidateSelectionExports(string directory)
        {
            try
            {
                int initialCount=collection.Scenes.Count;
                CopyScene_Click(this,new RoutedEventArgs());string copiedId=SelectedScene.Id;
                string copyFile=Path.Combine(directory,"editor-copy-export.json");SceneLibrary.Save(collection,copyFile);var copied=SceneLibrary.Read(copyFile);
                if(copied.SelectedSceneId!=copiedId||copied.Scenes.Count!=initialCount+1)throw new Exception("Copy then export lost the editor's selected scene.");
                RemoveScene_Click(this,new RoutedEventArgs());string remainingId=SelectedScene.Id;
                string removedFile=Path.Combine(directory,"editor-remove-export.json");SceneLibrary.Save(collection,removedFile);var removed=SceneLibrary.Read(removedFile);
                if(removed.SelectedSceneId!=remainingId||removed.Scenes.Count!=initialCount||removed.Scenes.Any(s=>s.Id==copiedId))throw new Exception("Remove then export retained a deleted scene selection.");
            }
            finally {changed=false;}
        }
    }
}
#endif
