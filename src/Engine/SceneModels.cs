// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Rilmo.Engine
{
    public sealed partial class RecorderOptions { public SceneCollection SceneCollection { get; set; } }
    public enum SceneSourceKind { Display, Window, Game, Browser, Camera, Image, Text, Color, Media, Slideshow, Scene }
    public enum SceneTransition { Cut, Fade, Swipe, Slide, FadeToBlack }
    public sealed class SourceCrop
    {
        public int Left { get; set; }
        public int Top { get; set; }
        public int Right { get; set; }
        public int Bottom { get; set; }
        internal SourceCrop Clone() { return (SourceCrop)MemberwiseClone(); }
    }
    public sealed class SceneSource
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public SceneSourceKind Kind { get; set; }
        public int MonitorIndex { get; set; }
        public string MonitorIdentity { get; set; }
        [JsonIgnore] public IntPtr WindowHandle { get; set; }
        public string WindowTitle { get; set; }
        public string WindowClass { get; set; }
        public string WindowExe { get; set; }
        public bool CaptureCursor { get; set; } = true;
        public string DeviceId { get; set; }
        public string FilePath { get; set; }
        public string Text { get; set; }
        public int FontSize { get; set; } = 32;
        public uint TextColor { get; set; } = 0xFFFFFF;
        // UI-friendly AARRGGBB; the native factory converts to libobs' AABBGGRR.
        public uint Color { get; set; } = 0xFF202028;
        public string BrowserUrl { get; set; }
        public int BrowserWidth { get; set; } = 1280;
        public int BrowserHeight { get; set; } = 720;
        public int BrowserFps { get; set; } = 30;
        public string GameCaptureMode { get; set; } = "window";
        public string NestedSceneId { get; set; }
        public bool MediaLoop { get; set; } = true;
        public bool MediaAudio { get; set; }
        public List<string> SlideFiles { get; set; } = new List<string>();
        public int SlideSeconds { get; set; } = 5;
        public bool ColorCorrection { get; set; }
        public double Brightness { get; set; }
        public double Contrast { get; set; }
        public double Saturation { get; set; }
        public double Opacity { get; set; } = 1;
        public bool ChromaKey { get; set; }
        public int ChromaSimilarity { get; set; } = 400;
        public SourceTransform Transform { get; set; } = new SourceTransform();
        public SourceCrop Crop { get; set; } = new SourceCrop();
        internal SceneSource Clone()
        {
            var copy=(SceneSource)MemberwiseClone(); copy.Transform=CloneTransform(Transform); copy.Crop=Crop==null?null:Crop.Clone(); copy.SlideFiles=SlideFiles==null?null:new List<string>(SlideFiles); return copy;
        }
        internal static SourceTransform CloneTransform(SourceTransform value)
        {
            return value==null?null:new SourceTransform { X=value.X,Y=value.Y,Width=value.Width,Height=value.Height,Rotation=value.Rotation,Visible=value.Visible };
        }
    }
    public sealed class SceneDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        // Back to front: index zero is the background layer.
        public List<SceneSource> Sources { get; set; } = new List<SceneSource>();
        internal SceneDefinition Clone()
        {
            var result=new SceneDefinition {Id=Id,Name=Name,Sources=Sources==null?null:new List<SceneSource>()};
            if(Sources!=null) foreach(var source in Sources) result.Sources.Add(source==null?null:source.Clone()); return result;
        }
    }
    public sealed class SceneCollection
    {
        public int CanvasWidth { get; set; } = 1280;
        public int CanvasHeight { get; set; } = 720;
        public List<SceneDefinition> Scenes { get; set; } = new List<SceneDefinition>();
        public string SelectedSceneId { get; set; }
        internal SceneCollection Clone()
        {
            var result=new SceneCollection {CanvasWidth=CanvasWidth,CanvasHeight=CanvasHeight,SelectedSceneId=SelectedSceneId,Scenes=Scenes==null?null:new List<SceneDefinition>()};
            if(Scenes!=null) foreach(var scene in Scenes) result.Scenes.Add(scene==null?null:scene.Clone()); return result;
        }
    }
    public sealed class ScenePreviewFrame
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int Stride { get; set; }
        public byte[] BgraPixels { get; set; }
    }
}
