// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace Rilmo.Engine
{
    // Call operations through the existing single-threaded EngineWorker. Read-only
    // properties use the last response snapshot and never block the UI thread.
    public sealed class RecorderClient:IDisposable
    {
        private RecorderEngine direct;
        private NamedPipeServerStream pipe;
        private Process host;
        private string hostConfiguration;
        private RecorderState state=new RecorderState();
        private int ownerThread;
        private long sequence;
        private bool disposed;
        private bool recordingFinalizationLost;
        public string EncoderName=>state.EncoderName;public string EncoderId=>state.EncoderId;public string OutputPath=>state.OutputPath;public string LastError=>state.LastError;public string LogPath=>state.LogPath;public string SelectedSceneId=>state.SelectedSceneId;
        public bool IsSessionActive=>state.IsSessionActive;public bool IsRecording=>state.IsRecording;public bool IsPaused=>state.IsPaused;public bool IsStreaming=>state.IsStreaming;public bool IsReplayBufferActive=>state.IsReplayBufferActive;public bool IsVirtualCameraActive=>state.IsVirtualCameraActive;public ulong VirtualCameraFrames=>state.VirtualCameraFrames;
        public bool IsIsolated=>host!=null;
        public int? IsolatedProcessId=>host==null?(int?)null:host.Id;
        private void CheckThread(){if(disposed)throw new ObjectDisposedException(nameof(RecorderClient));int current=Environment.CurrentManagedThreadId;if(ownerThread==0)ownerThread=current;else if(ownerThread!=current)throw new InvalidOperationException("Use the recording worker thread for capture operations.");}
        private static bool UsesBrowser(SceneCollection collection)=>collection?.Scenes?.Any(scene=>scene?.Sources?.Any(source=>source?.Kind==SceneSourceKind.Browser)==true)==true;
        public void Start(RecorderOptions options){StartSession(options);try{StartRecording();}catch{Stop();throw;}}
        public void StartSession(RecorderOptions options)
        {
            CheckThread();if(options==null)throw new ArgumentNullException(nameof(options));if(IsSessionActive)throw new InvalidOperationException("A capture session is already running.");
            recordingFinalizationLost=false;
            if(host!=null)CloseHost();
            if(UsesBrowser(options.SceneCollection))
            {
                direct?.Dispose();direct=null;
                try{OpenHost();Call("StartSession",options);}catch{CloseHost();throw;}
            }
            else {if(direct==null)direct=new RecorderEngine();Call("StartSession",options);}
        }
        private void OpenHost()
        {
            string executable=Environment.ProcessPath;
            if(String.IsNullOrEmpty(executable) || String.Equals(Path.GetFileName(executable),"dotnet.exe",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Launch the Rilmo application executable to use browser scenes.");
            string name="rilmo-engine-"+Guid.NewGuid().ToString("N"),token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            hostConfiguration=RecorderHost.ConfigurationFor(name);
            pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly,65536,65536);
            var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=AppContext.BaseDirectory};
            start.ArgumentList.Add("--engine-host");start.ArgumentList.Add(name);start.ArgumentList.Add(token);start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            host=Process.Start(start)??throw new InvalidOperationException("The isolated capture process could not start.");
            using(var timeout=new CancellationTokenSource(15000))pipe.WaitForConnectionAsync(timeout.Token).GetAwaiter().GetResult();
            if(!RecorderWire.GetNamedPipeClientProcessId(pipe.SafePipeHandle,out uint childId) || childId!=host.Id)throw new IOException("The isolated capture process identity did not match.");
            RecorderWire.Write(pipe,new RecorderRequest{Id=++sequence,Method="Hello",Token=token},15000);
            RecorderResponse reply=RecorderWire.Read<RecorderResponse>(pipe,15000);
            if(reply==null || reply.Id!=sequence || reply.Error!=null)throw new IOException("The isolated capture process handshake failed.");
            state=reply.State??new RecorderState();
        }
        private object Invoke(string name,object[] arguments)
        {
            CheckThread();
            if(!RecorderWire.Methods.TryGetValue(name,out MethodInfo method))throw new ArgumentException("Unsupported capture operation.");
            if(host==null)
            {
                if(direct==null)direct=new RecorderEngine();
                try{return method.Invoke(direct,arguments);}catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException??error).Throw();throw;}
                finally{state=RecorderState.Read(direct);}
            }
            RecorderResponse response;
            try
            {
                var parameters=method.GetParameters();var values=new JsonElement[arguments.Length];
                for(int i=0;i<values.Length;i++)values[i]=JsonSerializer.SerializeToElement(arguments[i],parameters[i].ParameterType,RecorderWire.Json);
                RecorderWire.Write(pipe,new RecorderRequest{Id=++sequence,Method=name,Arguments=values},30000);
                response=RecorderWire.Read<RecorderResponse>(pipe,60000);
                if(response==null || response.Id!=sequence)throw new IOException("The isolated capture process disconnected.");
                if(response.State!=null)state=response.State;
            }
            catch(Exception error)when(error is IOException || error is OperationCanceledException || error is JsonException || error is InvalidDataException)
            {
                recordingFinalizationLost|=state.IsRecording || name=="StartRecording";
                CloseHost();state.LastError="The browser capture process stopped responding. Recording finalization was not confirmed; inspect the retained recording path or output folder.";
                throw new IOException(state.LastError,error);
            }
            if(response.Error!=null)
            {
                if(response.ErrorType==nameof(TimeoutException))throw new TimeoutException(response.Error);
                if(response.ErrorType==nameof(ArgumentException) || response.ErrorType==nameof(ArgumentOutOfRangeException))throw new ArgumentException(response.Error);
                if(response.ErrorType==nameof(FileNotFoundException))throw new FileNotFoundException(response.Error);
                throw new InvalidOperationException(response.Error);
            }
            return method.ReturnType==typeof(void)?null:response.Result?.Deserialize(method.ReturnType,RecorderWire.Json);
        }
        private void Call(string name,params object[] arguments)=>Invoke(name,arguments);
        private T Call<T>(string name,params object[] arguments)=>(T)Invoke(name,arguments);
        private void RejectBrowserOnDirect(SceneCollection collection)
        {if(host==null && UsesBrowser(collection))throw new InvalidOperationException("Stop capture before adding a browser source. Rilmo opens browser scenes in a fresh isolated capture session.");}
        public void StartRecording()=>Call("StartRecording");
        public void StopRecording(){CheckThread();if(recordingFinalizationLost)throw new IOException("Recording finalization was not confirmed after the capture process disconnected. Inspect the retained file; it is not confirmed saved.");if(host!=null || direct!=null)Call("StopRecording");}
        public void SetPaused(bool paused)=>Call("SetPaused",paused);
        public EngineStats GetStats(){CheckThread();return host==null && direct==null?state.FinalStats??new EngineStats():Call<EngineStats>("GetStats");}
        public OutputStatus GetOutputStatus(){CheckThread();return host==null && direct==null?new OutputStatus():Call<OutputStatus>("GetOutputStatus");}public AudioLevels GetAudioLevels(){CheckThread();return host==null && direct==null?new AudioLevels():Call<AudioLevels>("GetAudioLevels");}
        public void StartStreaming(StreamOptions options)=>Call("StartStreaming",options);public void StopStreaming()=>Call("StopStreaming");
        public void StartReplayBuffer(ReplayOptions options)=>Call("StartReplayBuffer",options);public void StopReplayBuffer()=>Call("StopReplayBuffer");public string SaveReplay()=>Call<string>("SaveReplay");
        public void SetAudioControls(AudioChannel channel,AudioMixSettings settings)=>Call("SetAudioControls",channel,settings);
        public CaptureDevice[] GetCameraDevices()=>Call<CaptureDevice[]>("GetCameraDevices");public void AddOverlay(SceneOverlay overlay)=>Call("AddOverlay",overlay);public void UpdateOverlay(string id,SourceTransform transform)=>Call("UpdateOverlay",id,transform);public void RemoveOverlay(string id)=>Call("RemoveOverlay",id);public void UpdateCaptureTransform(SourceTransform transform)=>Call("UpdateCaptureTransform",transform);
        public SceneCollection GetSceneCollection()=>Call<SceneCollection>("GetSceneCollection");public void SetSceneCollection(SceneCollection collection){RejectBrowserOnDirect(collection);Call("SetSceneCollection",collection);}
        public void SwitchScene(string sceneId,SceneTransition transition=SceneTransition.Cut,int durationMs=300)=>Call("SwitchScene",sceneId,transition,durationMs);
        public void AddScene(SceneDefinition definition){if(host==null && definition?.Sources?.Any(source=>source.Kind==SceneSourceKind.Browser)==true)throw new InvalidOperationException("Stop capture before adding a browser source.");Call("AddScene",definition);}
        public void RemoveScene(string sceneId)=>Call("RemoveScene",sceneId);
        public void AddSceneSource(string sceneId,SceneSource source){if(host==null && source?.Kind==SceneSourceKind.Browser)throw new InvalidOperationException("Stop capture before adding a browser source.");Call("AddSceneSource",sceneId,source);}
        public void UpdateSceneSource(string sceneId,SceneSource source){if(host==null && source?.Kind==SceneSourceKind.Browser)throw new InvalidOperationException("Stop capture before adding a browser source.");Call("UpdateSceneSource",sceneId,source);}
        public void RemoveSceneSource(string sceneId,string sourceId)=>Call("RemoveSceneSource",sceneId,sourceId);public void MoveSceneSource(string sceneId,string sourceId,int index)=>Call("MoveSceneSource",sceneId,sourceId,index);
        public void SetSceneSourceTransform(string sceneId,string sourceId,SourceTransform transform)=>Call("SetSceneSourceTransform",sceneId,sourceId,transform);public void SetSceneSourceCrop(string sceneId,string sourceId,SourceCrop crop)=>Call("SetSceneSourceCrop",sceneId,sourceId,crop);
        public ScenePreviewFrame CapturePreview(int maxWidth=640,int maxHeight=360)=>Call<ScenePreviewFrame>("CapturePreview",maxWidth,maxHeight);
        public void StartVirtualCamera()=>Call("StartVirtualCamera");public void StopVirtualCamera()=>Call("StopVirtualCamera");
        public void Stop()
        {
            CheckThread();if(host==null){if(direct!=null)Call("Stop");return;}
            // Preserve a live host after an explicit Stop timeout so the user can
            // retry finalization instead of abruptly abandoning active outputs.
            Call("Stop");CloseHost();
        }
        private void CloseHost()
        {
            recordingFinalizationLost|=state.IsRecording;
            Process process=host;host=null;pipe?.Dispose();pipe=null;
            if(process!=null)
            {
                try {if(!process.WaitForExit(15000)){process.Kill(entireProcessTree:true);process.WaitForExit(5000);}}catch(InvalidOperationException){}finally{process.Dispose();}
            }
            RecorderHost.CleanupConfiguration(hostConfiguration);hostConfiguration=null;
            state.MarkStopped();
        }
        public void Dispose(){if(disposed)return;CheckThread();try{Stop();direct?.Dispose();direct=null;}finally{if(host!=null)CloseHost();disposed=true;}}
    }
}
