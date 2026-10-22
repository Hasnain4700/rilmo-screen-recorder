// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rilmo.Engine;
using Forms=System.Windows.Forms;

namespace Rilmo
{
    public partial class MainWindow
    {
        private SceneCollection sceneLibrary;
        private SceneEditorWindow sceneEditor;
        private bool refreshingScenePickers;
        private string sceneLoadError;
        private void InitializeScenes()
        {
            Motion.Enabled=settings.Animations;AnimationsToggle.IsChecked=settings.Animations;
            if(!automatedTest)
                try{sceneLibrary=SceneLibrary.Load();}catch(Exception ex){sceneLoadError=ex.Message;StatusLabel.Text="Saved scenes need attention: "+ex.Message;}
            if(sceneLibrary==null)
            {
                string id=Guid.NewGuid().ToString("N");int monitor=Math.Clamp(settings.MonitorIndex,0,Math.Max(0,monitors.Length-1));
                sceneLibrary=new SceneCollection{CanvasWidth=1920,CanvasHeight=1080,SelectedSceneId=id,Scenes=new List<SceneDefinition>{
                    new SceneDefinition{Id=id,Name="My screen",Sources=new List<SceneSource>{new SceneSource{Id=Guid.NewGuid().ToString("N"),Name="Display",Kind=SceneSourceKind.Display,MonitorIndex=monitor,MonitorIdentity=monitors.Length>0?monitorIdentities[monitor]:null,Transform=new SourceTransform{Width=1920,Height=1080,Visible=true}}}},
                    new SceneDefinition{Id=Guid.NewGuid().ToString("N"),Name="Starting soon",Sources=new List<SceneSource>{
                        new SceneSource{Id=Guid.NewGuid().ToString("N"),Name="Background",Kind=SceneSourceKind.Color,Color=0xFF101014,Transform=new SourceTransform{Width=1920,Height=1080,Visible=true}},
                        new SceneSource{Id=Guid.NewGuid().ToString("N"),Name="Red accent",Kind=SceneSourceKind.Color,Color=0xFFDF3048,Transform=new SourceTransform{X=160,Y=385,Width=8,Height=280,Visible=true}},
                        new SceneSource{Id=Guid.NewGuid().ToString("N"),Name="Title",Kind=SceneSourceKind.Text,Text="Starting soon",FontSize=100,Transform=new SourceTransform{X=220,Y=415,Width=1380,Height=155,Visible=true}},
                        new SceneSource{Id=Guid.NewGuid().ToString("N"),Name="Subtitle",Kind=SceneSourceKind.Text,Text="Make yourself comfortable. We'll be right with you.",FontSize=36,TextColor=0xAAAAB5,Transform=new SourceTransform{X=225,Y=585,Width=1360,Height=56,Visible=true}}
                    }}
                }};
            }
            if(settings.UseSceneCollection)captureMode="scene";
            RefreshScenePickers();
        }
        private void RefreshScenePickers()
        {
            refreshingScenePickers=true;
            foreach(var picker in new[]{RecordScenePicker,StudioScenePicker}){picker.ItemsSource=null;picker.ItemsSource=sceneLibrary.Scenes;picker.SelectedItem=sceneLibrary.Scenes.FirstOrDefault(s=>s.Id==sceneLibrary.SelectedSceneId);}
            refreshingScenePickers=false;
            SceneCountLabel.Text=sceneLibrary.Scenes.Count+" scenes · "+sceneLibrary.Scenes.Sum(s=>s.Sources.Count)+" layers";
            if(captureMode=="scene")SourceDetail.Text=(sceneLibrary.Scenes.FirstOrDefault(s=>s.Id==sceneLibrary.SelectedSceneId)?.Name??"Scene")+" · composed scene";
        }
        private async void ScenePicker_Changed(object sender,SelectionChangedEventArgs e)
        {
            if(!ready||refreshingScenePickers||((ComboBox)sender).SelectedItem is not SceneDefinition selected)return;
            if(busy||exporting||screenshotBusy){RefreshScenePickers();return;}
            var next=SceneLibrary.Clone(sceneLibrary);next.SelectedSceneId=selected.Id;
            try{await ApplySceneCollection(next,SceneTransition.Fade,300);}catch(Exception ex){StatusLabel.Text=ex.Message;RefreshScenePickers();}
        }
        private SceneCollection ResolveScenes(SceneCollection library,int width,int height)
        {
            if(sceneLoadError!=null)throw new InvalidOperationException("Saved scenes could not be loaded. Open Scene editor, review and apply a collection before capturing. "+sceneLoadError);
            RecorderEngine.ValidateSceneCollectionDefinition(library,false);
            var resolved=SceneLibrary.Clone(library);var screens=Forms.Screen.AllScreens;var identities=screens.Select(GetDisplayIdentity).ToArray();
            var windows=resolved.Scenes.Any(s=>s.Sources.Any(v=>v.Kind==SceneSourceKind.Window||v.Kind==SceneSourceKind.Game))?RecorderEngine.GetCaptureWindows():Array.Empty<CaptureWindow>();
            float scaleX=(float)width/library.CanvasWidth,scaleY=(float)height/library.CanvasHeight;
            foreach(var source in resolved.Scenes.SelectMany(s=>s.Sources))
            {
                source.Transform.X*=scaleX;source.Transform.Y*=scaleY;source.Transform.Width*=scaleX;source.Transform.Height*=scaleY;
                if(source.Kind==SceneSourceKind.Display)
                {
                    int index=Array.FindIndex(identities,v=>v==source.MonitorIdentity);
                    if(index<0)throw new InvalidOperationException("Reconnect or reselect the display for ‘"+source.Name+"’ in Scene editor.");source.MonitorIndex=index;
                }
                if(source.Kind==SceneSourceKind.Window||source.Kind==SceneSourceKind.Game)
                {
                    var matches=windows.Where(w=>w.Title==source.WindowTitle&&w.ClassName==source.WindowClass&&w.AppName==source.WindowExe).ToArray();
                    if(matches.Length!=1)throw new InvalidOperationException("Open and reselect the exact window for ‘"+source.Name+"’ in Scene editor.");source.WindowHandle=matches[0].Handle;
                }
            }
            RecorderEngine.ValidateSceneCollectionDefinition(resolved);return resolved;
        }
        private async Task ApplySceneCollection(SceneCollection next,SceneTransition transition,int duration)
        {
            if(busy||exporting||screenshotBusy)throw new InvalidOperationException("Wait for the current operation to finish, then apply the scene.");
            if(HasActiveSession&&captureMode!="scene")throw new InvalidOperationException("Finish this simple capture, then choose Scenes to use the editor.");
            busy=true;captureRevision++;UpdateQuickState();SetControlsEnabled(false);
            try
            {
                // Idle editing only validates the document. Cameras/pages open on Preview or Start.
                RecorderEngine.ValidateSceneCollectionDefinition(next,false);
                if(HasActiveSession)
                {
                    if(sessionWidth<=0||sessionHeight<=0)throw new InvalidOperationException("The active capture dimensions are unavailable. Finish capture before applying this scene.");
                    var resolved=ResolveScenes(next,sessionWidth,sessionHeight);
                    var before=SceneLibrary.Clone(sceneLibrary);before.SelectedSceneId=next.SelectedSceneId;
                    bool layoutChanged=JsonSerializer.Serialize(before)!=JsonSerializer.Serialize(next);
                    await worker.Run(()=>
                    {
                        if(layoutChanged){string destination=resolved.SelectedSceneId;if(resolved.Scenes.Any(s=>s.Id==recorder.SelectedSceneId))resolved.SelectedSceneId=recorder.SelectedSceneId;recorder.SetSceneCollection(resolved);recorder.SwitchScene(destination,transition,duration);}
                        else recorder.SwitchScene(next.SelectedSceneId,transition,duration);
                    });
                }
                sceneLibrary=SceneLibrary.Clone(next);captureMode="scene";settings.UseSceneCollection=true;
                sceneLoadError=null;
                RefreshScenePickers();UpdateCaptureMode();UpdateOptions();
                if(!automatedTest&&!renderOnly)SceneLibrary.Save(sceneLibrary);
                StatusLabel.Text="Scene ready · "+sceneLibrary.Scenes.First(s=>s.Id==sceneLibrary.SelectedSceneId).Name;
            }
            finally{busy=false;SetControlsEnabled(!HasActiveSession);UpdateQuickState();}
        }
        private void EditScenes_Click(object sender,RoutedEventArgs e)
        {
            if(busy||exporting||screenshotBusy)return;
            if(sceneEditor!=null){sceneEditor.Activate();return;}
            sceneEditor=new SceneEditorWindow(sceneLibrary,sceneLibrary.CanvasWidth,sceneLibrary.CanvasHeight,ApplySceneCollection,CaptureScenePreview){Owner=this};
            sceneEditor.Closed+=(s,args)=>sceneEditor=null;sceneEditor.Show();
        }
        private async Task<ScenePreviewFrame> CaptureScenePreview()
        {
            if(busy||exporting||screenshotBusy)throw new InvalidOperationException("Wait for the current operation to finish.");
            busy=true;captureRevision++;SetControlsEnabled(false);RecordButton.IsEnabled=false;UpdateQuickState();
            bool temporary=!HasActiveSession;
            try
            {
                if(temporary)await EnsureCaptureSession(videoOnly:true);
                return await worker.Run(()=>recorder.CapturePreview());
            }
            finally
            {
                if(temporary)await ReleaseUnusedSession();
                busy=false;RecordButton.IsEnabled=true;UpdateSessionIndicators();
            }
        }
        private async Task ShowScenePreview()
        {
            try
            {
                var frame=await CaptureScenePreview();var bitmap=BitmapSource.Create(frame.Width,frame.Height,96,96,PixelFormats.Bgra32,null,frame.BgraPixels,frame.Stride);bitmap.Freeze();
                PreviewImage.Source=bitmap;PreviewImage.Visibility=Visibility.Visible;PreviewPlaceholder.Visibility=Visibility.Collapsed;((TextBlock)CaptureBadge.Child).Text="SCENE SNAPSHOT · "+DateTime.Now.ToString("HH:mm:ss");StatusLabel.Text="One scene frame captured. Preview is asleep again.";
            }
            catch(Exception ex){StatusLabel.Text="Scene preview failed: "+ex.Message;}
        }
        private void AnimationToggle_Changed(object sender,RoutedEventArgs e)
        {
            if(!ready)return;settings.Animations=AnimationsToggle.IsChecked==true;Motion.Enabled=settings.Animations;UpdateOptions();
        }
    }
}
