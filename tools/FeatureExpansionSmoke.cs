using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Rilmo.Engine;

internal static class FeatureExpansionSmoke
{
    static readonly List<object> checks=new List<object>();
    static SceneSource ColorLayer(string id,uint color)=>new SceneSource{Id=id,Name=id,Kind=SceneSourceKind.Color,Color=color,Transform=new SourceTransform{Width=640,Height=360}};
    static SceneDefinition Scene(string id,params SceneSource[] layers)=>new SceneDefinition{Id=id,Name=id,Sources=layers.ToList()};
    static RecorderOptions Options(SceneCollection scenes,string output)=>new RecorderOptions{Width=640,Height=360,Fps=30,SystemAudio=false,Microphone=false,SceneCollection=scenes,OutputDirectory=output,SeparateAudioTracks=true};
    static (int R,int G,int B) Pixel(RecorderClient client){var f=client.CapturePreview(320,180);int i=f.Stride*(f.Height/2)+(f.Width/2)*4;return(f.BgraPixels[i+2],f.BgraPixels[i+1],f.BgraPixels[i]);}
    static void Expect(RecorderClient client,string label,Func<(int R,int G,int B),bool> match,int timeout=4000)
    {
        var timer=System.Diagnostics.Stopwatch.StartNew();(int R,int G,int B) pixel=default;
        do{Thread.Sleep(80);pixel=Pixel(client);if(match(pixel)){checks.Add(new {label,passed=true,pixel});return;}}while(timer.ElapsedMilliseconds<timeout);
        throw new Exception(label+" pixel mismatch: "+pixel);
    }
    [STAThread]static int Main(string[] args)
    {
        if(RecorderHost.IsHostRequest(args))return RecorderHost.Run(args);
        string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
        try
        {
            var fixture=new SceneCollection{CanvasWidth=640,CanvasHeight=360,SelectedSceneId="red",Scenes=new List<SceneDefinition>{Scene("red",ColorLayer("r",0xFFFF0000)),Scene("blue",ColorLayer("b",0xFF0000FF))}};
            string video;
            using(var client=new RecorderClient()){client.Start(Options(fixture,output));Thread.Sleep(1000);client.SwitchScene("blue");Thread.Sleep(1000);video=client.OutputPath;client.Stop();}
            string red=Path.Combine(output,"red.png"),blue=Path.Combine(output,"blue.png");
            foreach(var item in new[]{(red,Color.Red),(blue,Color.Blue)})using(var b=new Bitmap(640,360)){using(var g=Graphics.FromImage(b))g.Clear(item.Item2);b.Save(item.Item1);}
            var media=new SceneSource{Id="media",Name="Video",Kind=SceneSourceKind.Media,FilePath=video,MediaLoop=true,Transform=new SourceTransform{Width=640,Height=360}};
            var slides=new SceneSource{Id="slides",Name="Slides",Kind=SceneSourceKind.Slideshow,SlideFiles=new List<string>{red,blue},SlideSeconds=1,Transform=new SourceTransform{Width=640,Height=360}};
            var key=ColorLayer("green",0xFF00FF00);key.ChromaKey=true;
            var gray=ColorLayer("corrected",0xFFFF0000);gray.ColorCorrection=true;gray.Saturation=-1;
            var nested=new SceneSource{Id="group",Name="Nested red",Kind=SceneSourceKind.Scene,NestedSceneId="red",Transform=new SourceTransform{Width=640,Height=360}};
            var collection=new SceneCollection{CanvasWidth=640,CanvasHeight=360,SelectedSceneId="nested",Scenes=new List<SceneDefinition>{Scene("nested",nested),Scene("video",media),Scene("slides",slides),Scene("key",ColorLayer("bluebase",0xFF0000FF),key),Scene("gray",gray),fixture.Scenes[0],fixture.Scenes[1]}};
            var cycle=collection.Clone();cycle.Scenes.First(s=>s.Id=="red").Sources.Add(new SceneSource{Id="cycle",Name="Cycle",Kind=SceneSourceKind.Scene,NestedSceneId="nested",Transform=new SourceTransform{Width=640,Height=360}});
            bool rejected=false;try{RecorderEngine.ValidateSceneCollectionDefinition(cycle,false);}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Recursive scene accepted.");checks.Add(new{label="Recursive scenes rejected",passed=true});
            using(var client=new RecorderClient())
            {
                client.Start(Options(collection,output));Expect(client,"Nested scene",p=>p.R>200&&p.B<40);
                foreach(var transition in new[]{SceneTransition.Swipe,SceneTransition.Slide,SceneTransition.FadeToBlack}){client.SwitchScene("blue",transition,300);Expect(client,transition+" blue",p=>p.B>200&&p.R<40);client.SwitchScene("red",transition,300);Expect(client,transition+" red",p=>p.R>200&&p.B<40);}
                client.SwitchScene("video");Expect(client,"Local media red",p=>p.R>180&&p.B<60);Expect(client,"Local media blue",p=>p.B>180&&p.R<60);
                client.SwitchScene("slides");Expect(client,"Slideshow red",p=>p.R>200&&p.B<40);Expect(client,"Slideshow blue",p=>p.B>200&&p.R<40);
                client.SwitchScene("key");Expect(client,"Green screen transparency",p=>p.B>180&&p.G<60);
                client.SwitchScene("gray");Expect(client,"Color correction saturation",p=>Math.Abs(p.R-p.G)<12&&Math.Abs(p.G-p.B)<12&&p.R>30);
                string recording=client.OutputPath;client.Stop();checks.Add(new{label="Recording finalized",passed=File.Exists(recording)&&new FileInfo(recording).Length>1000,recording});
                File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{passed=true,checks},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
            }
            Console.WriteLine("Feature expansion passed: "+output);return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Console.Error.WriteLine(e);return 1;}
    }
}
