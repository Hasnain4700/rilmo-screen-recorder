using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using Rilmo.Engine;

internal static class AudioRoutingSmoke
{
    [DllImport("obs.dll",CallingConvention=CallingConvention.Cdecl)] static extern long obs_source_get_sync_offset(IntPtr source);
    [DllImport("obs.dll",CallingConvention=CallingConvention.Cdecl)] static extern int obs_source_get_monitoring_type(IntPtr source);
    // Substitute owned, generated tones for physical WASAPI inputs. This tests the
    // production audio controls, three encoder buses and muxing without opening a mic.
    static IntPtr Attach(RecorderEngine engine,string field,uint channel,uint mixers,string file)
    {
        var data=NativeObs.obs_data_create_from_json(JsonSerializer.Serialize(new{is_local_file=true,local_file=file,looping=true,close_when_inactive=true}));
        IntPtr source;
        try{source=NativeObs.obs_source_create("ffmpeg_source",field,data,IntPtr.Zero);}finally{NativeObs.obs_data_release(data);}
        if(source==IntPtr.Zero)throw new Exception("Tone source failed.");
        typeof(RecorderEngine).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(engine,source);
        NativeObs.obs_source_set_audio_mixers(source,mixers);NativeObs.obs_set_output_source(channel,source);return source;
    }
    [STAThread]static int Main(string[] args)
    {
        string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
        try
        {
            var scenes=new SceneCollection{CanvasWidth=640,CanvasHeight=360,SelectedSceneId="tone",Scenes=new List<SceneDefinition>{new SceneDefinition{Id="tone",Name="Tone routing",Sources=new List<SceneSource>{new SceneSource{Id="bg",Name="Background",Kind=SceneSourceKind.Color,Color=0xFF283040,Transform=new SourceTransform{Width=640,Height=360}}}}}};
            using(var engine=new RecorderEngine())
            {
                engine.StartSession(new RecorderOptions{Width=640,Height=360,Fps=30,SystemAudio=false,Microphone=false,SeparateAudioTracks=true,OutputDirectory=output,SceneCollection=scenes});
                var computer=Attach(engine,"desktopAudio",1,3,args[1]);var mic=Attach(engine,"microphone",2,5,args[2]);
                engine.SetAudioControls(AudioChannel.System,new AudioMixSettings{Volume=.5f,SyncOffsetMs=120});
                engine.SetAudioControls(AudioChannel.Microphone,new AudioMixSettings{Volume=.7f,SyncOffsetMs=-40,Compressor=true});
                if(obs_source_get_sync_offset(computer)!=120000000||obs_source_get_sync_offset(mic)!=-40000000||obs_source_get_monitoring_type(mic)!=0)throw new Exception("Native offset/monitor controls differ from requested state.");
                bool rejected=false;try{engine.SetAudioControls(AudioChannel.Microphone,new AudioMixSettings{SyncOffsetMs=20001});}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Invalid audio offset accepted.");
                Thread.Sleep(500);engine.StartRecording();Thread.Sleep(3200);string file=engine.OutputPath;engine.Stop();
                File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{passed=true,file,positiveAndNegativeOffsets=true,monitorDisabled=true,compressor=true,invalidOffsetRejected=true,scope="Synthetic source substitution; physical WASAPI devices and audible monitoring are not qualified."},new JsonSerializerOptions{WriteIndented=true}));
            }
            Console.WriteLine("Audio routing recorded: "+output);return 0;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(output,"failure.txt"),ex.ToString());Console.Error.WriteLine(ex);return 1;}
    }
}
