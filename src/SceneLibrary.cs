// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.IO;
using System.Text.Json;
using Rilmo.Engine;

namespace Rilmo
{
    internal static class SceneLibrary
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented=true };
        internal static string LibraryPath => Path.Combine(Settings.DataDirectory,"scenes.json");
        internal static SceneCollection Clone(SceneCollection source) => JsonSerializer.Deserialize<SceneCollection>(JsonSerializer.Serialize(source,Json),Json);
        internal static SceneCollection Load()
        {
            if(!File.Exists(LibraryPath)) return null;
            return Read(LibraryPath);
        }
        internal static SceneCollection Read(string path)
        {
            if(new FileInfo(path).Length>2*1024*1024) throw new InvalidDataException("Choose a scene file smaller than 2 MB.");
            var collection=JsonSerializer.Deserialize<SceneCollection>(File.ReadAllText(path),Json);
            if(collection?.Scenes==null || collection.Scenes.Count==0 || collection.Scenes.Count>16) throw new InvalidDataException("A scene collection needs 1–16 scenes.");
            foreach(var scene in collection.Scenes)
                if(scene==null || string.IsNullOrWhiteSpace(scene.Id) || string.IsNullOrWhiteSpace(scene.Name) || scene.Sources==null || scene.Sources.Count>32) throw new InvalidDataException("A scene is incomplete or has more than 32 sources.");
            RecorderEngine.ValidateSceneCollectionDefinition(collection,false);
            return collection;
        }
        internal static void Save(SceneCollection collection,string path=null)
        {
            path=path??LibraryPath;
            RecorderEngine.ValidateSceneCollectionDefinition(collection,false);
            string content=JsonSerializer.Serialize(collection,Json);
            if(content.Length>2*1024*1024) throw new InvalidDataException("The scene collection is too large.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try { File.WriteAllText(temporary,content); File.Move(temporary,path,true); }
            finally { if(File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
