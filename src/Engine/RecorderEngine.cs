using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Rilmo.Engine
{
    public sealed partial class RecorderOptions
    {
        public int MonitorIndex { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Fps { get; set; }
        public bool SystemAudio { get; set; }
        public bool Microphone { get; set; }
        public bool CaptureCursor { get; set; }
        public string OutputDirectory { get; set; }
        public string EncoderPreference { get; set; }
        public IntPtr WindowHandle { get; set; }
        public int CropX { get; set; }
        public int CropY { get; set; }
        public int CropWidth { get; set; }
        public int CropHeight { get; set; }
        public string MicrophoneDeviceId { get; set; }
        public string SystemAudioDeviceId { get; set; }
        public RecorderOptions()
        {
            Width = 1280; Height = 720; Fps = 30; SystemAudio = true; CaptureCursor = true;
            EncoderPreference = "auto";
            MicrophoneDeviceId = "default"; SystemAudioDeviceId = "default";
            OutputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Rilmo Recorder");
        }
    }

    public sealed class EngineStats
    {
        public int TotalFrames { get; set; }
        public int DroppedFrames { get; set; }
        public int EncoderSkippedFrames { get; set; }
        public double ActiveFps { get; set; }
        public double RenderMilliseconds { get; set; }
        public long OutputBytes { get; set; }
    }

    /// <summary>
    /// Owns a minimal libobs graph. Call all methods on one dedicated worker thread.
    /// Construction and availability checks never load the OBS runtime. Start initializes
    /// D3D11 and only capture/encoder modules; Stop finalizes the muxer and destroys the
    /// entire graph, including desktop duplication, audio threads and GPU resources.
    /// Preview is explicit. Browser sources are optional and the product facade
    /// isolates their CEF lifetime in a dedicated host; the OBS UI is never started.
    /// </summary>
    public sealed partial class RecorderEngine : IDisposable
    {
        private static readonly object InstanceGate = new object();
        private static RecorderEngine activeInstance;
        private static bool dllSearchConfigured;
        private readonly object logGate = new object();
        private readonly NativeObs.LogHandler nativeLogger;
        private StreamWriter logWriter;
        private bool initialized, disposed, comInitialized, preferWindowsCapture, loggerInstalled;
        private int ownerThread;
        private IntPtr capture, scene, captureItem, desktopAudio, microphone, output, videoEncoder, audioEncoder, recordedWindow;
        private readonly IntPtr[] separateAudioEncoders=new IntPtr[2];
        private EngineStats finalStats = new EngineStats();
        private uint laggedAtStart, skippedAtStart;

        public string EncoderName { get; private set; }
        public string EncoderId { get; private set; }
        public bool IsRecording { get; private set; }
        public bool IsPaused { get; private set; }
        public string OutputPath { get; private set; }
        public string LastError { get; private set; }
        public string LogPath { get; private set; }
        public static string RuntimePath { get { return RuntimeLocator.ResolveRoot(); } }
        public static bool RuntimeAvailable { get { return RuntimeLocator.IsAvailable; } }
        public static CaptureWindow[] GetCaptureWindows() { return WindowCatalog.Enumerate(); }

        public RecorderEngine() { nativeLogger = OnNativeLog; EncoderName = "Automatic"; LastError = String.Empty; }

        public void Start(RecorderOptions options)
        {
            StartSession(options);
            try { StartRecording(); }
            catch { ReleaseGraph(); throw; }
        }

        public void StartSession(RecorderOptions options)
        {
            CheckThread();
            if (options == null) throw new ArgumentNullException("options");
            if (initialized) throw new InvalidOperationException("A capture session is already running.");
            ValidateEncodingOptions(options);
            if (options.Fps < 10 || options.Fps > 120) throw new ArgumentException("Choose a frame rate between 10 and 120.");
            if (options.Width < 320 || options.Height < 180 || options.Width > 7680 || options.Height > 4320 || options.Width % 4 != 0 || options.Height % 2 != 0)
                throw new ArgumentException("Recording dimensions must be between 320 x 180 and 7680 x 4320, with width divisible by four and even height.");
            if (options.CropX < 0 || options.CropY < 0 || options.CropWidth < 0 || options.CropHeight < 0 || (options.CropWidth == 0) != (options.CropHeight == 0))
                throw new ArgumentException("Choose a valid capture region with both width and height, or leave both dimensions at zero.");
            CaptureWindow window = null;
            if (options.SceneCollection == null && options.WindowHandle != IntPtr.Zero)
            {
                window = WindowCatalog.Describe(options.WindowHandle);
                if (window == null) throw new ArgumentException("Restore the selected window before recording, or select an available window.");
                int matches = 0;
                foreach (CaptureWindow candidate in GetCaptureWindows()) if (String.Equals(candidate.Title, window.Title, StringComparison.OrdinalIgnoreCase)) matches++;
                if (matches != 1) throw new ArgumentException("Multiple windows have the same title. Give the selected window a unique title or use display capture.");
            }
            Screen[] screens = Screen.AllScreens;
            if (options.SceneCollection == null && (options.MonitorIndex < 0 || options.MonitorIndex >= screens.Length)) throw new ArgumentException("The selected display is no longer connected.");
            if (options.SceneCollection != null) ValidateSceneCollection(options.SceneCollection);
            sessionOptions = CloneOptions(options);
            OutputPath = null;
            LastError = String.Empty; finalStats = new EngineStats();
            lock (InstanceGate)
            {
                if (activeInstance != null && activeInstance != this) throw new InvalidOperationException("Another recorder owns the capture engine.");
                activeInstance = this;
            }
            try
            {
                Initialize(options, options.SceneCollection == null ? screens[options.MonitorIndex] : screens[0]);
                if(options.SceneCollection != null) SetSceneCollection(options.SceneCollection);
                else
                {
                recordedWindow = options.WindowHandle;
                capture = window == null ? CreateDisplaySource(options, screens[options.MonitorIndex], preferWindowsCapture ? 2 : 0) : CreateWindowSource(options, window);
                NativeObs.obs_set_output_source(0, capture);

                // Desktop duplication opens asynchronously on the graphics thread.
                Stopwatch warmup = Stopwatch.StartNew();
                while (NativeObs.obs_source_get_width(capture) == 0 && warmup.ElapsedMilliseconds < 1200) Thread.Sleep(40);
                if (NativeObs.obs_source_get_width(capture) == 0 && !preferWindowsCapture && window == null)
                {
                    Log("Automatic display capture did not initialize; trying Windows Graphics Capture.");
                    NativeObs.obs_set_output_source(0, IntPtr.Zero);
                    NativeObs.obs_source_release(capture); capture = IntPtr.Zero;
                    capture = CreateDisplaySource(options, screens[options.MonitorIndex], 2);
                    NativeObs.obs_set_output_source(0, capture);
                    warmup.Restart();
                    while (NativeObs.obs_source_get_width(capture) == 0 && warmup.ElapsedMilliseconds < 5000) Thread.Sleep(40);
                    if (NativeObs.obs_source_get_width(capture) != 0) preferWindowsCapture = true;
                }
                if (NativeObs.obs_source_get_width(capture) == 0) throw new InvalidOperationException("The capture could not start. Unlock Windows and ensure the selected display or window is available. Protected content cannot be captured.");
                ComposeSource(options);
                }
                if (options.SystemAudio) desktopAudio = CreateAudioSource("wasapi_output_capture", "System audio", 1, options.SystemAudioDeviceId);
                if (options.Microphone) microphone = CreateAudioSource("wasapi_input_capture", "Microphone", 2, options.MicrophoneDeviceId);
                InitializeCompositionAndAudio(options);
                audioEncoder = CreateAudioEncoder(options.AudioBitrateKbps);
                IsSessionActive = true;
            }
            catch (Exception e)
            {
                LastError = e.Message; Log("START FAILED: " + e);
                ReleaseGraph();
                try { if (File.Exists(OutputPath) && new FileInfo(OutputPath).Length == 0) File.Delete(OutputPath); } catch (IOException) { }
                throw;
            }
        }

        private void Initialize(RecorderOptions options, Screen screen)
        {
            if (IntPtr.Size != 8) throw new InvalidOperationException("The recorder requires a 64-bit process.");
            string processPath = Process.GetCurrentProcess().MainModule.FileName;
            if (String.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Launch Rilmo Recorder.exe instead of running its DLL with dotnet. The recording helpers must be beside the application executable.");
            string root = RuntimePath;
            if (!RuntimeAvailable) throw new FileNotFoundException("The bundled recording runtime is missing or incomplete. Reinstall Rilmo Recorder.");
            string bin = RuntimeLocator.ResolveBinDirectory();
            if (!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "obs-ffmpeg-mux.exe")))
                throw new FileNotFoundException("The recording helper is missing beside Rilmo Recorder. Rebuild or repair the application.");
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rilmo Recorder", "Logs");
            Directory.CreateDirectory(logDir);
            LogPath = Path.Combine(logDir, "engine-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss-fff") + ".log");
            logWriter = new StreamWriter(LogPath, false, new UTF8Encoding(false)); logWriter.AutoFlush = true;
            lock (InstanceGate)
            {
                if (!dllSearchConfigured)
                {
                    if (!NativeObs.SetDefaultDllDirectories(0x1000)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (NativeObs.AddDllDirectory(bin) == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (NativeObs.AddDllDirectory(Path.Combine(root, "obs-plugins", "64bit")) == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (NativeObs.LoadLibraryEx(Path.Combine(bin, "obs.dll"), IntPtr.Zero, 0x1100) == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "The OBS capture runtime could not be loaded.");
                    dllSearchConfigured = true;
                }
            }
            NativeObs.base_set_log_handler(nativeLogger, IntPtr.Zero);
            loggerInstalled = true;
            int com = NativeObs.CoInitializeEx(IntPtr.Zero, 0); comInitialized = com >= 0;
            string version = NativeObs.Text(NativeObs.obs_get_version_string());
            if (!version.StartsWith("32.2.")) throw new InvalidOperationException("This development build supports OBS Studio 32.2.x; found " + version + ".");
            string config = RecorderHost.ConfigurationDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rilmo Recorder", "Runtime");
            Directory.CreateDirectory(config);
            if (!NativeObs.obs_startup("en-US", config, IntPtr.Zero)) throw new InvalidOperationException("The capture engine could not initialize.");
            initialized = true;
            NativeObs.obs_add_data_path(Path.Combine(root, "data", "libobs").Replace('\\', '/') + "/");
            NativeObs.AudioInfo audio = new NativeObs.AudioInfo { SampleRate = 48000, Speakers = 2 };
            if (!NativeObs.obs_reset_audio(ref audio)) throw new InvalidOperationException("The audio engine could not initialize.");
            NativeObs.VideoInfo video = new NativeObs.VideoInfo
            {
                GraphicsModule = Path.Combine(bin, "libobs-d3d11.dll"), FpsNum = (uint)options.Fps, FpsDen = 1,
                BaseWidth = (uint)options.Width, BaseHeight = (uint)options.Height,
                OutputWidth = (uint)options.Width, OutputHeight = (uint)options.Height,
                Format = 2, Adapter = 0, GpuConversion = true, ColorSpace = 2, Range = 1, ScaleType = 3
            };
            int result = NativeObs.obs_reset_video(ref video);
            if (result != 0) throw new InvalidOperationException("Direct3D 11 video initialization failed (" + result + "). Update your graphics driver.");
            LoadModule("win-capture", true);
            LoadModule("win-wasapi", true);
            LoadModule("obs-ffmpeg", true);
            LoadModule("obs-x264", true);
            // GPU modules perform vendor capability checks and only register supported encoders.
            // AMF is part of obs-ffmpeg, and uses the GPU texture without CPU readback.
            LoadModule("obs-nvenc", false);
            LoadModule("obs-qsv11", false);
            NativeObs.obs_post_load_modules();
        }

        private void LoadModule(string name, bool required)
        {
            if(featureModules.Contains(name))return;
            IntPtr module;
            int result = NativeObs.obs_open_module(out module, Path.Combine(RuntimePath, "obs-plugins", "64bit", name + ".dll").Replace('\\', '/'), Path.Combine(RuntimePath, "data", "obs-plugins", name).Replace('\\', '/'));
            bool loaded = result == 0 && NativeObs.obs_init_module(module);
            if(loaded)featureModules.Add(name);
            Log("Module " + name + ": " + (loaded ? "loaded" : "unavailable (" + result + ")"));
            if (!loaded && required) throw new InvalidOperationException("Required capture module could not load: " + name + ".");
        }

        private IntPtr CreateDisplaySource(RecorderOptions options, Screen screen, int method)
        {
            string monitor = screen.DeviceName;
            NativeObs.DisplayDevice device = new NativeObs.DisplayDevice(); device.Size = (uint)Marshal.SizeOf(typeof(NativeObs.DisplayDevice));
            if (NativeObs.EnumDisplayDevices(screen.DeviceName, 0, ref device, 1) && !String.IsNullOrEmpty(device.DeviceId)) monitor = device.DeviceId;
            IntPtr properties = NativeObs.obs_get_source_properties("monitor_capture");
            if (properties != IntPtr.Zero)
            {
                try
                {
                    IntPtr list = NativeObs.obs_properties_get(properties, "monitor_id");
                    if (list != IntPtr.Zero)
                    {
                        bool found = false; ulong count = NativeObs.obs_property_list_item_count(list).ToUInt64();
                        for (ulong i = 0; i < count; i++)
                        {
                            string id = NativeObs.Text(NativeObs.obs_property_list_item_string(list, new UIntPtr(i)));
                            Log("Capture display: " + NativeObs.Text(NativeObs.obs_property_list_item_name(list, new UIntPtr(i))) + " [" + id + "]");
                            if (String.Equals(id, monitor, StringComparison.OrdinalIgnoreCase)) found = true;
                        }
                        if (!found) throw new InvalidOperationException("The selected display could not be matched to a capture device.");
                    }
                }
                finally { NativeObs.obs_properties_destroy(properties); }
            }
            IntPtr settings = NativeObs.obs_data_create();
            try
            {
                NativeObs.obs_data_set_string(settings, "monitor_id", monitor);
                NativeObs.obs_data_set_int(settings, "method", method); // Auto first; WGC fallback for incompatible duplication drivers.
                NativeObs.obs_data_set_bool(settings, "capture_cursor", options.CaptureCursor);
                NativeObs.obs_data_set_bool(settings, "force_sdr", true);
                IntPtr source = NativeObs.obs_source_create("monitor_capture", "Display", settings, IntPtr.Zero);
                if (source == IntPtr.Zero) throw new InvalidOperationException("The display capture source could not be created.");
                return source;
            }
            finally { NativeObs.obs_data_release(settings); }
        }

        private IntPtr CreateWindowSource(RecorderOptions options, CaptureWindow window)
        {
            if (NativeObs.ms_find_window(1, 1, window.ClassName, window.Title, window.AppName) != window.Handle)
                throw new InvalidOperationException("The selected window cannot be matched uniquely by the capture engine. Select another window or use display capture.");
            IntPtr settings = NativeObs.obs_data_create();
            try
            {
                NativeObs.obs_data_set_string(settings, "window", window.Selector);
                NativeObs.obs_data_set_int(settings, "priority", 1); // Require a matching title; do not silently switch to another app window.
                NativeObs.obs_data_set_int(settings, "method", 2); // WGC handles accelerated windows and occlusion.
                NativeObs.obs_data_set_bool(settings, "cursor", options.CaptureCursor);
                NativeObs.obs_data_set_bool(settings, "client_area", true);
                NativeObs.obs_data_set_bool(settings, "capture_audio", false);
                NativeObs.obs_data_set_bool(settings, "force_sdr", true);
                IntPtr source = NativeObs.obs_source_create("window_capture", "Window", settings, IntPtr.Zero);
                if (source == IntPtr.Zero) throw new InvalidOperationException("The selected window could not be captured.");
                return source;
            }
            finally { NativeObs.obs_data_release(settings); }
        }

        private void ComposeSource(RecorderOptions options)
        {
            int width = (int)NativeObs.obs_source_get_width(capture), height = (int)NativeObs.obs_source_get_height(capture);
            if (options.CropWidth > 0 && ((long)options.CropX + options.CropWidth > width || (long)options.CropY + options.CropHeight > height))
                throw new ArgumentException("The capture region extends beyond the selected source (" + width + " x " + height + ").");
            scene = NativeObs.obs_scene_create_private("Recording canvas");
            if (scene == IntPtr.Zero) throw new InvalidOperationException("The recording canvas could not be created.");
            IntPtr item = NativeObs.obs_scene_add(scene, capture);
            captureItem = item;
            if (item == IntPtr.Zero) throw new InvalidOperationException("The capture source could not be added to the canvas.");
            if (options.CropWidth > 0)
            {
                NativeObs.SceneCrop crop = new NativeObs.SceneCrop { Left = options.CropX, Top = options.CropY,
                    Right = width - options.CropX - options.CropWidth, Bottom = height - options.CropY - options.CropHeight };
                NativeObs.obs_sceneitem_set_crop(item, ref crop);
            }
            NativeObs.Vec2 bounds = new NativeObs.Vec2 { X = options.Width, Y = options.Height };
            NativeObs.obs_sceneitem_set_alignment(item, 5); // Item origin: top left.
            NativeObs.obs_sceneitem_set_bounds_alignment(item, 0); // Content: centered in its bounds.
            NativeObs.obs_sceneitem_set_bounds_type(item, 2); // Scale to fit, retaining the source aspect ratio.
            NativeObs.obs_sceneitem_set_bounds(item, ref bounds);
            NativeObs.obs_sceneitem_set_scale_filter(item, 3); // Bilinear GPU sampling favors inexpensive capture.
            NativeObs.obs_set_output_source(0, NativeObs.obs_scene_get_source(scene));
            previewAfterVideoTime=NativeObs.obs_get_video_frame_time();
        }

        private IntPtr CreateAudioSource(string type, string name, uint channel, string deviceId)
        {
            deviceId = String.IsNullOrWhiteSpace(deviceId) ? "default" : deviceId;
            IntPtr properties = NativeObs.obs_get_source_properties(type);
            bool available = false;
            if (properties != IntPtr.Zero)
            {
                try
                {
                    IntPtr list = NativeObs.obs_properties_get(properties, "device_id");
                    if (list != IntPtr.Zero)
                    {
                        ulong count = NativeObs.obs_property_list_item_count(list).ToUInt64();
                        for (ulong i = 0; i < count; i++)
                            if (String.Equals(NativeObs.Text(NativeObs.obs_property_list_item_string(list, new UIntPtr(i))), deviceId, StringComparison.OrdinalIgnoreCase)) available = true;
                    }
                }
                finally { NativeObs.obs_properties_destroy(properties); }
            }
            if (!available) throw new InvalidOperationException(name + " device is unavailable. Connect it or turn that audio input off before recording.");
            IntPtr settings = NativeObs.obs_data_create();
            try
            {
                NativeObs.obs_data_set_string(settings, "device_id", deviceId);
                NativeObs.obs_data_set_bool(settings, "use_device_timing", false);
                IntPtr source = NativeObs.obs_source_create(type, name, settings, IntPtr.Zero);
                if (source == IntPtr.Zero) throw new InvalidOperationException(name + " could not be initialized.");
                NativeObs.obs_source_set_audio_mixers(source, sessionOptions.SeparateAudioTracks?(channel==1?3u:5u):1u);
                NativeObs.obs_set_output_source(channel, source);
                return source;
            }
            finally { NativeObs.obs_data_release(settings); }
        }

        private IntPtr CreateAudioEncoder(int bitrate,int mixer=0)
        {
            IntPtr settings = NativeObs.obs_data_create();
            try
            {
                NativeObs.obs_data_set_int(settings, "bitrate", bitrate);
                IntPtr encoder = NativeObs.obs_audio_encoder_create("ffmpeg_aac", "AAC "+mixer, settings, new UIntPtr((uint)mixer), IntPtr.Zero);
                if (encoder == IntPtr.Zero) throw new InvalidOperationException("The AAC audio encoder is unavailable.");
                NativeObs.obs_encoder_set_audio(encoder, NativeObs.obs_get_audio());
                return encoder;
            }
            finally { NativeObs.obs_data_release(settings); }
        }

        private HashSet<string> GetAvailableEncoders()
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal); IntPtr id;
            for (uint i = 0; NativeObs.obs_enum_encoder_types(new UIntPtr(i), out id); i++) ids.Add(NativeObs.Text(id));
            Log("Registered encoders: " + String.Join(", ", new List<string>(ids).ToArray())); return ids;
        }

        private static string[] GetCandidates(string preference)
        {
            if (String.Equals(preference, "software", StringComparison.OrdinalIgnoreCase) || preference == "obs_x264") return new[] { "obs_x264" };
            List<string> result = new List<string>();
            if (!String.IsNullOrEmpty(preference) && !String.Equals(preference, "auto", StringComparison.OrdinalIgnoreCase)) result.Add(preference);
            result.AddRange(new[] { "h264_texture_amf", "obs_nvenc_h264_tex", "obs_qsv11_v2", "obs_qsv11", "obs_x264" });
            return result.ToArray();
        }

        private bool TryStartOutput(string encoderId, RecorderOptions options, List<string> failures)
        {
            bool shared = videoEncoder != IntPtr.Zero;
            if (!shared) videoEncoder = CreateVideoEncoder(encoderId, options.VideoBitrateKbps, "Recording video");
            if (videoEncoder == IntPtr.Zero) { failures.Add(encoderId + ": creation failed"); return false; }
            IntPtr settings = NativeObs.obs_data_create();
            try
            {
                NativeObs.obs_data_set_string(settings, "path", OutputPath);
                NativeObs.obs_data_set_string(settings, "muxer_settings", "");
                output = NativeObs.obs_output_create("ffmpeg_muxer", "Recording", settings, IntPtr.Zero);
            }
            finally { NativeObs.obs_data_release(settings); }
            if (output != IntPtr.Zero)
            {
                NativeObs.obs_output_set_video_encoder(output, videoEncoder);
                NativeObs.obs_output_set_audio_encoder(output, audioEncoder, UIntPtr.Zero);
                if(sessionOptions.SeparateAudioTracks)for(int i=0;i<2;i++)NativeObs.obs_output_set_audio_encoder(output,separateAudioEncoders[i],new UIntPtr((uint)(i+1)));
                laggedAtStart = NativeObs.obs_get_lagged_frames();
                skippedAtStart = NativeObs.video_output_get_skipped_frames(NativeObs.obs_get_video());
                if (NativeObs.obs_output_start(output))
                {
                    // A new output on a running replay encoder receives nothing
                    // until the next keyframe. Report Recording only after video
                    // actually reaches the muxer, so an immediate Stop is valid.
                    Stopwatch ready = Stopwatch.StartNew();
                    while (NativeObs.obs_output_active(output) && ready.ElapsedMilliseconds < 5000)
                    {
                        if (NativeObs.obs_output_get_total_frames(output) >= 2 && NativeObs.obs_output_get_total_bytes(output) > 0)
                        {
                            Log("Recording video ready after " + ready.ElapsedMilliseconds + " ms.");
                            return true;
                        }
                        Thread.Sleep(20);
                    }
                    if (NativeObs.obs_output_active(output))
                    {
                        failures.Add(encoderId + ": no encoded video arrived within five seconds");
                        Log("Recording startup failed: " + failures[failures.Count - 1]);
                        // Release only this new file output; an existing replay
                        // keeps its encoder and capture session. Preserve a native
                        // stop timeout for the caller's normal cleanup/retry path.
                        try { StopRecording(); }
                        catch (IOException cleanupError) { Log("Unstarted recording cleanup: " + cleanupError.Message); }
                        return false;
                    }
                }
                string error = NativeObs.Text(NativeObs.obs_output_get_last_error(output));
                failures.Add(encoderId + ": " + (error.Length == 0 ? "encoder or muxer could not start" : error));
                Log("Encoder fallback: " + failures[failures.Count - 1]);
                NativeObs.obs_output_release(output); output = IntPtr.Zero;
            }
            if (!shared) { NativeObs.obs_encoder_release(videoEncoder); videoEncoder = IntPtr.Zero; }
            return false;
        }

        public void SetPaused(bool paused)
        {
            CheckThread();
            if (!IsRecording || output == IntPtr.Zero) throw new InvalidOperationException("No recording is running.");
            if (IsStreaming || IsReplayBufferActive) throw new InvalidOperationException("Stop streaming and replay before pausing. Their encoders share live capture timestamps.");
            if (!NativeObs.obs_output_can_pause(output) || !NativeObs.obs_output_pause(output, paused)) throw new InvalidOperationException("The recorder could not " + (paused ? "pause" : "resume") + ".");
            IsPaused = NativeObs.obs_output_paused(output); Log(IsPaused ? "Paused." : "Resumed.");
        }

        public EngineStats GetStats()
        {
            CheckThread();
            if(!stoppingSession && IsSessionActive)
            {
                string unavailable=GetUnavailableProgramWindow();
                if(unavailable!=null)
                {
                    Stop();LastError="The selected scene window was closed or minimized ("+unavailable+"). Active outputs have been stopped.";
                    throw new InvalidOperationException(LastError);
                }
            }
            if (!stoppingSession && IsSessionActive && recordedWindow != IntPtr.Zero && (!WindowCatalog.IsWindow(recordedWindow) || WindowCatalog.IsIconic(recordedWindow)))
            {
                Stop(); LastError = "The selected window was closed or minimized. Active outputs have been stopped.";
                throw new InvalidOperationException(LastError);
            }
            if (output == IntPtr.Zero) return finalStats;
            if (IsRecording && !NativeObs.obs_output_active(output))
            {
                LastError = NativeObs.Text(NativeObs.obs_output_get_last_error(output));
                if (String.IsNullOrEmpty(LastError)) LastError = "The recording stopped unexpectedly. Check available disk space and the engine log.";
                IsRecording = false; IsPaused = false;
                throw new InvalidOperationException(LastError);
            }
            finalStats = new EngineStats
            {
                TotalFrames = NativeObs.obs_output_get_total_frames(output),
                DroppedFrames = NativeObs.obs_output_get_frames_dropped(output) + CounterDelta(NativeObs.obs_get_lagged_frames(), laggedAtStart),
                EncoderSkippedFrames = CounterDelta(NativeObs.video_output_get_skipped_frames(NativeObs.obs_get_video()), skippedAtStart),
                ActiveFps = NativeObs.obs_get_active_fps(),
                RenderMilliseconds = NativeObs.obs_get_average_frame_time_ns() / 1000000.0,
                OutputBytes = (long)NativeObs.obs_output_get_total_bytes(output)
            };
            return finalStats;
        }

        public void Stop()
        {
            CheckThread(); stoppingSession = true;
            List<Exception> errors = new List<Exception>();
            try
            {
                try { StopVirtualCamera(); } catch (Exception ex) { errors.Add(ex); }
                try { StopStreaming(); } catch (Exception ex) { errors.Add(ex); }
                try { StopReplayBuffer(); } catch (Exception ex) { errors.Add(ex); }
                try { StopRecording(); } catch (Exception ex) { errors.Add(ex); }
                // Never destroy a graph still owned by a native active output. A retry
                // remains possible after an abnormal stop timeout.
                if (streamOutput == IntPtr.Zero && replayOutput == IntPtr.Zero && output == IntPtr.Zero && !IsVirtualCameraActive) ReleaseGraph();
                if (errors.Count != 0) throw new AggregateException("One or more outputs could not finish normally.", errors);
            }
            finally { stoppingSession = false; }
        }

        public void StopRecording()
        {
            CheckThread();
            if (output == IntPtr.Zero) return;
            if (IsRecording) { try { GetStats(); } catch (InvalidOperationException) { } }
            // GetStats may already have finalized a window that closed or minimized.
            // Preserve that completed file's counters and never query a released output.
            if (output == IntPtr.Zero) return;
            bool forced = false;
            if (NativeObs.obs_output_active(output))
            {
                if (IsPaused) NativeObs.obs_output_pause(output, false);
                NativeObs.obs_output_stop(output);
                Stopwatch wait = Stopwatch.StartNew();
                while (NativeObs.obs_output_active(output) && wait.ElapsedMilliseconds < 15000) Thread.Sleep(30);
                if (NativeObs.obs_output_active(output))
                {
                    forced = true; Log("Graceful stop timed out; requesting an immediate stop.");
                    NativeObs.obs_output_force_stop(output); wait.Restart();
                    while (NativeObs.obs_output_active(output) && wait.ElapsedMilliseconds < 5000) Thread.Sleep(30);
                }
                if (NativeObs.obs_output_active(output)) throw new TimeoutException("The recording is still finalizing. Keep Rilmo Recorder open and try Stop again.");
            }
            string error = NativeObs.Text(NativeObs.obs_output_get_last_error(output));
            finalStats.TotalFrames = NativeObs.obs_output_get_total_frames(output);
            finalStats.OutputBytes = (long)NativeObs.obs_output_get_total_bytes(output);
            finalStats.EncoderSkippedFrames = Math.Max(finalStats.EncoderSkippedFrames, CounterDelta(NativeObs.video_output_get_skipped_frames(NativeObs.obs_get_video()), skippedAtStart));
            IsRecording = false; IsPaused = false;
            Log("Stopped: frames=" + finalStats.TotalFrames + ", bytes=" + finalStats.OutputBytes + ", render=" + finalStats.RenderMilliseconds.ToString("F3") + " ms.");
            NativeObs.obs_output_release(output); output = IntPtr.Zero; // Join mux helper before reporting a saved file.
            ReleaseUnusedQualityEncoder();
            if (forced) throw new IOException("The recording required a forced stop. Its MKV may be recoverable, but completion was not confirmed.");
            if (!String.IsNullOrEmpty(error)) { LastError = error; throw new IOException(error); }
            if (finalStats.TotalFrames < 2 || finalStats.OutputBytes <= 0 || !File.Exists(OutputPath) || new FileInfo(OutputPath).Length == 0)
                throw new IOException("No encoded recording was finalized. See the engine log.");
        }

        private void ReleaseGraph()
        {
            if (streamOutput != IntPtr.Zero || replayOutput != IntPtr.Zero || IsVirtualCameraActive) throw new InvalidOperationException("Stop streaming, replay and virtual camera before releasing the capture session.");
            if (output != IntPtr.Zero) { NativeObs.obs_output_release(output); output = IntPtr.Zero; }
            if (videoEncoder != IntPtr.Zero) { NativeObs.obs_encoder_release(videoEncoder); videoEncoder = IntPtr.Zero; }
            if (audioEncoder != IntPtr.Zero) { NativeObs.obs_encoder_release(audioEncoder); audioEncoder = IntPtr.Zero; }
            for(int i=0;i<2;i++)if(separateAudioEncoders[i]!=IntPtr.Zero){NativeObs.obs_encoder_release(separateAudioEncoders[i]);separateAudioEncoders[i]=IntPtr.Zero;}
            if (initialized)
            {
                ReleaseCompositionAndAudio();
                for (uint i = 0; i < 3; i++) NativeObs.obs_set_output_source(i, IntPtr.Zero);
                ReleaseSceneCollection();
                if (scene != IntPtr.Zero) { NativeObs.obs_scene_release(scene); scene = IntPtr.Zero; }
                if (capture != IntPtr.Zero) { NativeObs.obs_source_release(capture); capture = IntPtr.Zero; }
                if (desktopAudio != IntPtr.Zero) { NativeObs.obs_source_release(desktopAudio); desktopAudio = IntPtr.Zero; }
                if (microphone != IntPtr.Zero) { NativeObs.obs_source_release(microphone); microphone = IntPtr.Zero; }
                NativeObs.obs_shutdown(); initialized = false;
            }
            if (comInitialized) { NativeObs.CoUninitialize(); comInitialized = false; }
            if (loggerInstalled) { NativeObs.base_set_log_handler(null, IntPtr.Zero); loggerInstalled = false; }
            lock (logGate) { if (logWriter != null) { logWriter.Dispose(); logWriter = null; } }
            lock (InstanceGate) { if (activeInstance == this) activeInstance = null; }
            IsRecording = false; IsPaused = false;
            recordedWindow = IntPtr.Zero; IsSessionActive = false; sessionOptions = null;
        }

        private static string FriendlyEncoderName(string id)
        {
            if (id.Contains("amf")) return "AMD H.264 (GPU)";
            if (id.Contains("nvenc")) return "NVIDIA H.264 (GPU)";
            if (id.Contains("qsv")) return "Intel H.264 (GPU)";
            return "x264 ultrafast (CPU)";
        }
        private static int CounterDelta(uint current, uint baseline) { return current >= baseline ? (int)Math.Min((long)Int32.MaxValue, (long)current - baseline) : 0; }
        private void OnNativeLog(int level, IntPtr format, IntPtr args, IntPtr param)
        {
            IntPtr buffer = IntPtr.Zero;
            try
            {
                buffer = Marshal.AllocHGlobal(8192); Marshal.WriteByte(buffer, 0);
                NativeObs.__stdio_common_vsprintf(0, buffer, new UIntPtr(8191), format, IntPtr.Zero, args);
                Marshal.WriteByte(buffer, 8191, 0);
                Log("[" + level + "] " + NativeObs.Text(buffer));
            }
            catch (Exception e) { try { Log("Native log formatting failed: " + e.Message + "; format=" + NativeObs.Text(format)); } catch { } }
            finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
        }
        private void Log(string message)
        {
            lock (logGate) { if (logWriter != null) logWriter.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + RedactSecrets(message)); }
        }
        private void CheckThread()
        {
            if (disposed) throw new ObjectDisposedException("RecorderEngine");
            int current = Thread.CurrentThread.ManagedThreadId;
            if (ownerThread == 0) ownerThread = current;
            else if (ownerThread != current) throw new InvalidOperationException("Call the capture engine on its dedicated worker thread.");
        }
        public void Dispose()
        {
            if (disposed) return;
            Stop(); disposed = true; GC.KeepAlive(nativeLogger);
        }
    }
}
