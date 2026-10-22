// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Rilmo.Engine
{
    public sealed partial class RecorderEngine
    {
        private sealed class SceneLayerHandle { internal IntPtr Source, Item; internal SceneSource Model; }
        private sealed class NamedSceneHandle
        {
            internal IntPtr Scene;
            internal readonly Dictionary<string,SceneLayerHandle> Layers = new Dictionary<string,SceneLayerHandle>(StringComparer.Ordinal);
        }
        private Dictionary<string,NamedSceneHandle> namedScenes = new Dictionary<string,NamedSceneHandle>(StringComparer.Ordinal);
        private SceneCollection sceneCollection;
        private IntPtr sceneTransition;
        private SceneTransition activeTransitionKind=SceneTransition.Fade;
        private string transitionFromSceneId;
        private ulong previewAfterVideoTime;
        public string SelectedSceneId { get { return sceneCollection == null ? null : sceneCollection.SelectedSceneId; } }

        public SceneCollection GetSceneCollection() { CheckThread(); return sceneCollection == null ? null : sceneCollection.Clone(); }
        // Managed validation only; this does not load libobs or open a camera/browser.
        // Native source creation/readiness is additionally checked on Apply/Start.
        public static void ValidateSceneCollectionDefinition(SceneCollection collection,bool checkAvailability=true) { ValidateSceneCollection(collection,checkAvailability); }

        // Build and validate a separate graph before changing the program output. Retained
        // sources are shared by reference so repositioning a camera never reopens it.
        public void SetSceneCollection(SceneCollection collection)
        {
            CheckThread();
            if (!initialized) throw new InvalidOperationException("Start a capture session first.");
            SceneCollection replacement = ValidateSceneCollection(collection);
            var built = new Dictionary<string,NamedSceneHandle>(StringComparer.Ordinal);
            IntPtr newTransition = IntPtr.Zero;
            try
            {
                foreach (SceneDefinition definition in SceneBuildOrder(replacement))
                {
                    var node = new NamedSceneHandle { Scene = NativeObs.obs_scene_create_private(definition.Name) };
                    if (node.Scene == IntPtr.Zero) throw new InvalidOperationException("The scene could not be created: " + definition.Name);
                    built.Add(definition.Id,node);
                    foreach (SceneSource model in definition.Sources)
                    {
                        IntPtr source = IntPtr.Zero;
                        try
                        {
                            NamedSceneHandle oldScene; SceneLayerHandle previous;
                            if(model.Kind==SceneSourceKind.Scene) source=NativeObs.obs_source_get_ref(NativeObs.obs_scene_get_source(built[model.NestedSceneId].Scene));
                            else if (namedScenes.TryGetValue(definition.Id,out oldScene) && oldScene.Layers.TryGetValue(model.Id,out previous) && SameSourceContent(previous.Model,model))
                                source = NativeObs.obs_source_get_ref(previous.Source);
                            else
                            {
                                source=FindSharedCaptureSource(built,model);
                                if(source==IntPtr.Zero) source=FindSharedCaptureSource(namedScenes,model);
                                if(source==IntPtr.Zero) source=CreateSceneLayer(model,definition.Id + ":" + model.Id);
                            }
                            if (source == IntPtr.Zero) throw new InvalidOperationException("The source could not be created: " + model.Name);
                            IntPtr item = NativeObs.obs_scene_add(node.Scene,source);
                            if (item == IntPtr.Zero) throw new InvalidOperationException("The source could not be added to its scene: " + model.Name);
                            node.Layers.Add(model.Id,new SceneLayerHandle {Source=source,Item=item,Model=model});
                            source=IntPtr.Zero;
                            SceneLayerHandle layer=node.Layers[model.Id];
                            ValidateCrop(model.Crop,layer.Source);
                            ApplyCrop(layer.Item,model.Crop); ApplySceneTransform(layer.Item,model);
                        }
                        finally { if(source!=IntPtr.Zero) NativeObs.obs_source_release(source); }
                    }
                }
                if(sceneTransition==IntPtr.Zero)
                {
                    EnsureFeatureModule("obs-transitions");
                    newTransition=NativeObs.obs_source_create("fade_transition","Rilmo program transition",IntPtr.Zero,IntPtr.Zero);
                    if(newTransition==IntPtr.Zero) throw new InvalidOperationException("The fade transition could not initialize.");
                    NativeObs.obs_transition_set_size(newTransition,(uint)sessionOptions.Width,(uint)sessionOptions.Height);
                }
                IntPtr transition=sceneTransition==IntPtr.Zero?newTransition:sceneTransition;
                NamedSceneHandle selected=built[replacement.SelectedSceneId];
                IntPtr selectedSource=NativeObs.obs_scene_get_source(selected.Scene);
                // Keep shared visible inputs showing across the remove-old/add-new
                // child sequence inside obs_transition_set (especially webcams).
                NativeObs.obs_source_inc_showing(selectedSource);
                try {NativeObs.obs_transition_set(transition,selectedSource);NativeObs.obs_set_output_source(0,transition);}
                finally {NativeObs.obs_source_dec_showing(selectedSource);}
                if(sceneTransition==IntPtr.Zero) { sceneTransition=newTransition;newTransition=IntPtr.Zero;activeTransitionKind=SceneTransition.Fade; }
                var old=namedScenes;
                if(sceneCollection==null) ReleaseLegacySceneForReplacement();
                namedScenes=built; built=null; sceneCollection=replacement; scene=selected.Scene; transitionFromSceneId=null;
                previewAfterVideoTime=NativeObs.obs_get_video_frame_time();
                recordedWindow=IntPtr.Zero;
                ReleaseNamedScenes(old);
            }
            finally
            {
                if(newTransition!=IntPtr.Zero) NativeObs.obs_source_release(newTransition);
                if(built!=null) ReleaseNamedScenes(built);
            }
        }

        public void SwitchScene(string sceneId,SceneTransition transition=SceneTransition.Cut,int durationMs=300)
        {
            RequireNamedScenes(); NamedSceneHandle target;
            if(sceneId==null || !namedScenes.TryGetValue(sceneId,out target)) throw new ArgumentException("The scene does not exist.");
            if(!Enum.IsDefined(transition)) throw new ArgumentException("Choose an available transition.");
            if(durationMs<50 || durationMs>5000) throw new ArgumentException("Fade duration must be between 50 and 5000 milliseconds.");
            if(sceneId==SelectedSceneId) return;
            foreach(var layer in target.Layers.Values) ValidateSourceAvailability(layer.Model);
            string previous=SelectedSceneId;
            if(transition!=SceneTransition.Cut && transition!=activeTransitionKind)
            {
                string type=transition==SceneTransition.Swipe?"swipe_transition":transition==SceneTransition.Slide?"slide_transition":transition==SceneTransition.FadeToBlack?"fade_to_color_transition":"fade_transition";
                IntPtr next=NativeObs.obs_source_create(type,"Rilmo program transition",IntPtr.Zero,IntPtr.Zero);
                if(next==IntPtr.Zero)throw new InvalidOperationException("Transition unavailable.");
                NativeObs.obs_transition_set_size(next,(uint)sessionOptions.Width,(uint)sessionOptions.Height);
                NativeObs.obs_transition_set(next,NativeObs.obs_scene_get_source(namedScenes[previous].Scene));
                NativeObs.obs_set_output_source(0,next);
                NativeObs.obs_transition_clear(sceneTransition);NativeObs.obs_source_release(sceneTransition);sceneTransition=next;activeTransitionKind=transition;
            }
            if(transition==SceneTransition.Cut) NativeObs.obs_transition_set(sceneTransition,NativeObs.obs_scene_get_source(target.Scene));
            else if(!NativeObs.obs_transition_start(sceneTransition,0,(uint)durationMs,NativeObs.obs_scene_get_source(target.Scene)))
                throw new InvalidOperationException("The scene transition could not start.");
            transitionFromSceneId=transition!=SceneTransition.Cut?previous:null;
            sceneCollection.SelectedSceneId=sceneId; scene=target.Scene;
            previewAfterVideoTime=NativeObs.obs_get_video_frame_time();
        }

        public void AddScene(SceneDefinition definition) { var copy=RequireCollectionCopy(); copy.Scenes.Add(definition); SetSceneCollection(copy); }
        public void RemoveScene(string sceneId)
        {
            var copy=RequireCollectionCopy(); var found=FindScene(copy,sceneId); copy.Scenes.Remove(found);
            if(copy.SelectedSceneId==sceneId && copy.Scenes.Count!=0) copy.SelectedSceneId=copy.Scenes[0].Id;
            SetSceneCollection(copy);
        }
        public void AddSceneSource(string sceneId,SceneSource source) {var copy=RequireCollectionCopy();FindScene(copy,sceneId).Sources.Add(source);SetSceneCollection(copy);}
        public void UpdateSceneSource(string sceneId,SceneSource source)
        {
            if(source==null) throw new ArgumentNullException("source");
            var copy=RequireCollectionCopy();var definition=FindScene(copy,sceneId);int index=definition.Sources.FindIndex(s=>s.Id==source.Id);
            if(index<0) throw new ArgumentException("The source does not exist."); definition.Sources[index]=source;SetSceneCollection(copy);
        }
        public void RemoveSceneSource(string sceneId,string sourceId)
        {var copy=RequireCollectionCopy();var definition=FindScene(copy,sceneId);var source=FindSource(definition,sourceId);definition.Sources.Remove(source);SetSceneCollection(copy);}
        public void MoveSceneSource(string sceneId,string sourceId,int index)
        {
            RequireNamedScenes();var definition=FindScene(sceneCollection,sceneId);var source=FindSource(definition,sourceId);
            if(index<0 || index>=definition.Sources.Count) throw new ArgumentOutOfRangeException("index");
            NativeObs.obs_sceneitem_set_order_position(namedScenes[sceneId].Layers[sourceId].Item,index);
            definition.Sources.Remove(source);definition.Sources.Insert(index,source);
            previewAfterVideoTime=NativeObs.obs_get_video_frame_time();
        }
        public void SetSceneSourceTransform(string sceneId,string sourceId,SourceTransform transform)
        {
            RequireNamedScenes();ValidateTransform(transform);var source=FindSource(FindScene(sceneCollection,sceneId),sourceId);
            source.Transform=SceneSource.CloneTransform(transform);ApplySceneTransform(namedScenes[sceneId].Layers[sourceId].Item,source);
            previewAfterVideoTime=NativeObs.obs_get_video_frame_time();
        }
        public void SetSceneSourceCrop(string sceneId,string sourceId,SourceCrop crop)
        {
            RequireNamedScenes();var source=FindSource(FindScene(sceneCollection,sceneId),sourceId);var layer=namedScenes[sceneId].Layers[sourceId];
            ValidateCrop(crop,layer.Source);ApplyCrop(layer.Item,crop);source.Crop=crop.Clone();
            previewAfterVideoTime=NativeObs.obs_get_video_frame_time();
        }
        private void RequireNamedScenes() { RequireSession(); if(sceneCollection==null) throw new InvalidOperationException("This session uses the simple capture canvas. Apply a scene collection first."); }
        private SceneCollection RequireCollectionCopy() { RequireNamedScenes();return sceneCollection.Clone(); }
        private static SceneDefinition FindScene(SceneCollection collection,string id)
        {var result=collection.Scenes.Find(s=>s.Id==id);if(result==null) throw new ArgumentException("The scene does not exist.");return result;}
        private static SceneSource FindSource(SceneDefinition scene,string id)
        {var result=scene.Sources.Find(s=>s.Id==id);if(result==null) throw new ArgumentException("The source does not exist.");return result;}
        private static SceneCollection ValidateSceneCollection(SceneCollection input,bool checkAvailability=true)
        {
            if(input==null || input.Scenes==null || input.Scenes.Count<1 || input.Scenes.Count>16) throw new ArgumentException("A collection needs between 1 and 16 scenes.");
            if(input.CanvasWidth<16 || input.CanvasWidth>8192 || input.CanvasHeight<16 || input.CanvasHeight>8192) throw new ArgumentException("Scene canvas dimensions must be between 16 and 8192 pixels per side.");
            var copy=input.Clone();var ids=new HashSet<string>(StringComparer.Ordinal);int total=0;
            foreach(var definition in copy.Scenes)
            {
                if(definition==null || !ValidId(definition.Id) || !ids.Add(definition.Id) || String.IsNullOrWhiteSpace(definition.Name) || definition.Name.Length>120)
                    throw new ArgumentException("Each scene needs a unique ID and a name from 1 to 120 characters.");
                if(definition.Sources==null || definition.Sources.Count>32 || (total+=definition.Sources.Count)>128) throw new ArgumentException("Use at most 32 layers per scene and 128 layers per collection.");
                var sourceIds=new HashSet<string>(StringComparer.Ordinal);
                foreach(var source in definition.Sources)
                {
                    if(source==null || !ValidId(source.Id) || !sourceIds.Add(source.Id) || String.IsNullOrWhiteSpace(source.Name) || source.Name.Length>120 || !Enum.IsDefined(typeof(SceneSourceKind),source.Kind))
                        throw new ArgumentException("Each source needs a unique ID, a name, and a supported source kind.");
                    ValidateTransform(source.Transform);ValidateCrop(source.Crop,IntPtr.Zero);
                    ValidateSourceDefinition(source);
                    if(checkAvailability) ValidateSourceAvailability(source);
                    if(source.Kind==SceneSourceKind.Text && (String.IsNullOrEmpty(source.Text) || source.Text.Length>4096 || source.FontSize<8 || source.FontSize>256))
                        throw new ArgumentException("Text needs 1–4096 characters and a font size from 8 to 256.");
                    if(source.Kind==SceneSourceKind.Image && File.Exists(source.FilePath) && new FileInfo(source.FilePath).Length>32*1024*1024) throw new ArgumentException("Choose an image smaller than 32 MB.");
                }
            }
            if(String.IsNullOrEmpty(copy.SelectedSceneId) || !ids.Contains(copy.SelectedSceneId)) throw new ArgumentException("Select an existing scene.");
            _=SceneBuildOrder(copy);
            return copy;
        }
        private static bool ValidId(string id) { return !String.IsNullOrWhiteSpace(id) && id.Length<=100; }
        private static void ValidateSourceDefinition(SceneSource source)
        {
            ValidateMediaDefinition(source,false);
            if(source.Kind==SceneSourceKind.Camera && String.IsNullOrWhiteSpace(source.DeviceId)) throw new ArgumentException("Choose a camera for source: " + source.Name);
            if(source.Kind==SceneSourceKind.Image && String.IsNullOrWhiteSpace(source.FilePath)) throw new ArgumentException("Choose an image for source: " + source.Name);
            if(source.Kind==SceneSourceKind.Game && source.GameCaptureMode!="window") throw new ArgumentException("Game capture requires an explicitly selected game window.");
            if(source.Kind==SceneSourceKind.Browser)
            {
                if(source.BrowserWidth<16 || source.BrowserWidth>8192 || source.BrowserHeight<16 || source.BrowserHeight>8192 || source.BrowserFps<1 || source.BrowserFps>60) throw new ArgumentException("Browser sources need dimensions from 16 to 8192 and frame rate from 1 to 60.");
                if(String.IsNullOrWhiteSpace(source.FilePath))
                {
                    Uri uri;
                    if(!Uri.TryCreate(source.BrowserUrl,UriKind.Absolute,out uri) || (uri.Scheme!=Uri.UriSchemeHttp && uri.Scheme!=Uri.UriSchemeHttps) || !String.IsNullOrEmpty(uri.UserInfo)) throw new ArgumentException("Enter an HTTP or HTTPS browser URL without embedded credentials.");
                }
                else
                {
                    string extension=Path.GetExtension(source.FilePath);
                    if(!extension.Equals(".html",StringComparison.OrdinalIgnoreCase) && !extension.Equals(".htm",StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("A local browser source must be an HTML file.");
                }
            }
        }
        private static void ValidateSourceAvailability(SceneSource source)
        {
            ValidateMediaDefinition(source,true);
            if(source.Kind==SceneSourceKind.Display && (source.MonitorIndex<0 || source.MonitorIndex>=Screen.AllScreens.Length)) throw new ArgumentException("A scene display is no longer connected.");
            if((source.Kind==SceneSourceKind.Window || source.Kind==SceneSourceKind.Game) && WindowCatalog.Describe(source.WindowHandle)==null)
                throw new ArgumentException("Restore or reselect the window for source: " + source.Name);
            if(source.Kind==SceneSourceKind.Image && (String.IsNullOrWhiteSpace(source.FilePath) || !File.Exists(source.FilePath))) throw new FileNotFoundException("The scene image is missing: " + source.Name);
            if(source.Kind==SceneSourceKind.Browser && !String.IsNullOrWhiteSpace(source.FilePath) && !File.Exists(source.FilePath)) throw new FileNotFoundException("The browser source HTML file is missing: " + source.Name);
        }
        private static bool SameSourceContent(SceneSource a,SceneSource b)
        {
            if(a.Kind!=b.Kind || !SameVisualFilters(a,b)) return false;
            switch(a.Kind)
            {
                case SceneSourceKind.Display:return a.MonitorIndex==b.MonitorIndex && a.MonitorIdentity==b.MonitorIdentity && a.CaptureCursor==b.CaptureCursor;
                case SceneSourceKind.Window:return a.WindowHandle==b.WindowHandle && a.CaptureCursor==b.CaptureCursor;
                case SceneSourceKind.Game:return a.WindowHandle==b.WindowHandle && a.CaptureCursor==b.CaptureCursor && a.GameCaptureMode==b.GameCaptureMode;
                case SceneSourceKind.Browser:return a.FilePath==b.FilePath && a.BrowserUrl==b.BrowserUrl && a.BrowserWidth==b.BrowserWidth && a.BrowserHeight==b.BrowserHeight && a.BrowserFps==b.BrowserFps;
                case SceneSourceKind.Camera:return a.DeviceId==b.DeviceId;
                case SceneSourceKind.Media:return a.FilePath==b.FilePath&&a.MediaLoop==b.MediaLoop&&a.MediaAudio==b.MediaAudio;
                case SceneSourceKind.Slideshow:return a.SlideSeconds==b.SlideSeconds&&String.Join("\n",a.SlideFiles)==String.Join("\n",b.SlideFiles);
                case SceneSourceKind.Image:return a.FilePath==b.FilePath;
                case SceneSourceKind.Text:return a.Text==b.Text && a.FontSize==b.FontSize && a.TextColor==b.TextColor;
                case SceneSourceKind.Color:return a.Color==b.Color;
                default:return false;
            }
        }
        private static IntPtr FindSharedCaptureSource(Dictionary<string,NamedSceneHandle> nodes,SceneSource model)
        {
            // Hardware inputs are shared across scenes instead of opening another
            // camera/capture session for each scene. Item geometry stays independent.
            if(model.Kind!=SceneSourceKind.Camera && model.Kind!=SceneSourceKind.Display && model.Kind!=SceneSourceKind.Window && model.Kind!=SceneSourceKind.Game) return IntPtr.Zero;
            foreach(var node in nodes.Values) foreach(var layer in node.Layers.Values)
                if(SameSourceContent(layer.Model,model)) return NativeObs.obs_source_get_ref(layer.Source);
            return IntPtr.Zero;
        }
        private IntPtr CreateSceneLayer(SceneSource model,string uniqueName)
        {
            IntPtr result=IntPtr.Zero;
            try
            {
                if(model.Kind==SceneSourceKind.Display) result=CreateDisplaySource(new RecorderOptions {CaptureCursor=model.CaptureCursor},Screen.AllScreens[model.MonitorIndex],preferWindowsCapture?2:0);
                else if(model.Kind==SceneSourceKind.Window) result=CreateWindowSource(new RecorderOptions {CaptureCursor=model.CaptureCursor},WindowCatalog.Describe(model.WindowHandle));
                else if(model.Kind==SceneSourceKind.Media || model.Kind==SceneSourceKind.Slideshow) result=CreateMediaSource(model,uniqueName);
                else if(model.Kind==SceneSourceKind.Game || model.Kind==SceneSourceKind.Browser) result=CreateAdvancedSource(model,uniqueName);
                else
                {
                    string type;
                    if(model.Kind==SceneSourceKind.Camera)
                    {
                        EnsureFeatureModule("win-dshow"); bool found=false;
                        foreach(var device in EnumerateCameraDevices()) if(device.Id==model.DeviceId) found=true;
                        if(!found) throw new InvalidOperationException("The scene camera is unavailable: " + model.Name);
                        type="dshow_input";
                    }
                    else if(model.Kind==SceneSourceKind.Image) {EnsureFeatureModule("image-source");type="image_source";}
                    else if(model.Kind==SceneSourceKind.Color) {EnsureFeatureModule("image-source");type="color_source_v3";}
                    else {EnsureFeatureModule("obs-text");type="text_gdiplus_v3";}
                    IntPtr settings=NativeObs.obs_data_create();
                    try
                    {
                        if(model.Kind==SceneSourceKind.Camera)
                        {
                            NativeObs.obs_data_set_string(settings,"video_device_id",model.DeviceId);NativeObs.obs_data_set_int(settings,"res_type",0);
                            NativeObs.obs_data_set_bool(settings,"deactivate_when_not_showing",true);NativeObs.obs_data_set_bool(settings,"use_custom_audio_device",true);NativeObs.obs_data_set_string(settings,"audio_device_id","");
                        }
                        else if(model.Kind==SceneSourceKind.Image) {NativeObs.obs_data_set_string(settings,"file",Path.GetFullPath(model.FilePath));NativeObs.obs_data_set_bool(settings,"unload",false);}
                        else if(model.Kind==SceneSourceKind.Color)
                        {
                            NativeObs.obs_data_set_int(settings,"color",ToNativeColor(model.Color));NativeObs.obs_data_set_int(settings,"width",sessionOptions.Width);NativeObs.obs_data_set_int(settings,"height",sessionOptions.Height);
                        }
                        else
                        {
                            NativeObs.obs_data_set_string(settings,"text",model.Text);NativeObs.obs_data_set_int(settings,"color",ToNativeColor(model.TextColor));
                            IntPtr font=NativeObs.obs_data_create();try {NativeObs.obs_data_set_string(font,"face","Segoe UI");NativeObs.obs_data_set_int(font,"size",model.FontSize);NativeObs.obs_data_set_obj(settings,"font",font);} finally {NativeObs.obs_data_release(font);}
                        }
                        result=NativeObs.obs_source_create(type,uniqueName,settings,IntPtr.Zero);
                    }
                    finally {NativeObs.obs_data_release(settings);}
                }
                if(result==IntPtr.Zero) throw new InvalidOperationException("The scene source could not initialize: " + model.Name);
                NativeObs.obs_source_set_audio_mixers(result,model.Kind==SceneSourceKind.Media&&model.MediaAudio?1u:0u);
                // Browser loads only when its scene is shown. Its dimensions do not
                // prove page readiness. Other capture devices must actually open.
                if(model.Kind!=SceneSourceKind.Browser)
                {
                    bool ready;
                    if(model.Kind==SceneSourceKind.Media)NativeObs.obs_source_inc_active(result);
                    try{ready=WarmSceneSource(result,model.Kind==SceneSourceKind.Game?8000:(model.Kind==SceneSourceKind.Display&&!preferWindowsCapture?1200:5000));}
                    finally{if(model.Kind==SceneSourceKind.Media)NativeObs.obs_source_dec_active(result);}
                    if(!ready && model.Kind==SceneSourceKind.Display && !preferWindowsCapture)
                    {
                        NativeObs.obs_source_release(result);result=IntPtr.Zero;
                        result=CreateDisplaySource(new RecorderOptions {CaptureCursor=model.CaptureCursor},Screen.AllScreens[model.MonitorIndex],2);
                        NativeObs.obs_source_set_audio_mixers(result,0);ready=WarmSceneSource(result,5000);
                        if(ready) preferWindowsCapture=true;
                    }
                    if(!ready) throw new InvalidOperationException(model.Kind==SceneSourceKind.Game ? "Game capture could not hook the selected window. Check game capture compatibility and permissions; no fallback window was captured." : "The source produced no video: " + model.Name);
                }
                ApplyVisualFilters(result,model);
                return result;
            }
            catch {if(result!=IntPtr.Zero) NativeObs.obs_source_release(result);throw;}
        }
        private static bool WarmSceneSource(IntPtr source,int timeout)
        {
            NativeObs.obs_source_inc_showing(source);
            try
            {
                Stopwatch wait=Stopwatch.StartNew();
                while(NativeObs.obs_source_get_width(source)==0 && wait.ElapsedMilliseconds<timeout) Thread.Sleep(40);
                return NativeObs.obs_source_get_width(source)>0;
            }
            finally {NativeObs.obs_source_dec_showing(source);}
        }
        private static uint ToNativeColor(uint color) {return (color&0xFF00FF00) | ((color&0xFF)<<16) | ((color>>16)&0xFF);}
        private static void ValidateCrop(SourceCrop crop,IntPtr source)
        {
            if(crop==null || crop.Left<0 || crop.Top<0 || crop.Right<0 || crop.Bottom<0 || crop.Left>16384 || crop.Top>16384 || crop.Right>16384 || crop.Bottom>16384) throw new ArgumentException("Crop edges must be between 0 and 16384 pixels.");
            if(source!=IntPtr.Zero)
            {
                uint width=NativeObs.obs_source_get_width(source),height=NativeObs.obs_source_get_height(source);
                if((width>0 && (long)crop.Left+crop.Right>=width) || (height>0 && (long)crop.Top+crop.Bottom>=height)) throw new ArgumentException("The crop must leave at least one source pixel in each dimension.");
            }
        }
        private static void ApplyCrop(IntPtr item,SourceCrop crop)
        {var native=new NativeObs.SceneCrop {Left=crop.Left,Top=crop.Top,Right=crop.Right,Bottom=crop.Bottom};NativeObs.obs_sceneitem_set_crop(item,ref native);}
        private static void ApplySceneTransform(IntPtr item,SceneSource source)
        {
            ApplyTransform(item,source.Transform);
            // A solid color is a rectangle, with no image aspect ratio to preserve.
            if(source.Kind==SceneSourceKind.Color) NativeObs.obs_sceneitem_set_bounds_type(item,1);
        }
        private void ReleaseLegacySceneForReplacement()
        {
            foreach(var overlay in overlayHandles.Values) {NativeObs.obs_sceneitem_remove(overlay.Item);NativeObs.obs_source_release(overlay.Source);} overlayHandles.Clear();
            if(scene!=IntPtr.Zero) {NativeObs.obs_scene_release(scene);scene=IntPtr.Zero;}
            if(capture!=IntPtr.Zero) {NativeObs.obs_source_release(capture);capture=IntPtr.Zero;}captureItem=IntPtr.Zero;
        }
        private static void ReleaseNamedScenes(Dictionary<string,NamedSceneHandle> nodes)
        {
            foreach(var node in nodes.Values)
            {
                NativeObs.obs_scene_release(node.Scene);
                foreach(var layer in node.Layers.Values) NativeObs.obs_source_release(layer.Source);
            }
            nodes.Clear();
        }
        private void ReleaseSceneCollection()
        {
            if(sceneTransition!=IntPtr.Zero) {NativeObs.obs_transition_clear(sceneTransition);NativeObs.obs_source_release(sceneTransition);sceneTransition=IntPtr.Zero;}
            if(sceneCollection!=null) scene=IntPtr.Zero;
            ReleaseNamedScenes(namedScenes);sceneCollection=null;transitionFromSceneId=null;previewAfterVideoTime=0;
        }
        private string GetUnavailableProgramWindow()
        {
            if(sceneCollection==null) return null;
            if(transitionFromSceneId!=null && !NativeObs.obs_transition_is_active(sceneTransition)) transitionFromSceneId=null;
            var activeScenes=new HashSet<string>(StringComparer.Ordinal);
            void Include(string id)
            {
                if(id==null||!activeScenes.Add(id))return;
                foreach(var layer in FindScene(sceneCollection,id).Sources)
                    if(layer.Transform.Visible&&layer.Kind==SceneSourceKind.Scene)Include(layer.NestedSceneId);
            }
            Include(SelectedSceneId);Include(transitionFromSceneId);
            foreach(var definition in sceneCollection.Scenes)
            {
                if(!activeScenes.Contains(definition.Id))continue;
                foreach(var source in definition.Sources)
                    if(source.Transform.Visible && (source.Kind==SceneSourceKind.Window || source.Kind==SceneSourceKind.Game) && (!WindowCatalog.IsWindow(source.WindowHandle) || WindowCatalog.IsIconic(source.WindowHandle))) return source.Name;
            }
            return null;
        }

        // A bounded on-demand GPU readback, never a background preview loop. The
        // caller controls cadence and discards frames from stale source revisions.
        public ScenePreviewFrame CapturePreview(int maxWidth=640,int maxHeight=360)
        {
            RequireSession();
            if(maxWidth<16 || maxHeight<16 || maxWidth>1920 || maxHeight>1080) throw new ArgumentException("Preview dimensions must fit between 16 x 16 and 1920 x 1080.");
            if(previewAfterVideoTime!=0)
            {
                // Scene geometry/show callbacks are applied by the video loop. Wait
                // for actual ticks after the edit, not an arbitrary startup sleep.
                ulong observed=previewAfterVideoTime;int ticks=0;var wait=Stopwatch.StartNew();
                while(ticks<2 && wait.ElapsedMilliseconds<2000)
                {
                    ulong current=NativeObs.obs_get_video_frame_time();
                    if(current!=observed){observed=current;ticks++;} else Thread.Sleep(5);
                }
                if(ticks<2) throw new TimeoutException("The scene has not produced a video frame yet. Try preview again after the graphics device responds.");
                previewAfterVideoTime=0;
            }
            double scale=Math.Min(1,Math.Min((double)maxWidth/sessionOptions.Width,(double)maxHeight/sessionOptions.Height));
            int width=Math.Max(1,(int)(sessionOptions.Width*scale)),height=Math.Max(1,(int)(sessionOptions.Height*scale));
            var result=new ScenePreviewFrame {Width=width,Height=height,Stride=width*4,BgraPixels=new byte[width*height*4]};
            IntPtr source=NativeObs.obs_get_output_source(0),texture=IntPtr.Zero,surface=IntPtr.Zero;
            if(source==IntPtr.Zero) throw new InvalidOperationException("No program source is available.");
            NativeObs.obs_enter_graphics();
            try
            {
                texture=NativeObs.gs_texrender_create(5,0);surface=NativeObs.gs_stagesurface_create((uint)width,(uint)height,5);
                if(texture==IntPtr.Zero || surface==IntPtr.Zero || !NativeObs.gs_texrender_begin(texture,(uint)width,(uint)height)) throw new InvalidOperationException("The preview texture could not be created.");
                try
                {
                    var clear=new NativeObs.Vec4 {W=1};NativeObs.gs_clear(1,ref clear,0,0);
                    NativeObs.gs_viewport_push();NativeObs.gs_projection_push();NativeObs.gs_blend_state_push();
                    try
                    {
                        NativeObs.gs_ortho(0,sessionOptions.Width,0,sessionOptions.Height,-100,100);NativeObs.gs_set_viewport(0,0,width,height);NativeObs.gs_blend_function(1,0);
                        NativeObs.obs_source_video_render(source);
                    }
                    finally {NativeObs.gs_blend_state_pop();NativeObs.gs_projection_pop();NativeObs.gs_viewport_pop();}
                }
                finally {NativeObs.gs_texrender_end(texture);}
                NativeObs.gs_stage_texture(surface,NativeObs.gs_texrender_get_texture(texture));
                IntPtr data;uint stride;
                if(!NativeObs.gs_stagesurface_map(surface,out data,out stride)) throw new InvalidOperationException("The preview texture could not be read.");
                try {for(int y=0;y<height;y++) Marshal.Copy(IntPtr.Add(data,checked((int)(y*stride))),result.BgraPixels,y*result.Stride,result.Stride);}
                finally {NativeObs.gs_stagesurface_unmap(surface);}
                return result;
            }
            finally
            {
                if(surface!=IntPtr.Zero) NativeObs.gs_stagesurface_destroy(surface);
                if(texture!=IntPtr.Zero) NativeObs.gs_texrender_destroy(texture);
                NativeObs.obs_leave_graphics();NativeObs.obs_source_release(source);
            }
        }
    }
}
