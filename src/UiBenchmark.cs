#if RILMO_TEST
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Rilmo.Engine;

namespace Rilmo
{
    public partial class MainWindow
    {
        internal string BenchmarkRequest;
        private sealed class BenchmarkInput{public long WindowHandle{get;set;}public string Directory{get;set;}public bool Software{get;set;}}
        private async Task RunMatchedBenchmark()
        {
            var input=JsonSerializer.Deserialize<BenchmarkInput>(File.ReadAllText(BenchmarkRequest));string directory=Path.GetFullPath(input.Directory);
            void Write(string name,object value){string temp=Path.Combine(directory,name+".tmp");File.WriteAllText(temp,JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,Path.Combine(directory,name),true);}
            async Task Wait(string name){var timeout=Stopwatch.StartNew();while(!File.Exists(Path.Combine(directory,name))){if(timeout.Elapsed.TotalSeconds>150)throw new TimeoutException("Benchmark controller timeout: "+name);await Task.Delay(100);}}
            try
            {
                Directory.CreateDirectory(directory);Motion.Enabled=false;
                settings.OutputDirectory=directory;FolderPath.Text=directory;
                CountdownToggle.IsChecked=false;MinimizeToggle.IsChecked=true;FloatingToggle.IsChecked=false;
                SystemAudioToggle.IsChecked=false;MicrophoneToggle.IsChecked=false;CursorToggle.IsChecked=false;QualityPicker.SelectedIndex=0;EncoderPicker.SelectedIndex=input.Software?1:0;
                settings.UseSceneCollection=false;captureMode="window";UpdateCaptureMode();WindowPicker.SelectedItem=WindowPicker.Items.Cast<CaptureWindow>().Single(w=>w.Handle.ToInt64()==input.WindowHandle);UpdateOptions();
                WindowState=WindowState.Minimized;Write("idle-ready.json",new{pid=Environment.ProcessId,width=BuildOptions().Width,height=BuildOptions().Height,fps=BuildOptions().Fps,scope="Actual WPF frontend; preview and floating controls disabled; real StartRecording/StopRecording paths."});
                await Wait("start.flag");var startup=Stopwatch.StartNew();await StartRecording();startup.Stop();
                if(!recording)throw new Exception(StatusLabel.Text);
                Write("recording-ready.json",new{encoder=recorder.EncoderId,path=recorder.OutputPath,log=recorder.LogPath,startupMs=startup.ElapsedMilliseconds});
                await Wait("stop.flag");var stats=await worker.Run(()=>recorder.GetStats());string output=recorder.OutputPath;await StopRecording();
                if(recording||cleanupPending)throw new Exception("Benchmark stop did not finalize.");
                Write("result.json",new{passed=true,output,stats,startupMs=startup.ElapsedMilliseconds});
            }
            catch(Exception ex){Write("failure.json",new{error=ex.ToString()});if(HasActiveSession)try{await worker.Run(()=>recorder.Stop());}catch{} }
            finally{closeAllowed=true;Close();}
        }
    }
}
#endif

