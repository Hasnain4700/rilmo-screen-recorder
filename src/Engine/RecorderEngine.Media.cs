using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Rilmo.Engine
{
    public sealed partial class RecorderEngine
    {
        private static IEnumerable<SceneDefinition> SceneBuildOrder(SceneCollection collection)
        {
            var nodes=collection.Scenes.ToDictionary(s=>s.Id,StringComparer.Ordinal);
            var visiting=new HashSet<string>();var depths=new Dictionary<string,int>();var result=new List<SceneDefinition>();
            int Visit(SceneDefinition node)
            {
                if(visiting.Contains(node.Id))throw new ArgumentException("A nested scene cannot refer back to itself.");
                if(depths.TryGetValue(node.Id,out int cached))return cached;
                visiting.Add(node.Id);int depth=0;
                foreach(var source in node.Sources.Where(s=>s.Kind==SceneSourceKind.Scene))
                {
                    if(source.NestedSceneId==null||!nodes.TryGetValue(source.NestedSceneId,out var child))throw new ArgumentException("Choose an existing nested scene for "+source.Name+".");
                    if(source.ColorCorrection||source.ChromaKey)throw new ArgumentException("Apply filters to layers inside a nested scene.");
                    depth=Math.Max(depth,Visit(child)+1);
                }
                if(depth>8)throw new ArgumentException("Nested scenes support up to eight levels.");
                visiting.Remove(node.Id);depths.Add(node.Id,depth);result.Add(node);return depth;
            }
            foreach(var node in collection.Scenes)Visit(node);
            return result;
        }
        private static void ValidateMediaDefinition(SceneSource source,bool availability)
        {
            if(!double.IsFinite(source.Brightness)||Math.Abs(source.Brightness)>1||!double.IsFinite(source.Contrast)||Math.Abs(source.Contrast)>1||!double.IsFinite(source.Saturation)||Math.Abs(source.Saturation)>1||!double.IsFinite(source.Opacity)||source.Opacity<0||source.Opacity>1||source.ChromaSimilarity<1||source.ChromaSimilarity>1000)throw new ArgumentException("Filter values are outside the supported range.");
            if(source.Kind==SceneSourceKind.Media)
            {
                if(string.IsNullOrWhiteSpace(source.FilePath)||!Path.IsPathFullyQualified(source.FilePath))throw new ArgumentException("Choose a local video file.");
                if(availability&&!File.Exists(source.FilePath))throw new FileNotFoundException("Media file missing: "+source.Name);
            }
            if(source.Kind==SceneSourceKind.Slideshow)
            {
                if(source.SlideFiles==null||source.SlideFiles.Count<1||source.SlideFiles.Count>20||source.SlideSeconds<1||source.SlideSeconds>3600)throw new ArgumentException("Choose 1–20 slides and a duration from 1 to 3600 seconds.");
                long bytes=0;
                foreach(string path in source.SlideFiles)
                {
                    if(string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path))throw new ArgumentException("Slide paths must be absolute.");
                    if(availability&&!File.Exists(path))throw new FileNotFoundException("Slideshow image missing: "+path);
                    if(File.Exists(path)){long size=new FileInfo(path).Length;bytes+=size;if(size>32*1024*1024)throw new ArgumentException("Each slide must be smaller than 32 MB.");}
                }
                if(bytes>128*1024*1024)throw new ArgumentException("Choose a slideshow smaller than 128 MB.");
            }
        }
        private IntPtr CreateMediaSource(SceneSource model,string name)
        {
            string type;object values;
            if(model.Kind==SceneSourceKind.Media)
            {
                EnsureFeatureModule("obs-ffmpeg");type="ffmpeg_source";
                values=new {is_local_file=true,local_file=model.FilePath,looping=model.MediaLoop,hw_decode=true,close_when_inactive=true,restart_on_activate=true,clear_on_media_end=true,speed_percent=100};
            }
            else
            {
                EnsureFeatureModule("image-source");EnsureFeatureModule("obs-transitions");type="slideshow";
                values=new {files=model.SlideFiles.Select(f=>new {value=f}).ToArray(),slide_time=model.SlideSeconds*1000,transition_speed=300,transition="fade",loop=true,randomize=false,playback_behavior="pause_unpause",slide_mode="mode_auto",use_custom_size=sessionOptions.Width+"x"+sessionOptions.Height};
            }
            IntPtr data=NativeObs.obs_data_create_from_json(JsonSerializer.Serialize(values));
            try{return NativeObs.obs_source_create(type,name,data,IntPtr.Zero);}finally{NativeObs.obs_data_release(data);}
        }
        private static bool SameVisualFilters(SceneSource a,SceneSource b)=>a.ColorCorrection==b.ColorCorrection&&a.Brightness==b.Brightness&&a.Contrast==b.Contrast&&a.Saturation==b.Saturation&&a.Opacity==b.Opacity&&a.ChromaKey==b.ChromaKey&&a.ChromaSimilarity==b.ChromaSimilarity;
        private void ApplyVisualFilters(IntPtr source,SceneSource model)
        {
            if(!model.ColorCorrection&&!model.ChromaKey)return;
            EnsureFeatureModule("obs-filters");
            void Add(string type,string name,object values)
            {
                IntPtr data=NativeObs.obs_data_create_from_json(JsonSerializer.Serialize(values));IntPtr filter=IntPtr.Zero;
                try{filter=NativeObs.obs_source_create(type,name,data,IntPtr.Zero);if(filter==IntPtr.Zero)throw new InvalidOperationException("Filter unavailable: "+name);NativeObs.obs_source_filter_add(source,filter);}
                finally{if(filter!=IntPtr.Zero)NativeObs.obs_source_release(filter);NativeObs.obs_data_release(data);}
            }
            if(model.ChromaKey)Add("chroma_key_filter_v2","Green screen",new {key_color_type="green",similarity=model.ChromaSimilarity,smoothness=80,spill=100,opacity=1.0,contrast=0.0,brightness=0.0,gamma=0.0});
            if(model.ColorCorrection)Add("color_filter_v2","Color correction",new {brightness=model.Brightness,contrast=model.Contrast,saturation=model.Saturation,opacity=model.Opacity,gamma=0.0,hue_shift=0.0,color_multiply=0xFFFFFFFFu,color_add=0u});
        }
    }
}
