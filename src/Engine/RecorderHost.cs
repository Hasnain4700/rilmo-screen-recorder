// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Rilmo.Engine
{
    // A fresh process owns each browser session. CEF cannot safely restart after
    // CefShutdown in one process, even if its DLLs are explicitly unloaded.
    public static class RecorderHost
    {
        // Kept until process exit, not disposed when Run returns. The operating
        // system closes this non-inheritable handle if the host crashes and then
        // terminates its CEF descendants. Parent-pipe EOF still gets graceful Stop.
        private static SafeFileHandle processJob;
        internal static string ConfigurationDirectory {get;private set;}
        internal static string ConfigurationFor(string pipeName)=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Rilmo Recorder","RuntimeSessions",pipeName);
        internal static void CleanupConfiguration(string path)
        {
            if(String.IsNullOrEmpty(path))return;
            string root=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Rilmo Recorder","RuntimeSessions"));
            string full=Path.GetFullPath(path);
            if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("rilmo-engine-",StringComparison.Ordinal))return;
            try
            {
                if(!Directory.Exists(full))return;
                var parents=new DirectoryInfo(full);while(parents!=null){if((parents.Attributes&FileAttributes.ReparsePoint)!=0)return;parents=parents.Parent;}
                var queue=new Queue<DirectoryInfo>();queue.Enqueue(new DirectoryInfo(full));
                while(queue.Count>0)foreach(var item in queue.Dequeue().EnumerateFileSystemInfos()){if((item.Attributes&FileAttributes.ReparsePoint)!=0)return;if(item is DirectoryInfo directory)queue.Enqueue(directory);}
                Directory.Delete(full,true);
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
        public static bool IsHostRequest(string[] args) => args.Length>0 && args[0]=="--engine-host";
        public static int Run(string[] args)
        {
            if(args.Length!=4 || !args[1].StartsWith("rilmo-engine-",StringComparison.Ordinal) || args[1].Length!=45 ||
                args[2].Length!=64 || !args[2].All(Uri.IsHexDigit) || !int.TryParse(args[3],out int parentId)) return 2;
            int result=1;
            var worker=new Thread(()=>{try{result=RunCore(args[1],args[2],parentId);}catch{result=1;}finally{CleanupConfiguration(ConfigurationDirectory);}}){Name="Rilmo isolated capture",IsBackground=false};
            worker.SetApartmentState(ApartmentState.MTA);worker.Start();worker.Join();return result;
        }
        private static int RunCore(string name,string token,int parentId)
        {
            processJob=RecorderWire.CreateOwnedProcessJob();
            ConfigurationDirectory=ConfigurationFor(name);
            using(var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous))
            using(var engine=new RecorderEngine())
            {
                pipe.Connect(15000);
                if(!RecorderWire.GetNamedPipeServerProcessId(pipe.SafePipeHandle,out uint serverId) || serverId!=parentId) return 3;
                RecorderRequest hello=RecorderWire.Read<RecorderRequest>(pipe,15000);
                if(hello==null || hello.Method!="Hello" || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hello.Token??""),Encoding.ASCII.GetBytes(token)))return 3;
                RecorderWire.Write(pipe,new RecorderResponse {Id=hello.Id,State=RecorderState.Read(engine),Result=JsonSerializer.SerializeToElement(true)},15000);
                while(true)
                {
                    // No idle timeout: a pipe EOF is the parent-crash signal. Native
                    // output cleanup stays on this engine-owning thread in finally.
                    RecorderRequest request=RecorderWire.Read<RecorderRequest>(pipe,Timeout.Infinite);
                    if(request==null)return 0;
                    var response=new RecorderResponse {Id=request.Id};bool exit=request.Method=="Shutdown";
                    try
                    {
                        if(exit)engine.Stop();
                        else
                        {
                            if(!RecorderWire.Methods.TryGetValue(request.Method??"",out MethodInfo method))throw new ArgumentException("Unknown recording operation.");
                            var parameters=method.GetParameters();
                            if(request.Arguments==null || request.Arguments.Length!=parameters.Length)throw new ArgumentException("Invalid recording operation arguments.");
                            object[] values=new object[parameters.Length];
                            for(int i=0;i<values.Length;i++) values[i]=request.Arguments[i].Deserialize(parameters[i].ParameterType,RecorderWire.Json);
                            object value=method.Invoke(engine,values);
                            if(method.ReturnType!=typeof(void))response.Result=JsonSerializer.SerializeToElement(value,method.ReturnType,RecorderWire.Json);
                        }
                    }
                    catch(Exception error)
                    {
                        Exception cause=error is TargetInvocationException && error.InnerException!=null?error.InnerException:error;
                        response.ErrorType=cause.GetType().Name;
                        response.Error=(cause.Message??"Capture operation failed.");
                        if(response.Error.Length>2048)response.Error=response.Error.Substring(0,2048);
                    }
                    finally {response.State=RecorderState.Read(engine);}
                    RecorderWire.Write(pipe,response,30000);
                    if(exit && response.Error==null)return 0;
                }
            }
        }
    }

    internal sealed class RecorderRequest
    {
        public long Id {get;set;} public string Method {get;set;} public string Token {get;set;}
        public JsonElement[] Arguments {get;set;}
    }
    internal sealed class RecorderResponse
    {
        public long Id {get;set;} public JsonElement? Result {get;set;} public string Error {get;set;} public string ErrorType {get;set;} public RecorderState State {get;set;}
    }
    internal sealed class RecorderState
    {
        public string EncoderName {get;set;}="Automatic";public string EncoderId {get;set;}public string OutputPath {get;set;}public string LogPath {get;set;}public string LastError {get;set;}="";public string SelectedSceneId {get;set;}
        public bool IsSessionActive {get;set;}public bool IsRecording {get;set;}public bool IsPaused {get;set;}public bool IsStreaming {get;set;}public bool IsReplayBufferActive {get;set;}public bool IsVirtualCameraActive {get;set;}public ulong VirtualCameraFrames {get;set;}
        public EngineStats FinalStats {get;set;}
        internal static RecorderState Read(RecorderEngine engine) => new RecorderState {EncoderName=engine.EncoderName,EncoderId=engine.EncoderId,OutputPath=engine.OutputPath,LogPath=engine.LogPath,LastError=engine.LastError,SelectedSceneId=engine.SelectedSceneId,
            IsSessionActive=engine.IsSessionActive,IsRecording=engine.IsRecording,IsPaused=engine.IsPaused,IsStreaming=engine.IsStreaming,IsReplayBufferActive=engine.IsReplayBufferActive,IsVirtualCameraActive=engine.IsVirtualCameraActive,VirtualCameraFrames=engine.VirtualCameraFrames,FinalStats=engine.IsSessionActive?null:engine.GetStats()};
        internal void MarkStopped(){IsSessionActive=IsRecording=IsPaused=IsStreaming=IsReplayBufferActive=IsVirtualCameraActive=false;VirtualCameraFrames=0;}
    }
    internal static class RecorderWire
    {
        internal const int MaximumBytes=16*1024*1024;
        internal static readonly JsonSerializerOptions Json=CreateJson();
        internal static readonly IReadOnlyDictionary<string,MethodInfo> Methods=CreateMethods();
        private static Dictionary<string,MethodInfo> CreateMethods()
        {
            string[] names={"StartSession","StartRecording","StopRecording","Stop","SetPaused","GetStats","StartStreaming","StopStreaming","StartReplayBuffer","StopReplayBuffer","SaveReplay","GetOutputStatus","GetAudioLevels","SetAudioControls","GetCameraDevices","AddOverlay","UpdateOverlay","RemoveOverlay","UpdateCaptureTransform","GetSceneCollection","SetSceneCollection","SwitchScene","AddScene","RemoveScene","AddSceneSource","UpdateSceneSource","RemoveSceneSource","MoveSceneSource","SetSceneSourceTransform","SetSceneSourceCrop","CapturePreview","StartVirtualCamera","StopVirtualCamera"};
            var result=new Dictionary<string,MethodInfo>(StringComparer.Ordinal);
            foreach(string name in names)result.Add(name,typeof(RecorderEngine).GetMethod(name,BindingFlags.Public|BindingFlags.Instance)??throw new InvalidOperationException("Missing engine RPC method: "+name));
            return result;
        }
        private static JsonSerializerOptions CreateJson()
        {
            var resolver=new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(info=>{
                if(info.Type==typeof(SceneSource))
                {
                    // Saved scenes intentionally ignore transient HWNDs. This extra
                    // property exists only in authenticated local IPC serialization.
                    var property=info.CreateJsonPropertyInfo(typeof(IntPtr),"NativeWindowHandle");
                    property.Get=value=>((SceneSource)value).WindowHandle;
                    property.Set=(value,handle)=>((SceneSource)value).WindowHandle=(IntPtr)handle;
                    info.Properties.Add(property);
                }
            });
            var options=new JsonSerializerOptions {MaxDepth=40,TypeInfoResolver=resolver,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals};
            options.Converters.Add(new HandleConverter());return options;
        }
        private sealed class HandleConverter:JsonConverter<IntPtr>
        {
            public override IntPtr Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)=>new IntPtr(reader.GetInt64());
            public override void Write(Utf8JsonWriter writer,IntPtr value,JsonSerializerOptions options)=>writer.WriteNumberValue(value.ToInt64());
        }
        internal static T Read<T>(Stream stream,int timeout)where T:class
        {
            using(var cancel=new CancellationTokenSource())
            {
                if(timeout!=Timeout.Infinite)cancel.CancelAfter(timeout);
                byte[] length=new byte[4];int first=stream.ReadAsync(length.AsMemory(0,1),cancel.Token).AsTask().GetAwaiter().GetResult();if(first==0)return null;
                stream.ReadExactlyAsync(length.AsMemory(1,3),cancel.Token).AsTask().GetAwaiter().GetResult();
                int size=BitConverter.ToInt32(length,0);if(size<2 || size>MaximumBytes)throw new InvalidDataException("Capture IPC packet exceeded its limit.");
                byte[] data=new byte[size];stream.ReadExactlyAsync(data,cancel.Token).AsTask().GetAwaiter().GetResult();return JsonSerializer.Deserialize<T>(data,Json);
            }
        }
        internal static void Write<T>(Stream stream,T value,int timeout)
        {
            byte[] data=JsonSerializer.SerializeToUtf8Bytes(value,Json);if(data.Length>MaximumBytes)throw new InvalidDataException("Capture IPC response exceeded its limit.");
            using(var cancel=new CancellationTokenSource(timeout))
            {
                stream.WriteAsync(BitConverter.GetBytes(data.Length),cancel.Token).AsTask().GetAwaiter().GetResult();
                stream.WriteAsync(data,cancel.Token).AsTask().GetAwaiter().GetResult();stream.FlushAsync(cancel.Token).GetAwaiter().GetResult();
            }
        }
        [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint processId);
        [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint processId);
        [StructLayout(LayoutKind.Sequential)]private struct BasicLimits {internal long ProcessTime,JobTime;internal uint Flags;internal UIntPtr MinimumWorkingSet,MaximumWorkingSet;internal uint ActiveProcesses;internal UIntPtr Affinity;internal uint Priority,Scheduling;}
        [StructLayout(LayoutKind.Sequential)]private struct IoCounters {internal ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes;}
        [StructLayout(LayoutKind.Sequential)]private struct ExtendedLimits {internal BasicLimits Basic;internal IoCounters Io;internal UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;}
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern SafeFileHandle CreateJobObject(IntPtr attributes,string name);
        [DllImport("kernel32.dll",SetLastError=true)]private static extern bool SetInformationJobObject(SafeFileHandle job,int information,ref ExtendedLimits limits,uint length);
        [DllImport("kernel32.dll",SetLastError=true)]private static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
        internal static SafeFileHandle CreateOwnedProcessJob()
        {
            var job=CreateJobObject(IntPtr.Zero,null);
            if(job.IsInvalid)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Capture process cleanup could not be configured.");
            var limits=new ExtendedLimits {Basic=new BasicLimits {Flags=0x2000}};
            if(!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf<ExtendedLimits>()) || !AssignProcessToJobObject(job,new IntPtr(-1)))
            {int error=Marshal.GetLastWin32Error();job.Dispose();throw new System.ComponentModel.Win32Exception(error,"Capture process descendants could not be contained.");}
            return job;
        }
    }
}
