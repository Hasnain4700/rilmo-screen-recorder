using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace Rilmo.Engine
{
    public sealed partial class RecorderEngine
    {
        private RecorderOptions sessionOptions;
        private bool stoppingSession;
        private IntPtr streamOutput, streamService, streamVideoEncoder, streamAudioEncoder, replayOutput;
        private NativeObs.SignalCallback replaySavedCallback;
        private ManualResetEvent replaySaved;
        private ManualResetEvent streamStopped;
        private NativeObs.SignalCallback streamStoppedCallback;
        private bool streamConnecting;
        private string lastStreamError = String.Empty;
        private string[] streamingSecrets = new string[0];
        public bool IsSessionActive { get; private set; }
        public bool IsStreaming { get; private set; }
        public bool IsReplayBufferActive { get; private set; }

        private static void ValidateEncodingOptions(RecorderOptions options)
        {
            if (options.Quality < 14 || options.Quality > 35) throw new ArgumentException("Recording quality must be between 14 and 35.");
            if (options.VideoBitrateKbps != 0 && (options.VideoBitrateKbps < 500 || options.VideoBitrateKbps > 100000)) throw new ArgumentException("Video bitrate must be zero for quality mode, or 500–100000 Kbps.");
            if (options.AudioBitrateKbps < 64 || options.AudioBitrateKbps > 320) throw new ArgumentException("Audio bitrate must be 64–320 Kbps.");
            if (String.IsNullOrWhiteSpace(options.OutputDirectory)) throw new ArgumentException("Choose an output directory.");
        }
        private static RecorderOptions CloneOptions(RecorderOptions source)
        {
            return new RecorderOptions { MonitorIndex = source.MonitorIndex, Width = source.Width, Height = source.Height, Fps = source.Fps,
                SystemAudio = source.SystemAudio, Microphone = source.Microphone, CaptureCursor = source.CaptureCursor,
                OutputDirectory = Path.GetFullPath(source.OutputDirectory), EncoderPreference = source.EncoderPreference,
                Quality = source.Quality, VideoBitrateKbps = source.VideoBitrateKbps, AudioBitrateKbps = source.AudioBitrateKbps,SeparateAudioTracks=source.SeparateAudioTracks };
        }
        public void StartRecording()
        {
            RequireSession();
            if (output != IntPtr.Zero) throw new InvalidOperationException("A recording is already running or still finalizing.");
            Directory.CreateDirectory(sessionOptions.OutputDirectory);
            OutputPath = Path.Combine(sessionOptions.OutputDirectory, "Rilmo " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".mkv");
            using (FileStream check = new FileStream(OutputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
            finalStats = new EngineStats();
            try
            {
                HashSet<string> available = GetAvailableEncoders();
                string[] candidates = videoEncoder == IntPtr.Zero ? GetCandidates(sessionOptions.EncoderPreference) : new[] { EncoderId };
                var failures = new List<string>();
                foreach (string id in candidates)
                {
                    if (!available.Contains(id)) continue;
                    if (!TryStartOutput(id, sessionOptions, failures)) continue;
                    EncoderId = id; EncoderName = FriendlyEncoderName(id); IsRecording = true; IsPaused = false;
                    Log("Recording started: " + EncoderName + ", " + sessionOptions.Width + "x" + sessionOptions.Height + " @ " + sessionOptions.Fps + " fps. " + OutputPath);
                    return;
                }
                throw new InvalidOperationException("No recording encoder could start. " + String.Join("; ", failures.ToArray()));
            }
            catch { try { if (File.Exists(OutputPath) && new FileInfo(OutputPath).Length == 0) File.Delete(OutputPath); } catch (IOException) { } throw; }
        }
        private IntPtr CreateVideoEncoder(string id, int bitrate, string name)
        {
            IntPtr settings = NativeObs.obs_data_create();
            try
            {
                NativeObs.obs_data_set_int(settings, "keyint_sec", 2); NativeObs.obs_data_set_string(settings, "profile", "high");
                NativeObs.obs_data_set_string(settings, "rate_control", bitrate > 0 ? "CBR" : id == "obs_x264" ? "CRF" : "CQP");
                NativeObs.obs_data_set_int(settings, "bitrate", bitrate > 0 ? bitrate : 4500);
                NativeObs.obs_data_set_int(settings, "crf", sessionOptions.Quality); NativeObs.obs_data_set_int(settings, "cqp", sessionOptions.Quality);
                if (id == "obs_x264")
                {
                    NativeObs.obs_data_set_string(settings, "preset", "ultrafast"); NativeObs.obs_data_set_string(settings, "tune", "zerolatency");
                    NativeObs.obs_data_set_string(settings, "x264opts", "threads=2");
                }
                else
                {
                    NativeObs.obs_data_set_int(settings, "bf", 0); NativeObs.obs_data_set_bool(settings, "pre_analysis", false); NativeObs.obs_data_set_bool(settings, "lookahead", false);
                    if (id.Contains("amf")) NativeObs.obs_data_set_string(settings, "preset", "speed");
                    if (id.Contains("nvenc")) NativeObs.obs_data_set_string(settings, "preset", "p3");
                    if (id.Contains("qsv")) NativeObs.obs_data_set_string(settings, "target_usage", "TU7");
                }
                IntPtr encoder = NativeObs.obs_video_encoder_create(id, name, settings, IntPtr.Zero);
                if (encoder != IntPtr.Zero) NativeObs.obs_encoder_set_video(encoder, NativeObs.obs_get_video());
                return encoder;
            }
            finally { NativeObs.obs_data_release(settings); }
        }
        private void ReleaseUnusedQualityEncoder()
        {
            if (videoEncoder != IntPtr.Zero && output == IntPtr.Zero && replayOutput == IntPtr.Zero) { NativeObs.obs_encoder_release(videoEncoder); videoEncoder = IntPtr.Zero; }
        }
        public void StartStreaming(StreamOptions options)
        {
            RequireSession();
            if (streamOutput != IntPtr.Zero) throw new InvalidOperationException("A stream is already running or finalizing.");
            if (IsPaused) throw new InvalidOperationException("Resume the recording before starting a stream.");
            Uri endpoint;
            if (options == null || !Uri.TryCreate(options.ServerUrl, UriKind.Absolute, out endpoint) || (endpoint.Scheme != "rtmp" && endpoint.Scheme != "rtmps") || String.IsNullOrEmpty(endpoint.Host) || !String.IsNullOrEmpty(endpoint.UserInfo) || options.ServerUrl.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("Enter an RTMP or RTMPS server URL. Use the separate username/password fields for authentication.");
            if (options.VideoBitrateKbps < 500 || options.VideoBitrateKbps > 50000 || options.AudioBitrateKbps < 64 || options.AudioBitrateKbps > 320) throw new ArgumentException("Stream video bitrate must be 500–50000 Kbps and audio 64–320 Kbps.");
            foreach (string field in new[] { options.ServerUrl, options.StreamKey, options.Username, options.Password })
                if (field != null && (field.Length > 4096 || field.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)) throw new ArgumentException("Streaming connection fields must be single lines of at most 4096 characters.");
            EnsureFeatureModule("obs-outputs"); EnsureFeatureModule("rtmp-services");
            lock (logGate) streamingSecrets = new[] { options.ServerUrl, options.StreamKey, options.Password, options.Username };
            lastStreamError = String.Empty;
            try
            {
                IntPtr settings = NativeObs.obs_data_create();
                try
                {
                    NativeObs.obs_data_set_string(settings, "server", options.ServerUrl);
                    NativeObs.obs_data_set_string(settings, "key", options.StreamKey ?? String.Empty);
                    NativeObs.obs_data_set_bool(settings, "use_auth", !String.IsNullOrEmpty(options.Username));
                    NativeObs.obs_data_set_string(settings, "username", options.Username ?? String.Empty);
                    NativeObs.obs_data_set_string(settings, "password", options.Password ?? String.Empty);
                    streamService = NativeObs.obs_service_create("rtmp_custom", "Streaming service", settings, IntPtr.Zero);
                }
                finally { NativeObs.obs_data_release(settings); }
                if (streamService == IntPtr.Zero) throw new InvalidOperationException("The RTMP service could not initialize.");
                streamAudioEncoder = CreateAudioEncoder(options.AudioBitrateKbps);
                HashSet<string> available = GetAvailableEncoders();
                var failures = new List<string>();
                foreach (string id in GetCandidates(sessionOptions.EncoderPreference))
                {
                    if (!available.Contains(id)) continue;
                    streamVideoEncoder = CreateVideoEncoder(id, options.VideoBitrateKbps, "Stream video");
                    if (streamVideoEncoder == IntPtr.Zero) continue;
                    streamOutput = NativeObs.obs_output_create("rtmp_output", "Stream", IntPtr.Zero, IntPtr.Zero);
                    if (streamOutput == IntPtr.Zero) throw new InvalidOperationException("The RTMP output is unavailable.");
                    streamStopped = new ManualResetEvent(false);
                    streamStoppedCallback = delegate { try { streamStopped.Set(); } catch (ObjectDisposedException) { } };
                    NativeObs.signal_handler_connect(NativeObs.obs_output_get_signal_handler(streamOutput), "stop", streamStoppedCallback, IntPtr.Zero);
                    NativeObs.obs_output_set_video_encoder(streamOutput, streamVideoEncoder);
                    NativeObs.obs_output_set_audio_encoder(streamOutput, streamAudioEncoder, UIntPtr.Zero);
                    NativeObs.obs_output_set_service(streamOutput, streamService);
                    NativeObs.obs_output_set_reconnect_settings(streamOutput, 0, 2); // Explicit error state instead of invisible endless reconnects.
                    bool started = NativeObs.obs_output_start(streamOutput);
                    if (started)
                    {
                        streamConnecting = true;
                        Stopwatch wait = Stopwatch.StartNew();
                        // obs_output_active is false during asynchronous RTMP connect.
                        // Wait for packets or an explicit stop signal before deciding.
                        while (!streamStopped.WaitOne(0) && NativeObs.obs_output_get_total_bytes(streamOutput) == 0 && wait.ElapsedMilliseconds < 12000) Thread.Sleep(50);
                        if (streamStopped.WaitOne(0) || NativeObs.obs_output_active(streamOutput)) streamConnecting = false;
                        if (NativeObs.obs_output_active(streamOutput) && NativeObs.obs_output_get_total_bytes(streamOutput) > 0)
                        { IsStreaming = true; Log("Stream connected using " + FriendlyEncoderName(id) + ". Destination and credentials redacted."); return; }
                        // A service/network failure will not improve by opening multiple GPU encoders.
                        string connectionError = RedactSecrets(NativeObs.Text(NativeObs.obs_output_get_last_error(streamOutput)));
                        throw new IOException("The stream did not connect. " + (connectionError.Length == 0 ? "Check the server URL, stream key and network connection." : connectionError));
                    }
                    failures.Add(id + ": " + RedactSecrets(NativeObs.Text(NativeObs.obs_output_get_last_error(streamOutput))));
                    DisconnectStreamSignal();
                    NativeObs.obs_output_release(streamOutput); streamOutput = IntPtr.Zero;
                    NativeObs.obs_encoder_release(streamVideoEncoder); streamVideoEncoder = IntPtr.Zero;
                }
                throw new InvalidOperationException("No streaming encoder could start. " + String.Join("; ", failures.ToArray()));
            }
            catch (Exception error)
            {
                lastStreamError = RedactSecrets(error.Message);
                try { StopStreaming(); } catch (Exception cleanup) { throw new InvalidOperationException(lastStreamError + ". Streaming cleanup requires Stop again: " + RedactSecrets(cleanup.Message)); }
                throw new InvalidOperationException(lastStreamError);
            }
        }
        public void StopStreaming()
        {
            CheckThread();
            if (streamOutput != IntPtr.Zero)
            {
                // Pending connect has no active flag; force_stop joins the native
                // connect thread before releasing its output/service/encoder refs.
                if (streamConnecting) { NativeObs.obs_output_force_stop(streamOutput); streamConnecting = false; }
                StopNativeOutput(streamOutput, "Streaming");
                string error = RedactSecrets(NativeObs.Text(NativeObs.obs_output_get_last_error(streamOutput)));
                if (error.Length != 0) lastStreamError = error;
                DisconnectStreamSignal(); NativeObs.obs_output_release(streamOutput); streamOutput = IntPtr.Zero;
            }
            if (streamVideoEncoder != IntPtr.Zero) { NativeObs.obs_encoder_release(streamVideoEncoder); streamVideoEncoder = IntPtr.Zero; }
            if (streamAudioEncoder != IntPtr.Zero) { NativeObs.obs_encoder_release(streamAudioEncoder); streamAudioEncoder = IntPtr.Zero; }
            if (streamService != IntPtr.Zero) { NativeObs.obs_service_release(streamService); streamService = IntPtr.Zero; }
            IsStreaming = false;
            lock (logGate) streamingSecrets = new string[0];
        }
        private void DisconnectStreamSignal()
        {
            if (streamStoppedCallback != null) NativeObs.signal_handler_disconnect(NativeObs.obs_output_get_signal_handler(streamOutput), "stop", streamStoppedCallback, IntPtr.Zero);
            streamStoppedCallback = null;
            if (streamStopped != null) { streamStopped.Dispose(); streamStopped = null; }
        }
        public void StartReplayBuffer(ReplayOptions options)
        {
            RequireSession();
            if (replayOutput != IntPtr.Zero) throw new InvalidOperationException("The replay buffer is already running or finalizing.");
            if (IsPaused) throw new InvalidOperationException("Resume the recording before starting replay.");
            if (options == null || options.Seconds < 5 || options.Seconds > 300 || options.MaximumMegabytes < 16 || options.MaximumMegabytes > 1024) throw new ArgumentException("Replay length must be 5–300 seconds, with a 16–1024 MB buffer.");
            string directory = Path.GetFullPath(String.IsNullOrWhiteSpace(options.OutputDirectory) ? sessionOptions.OutputDirectory : options.OutputDirectory);
            Directory.CreateDirectory(directory);
            string writeProbe = Path.Combine(directory, ".rilmo-write-" + Guid.NewGuid().ToString("N"));
            using (var probe = new FileStream(writeProbe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            bool shared = videoEncoder != IntPtr.Zero;
            var available = GetAvailableEncoders();
            string[] candidates = shared ? new[] { EncoderId } : GetCandidates(sessionOptions.EncoderPreference);
            try
            {
                foreach (string id in candidates)
                {
                    if (!available.Contains(id)) continue;
                    if (!shared) videoEncoder = CreateVideoEncoder(id, sessionOptions.VideoBitrateKbps, "Replay video");
                    if (videoEncoder == IntPtr.Zero) continue;
                    IntPtr settings = NativeObs.obs_data_create();
                    try
                    {
                        NativeObs.obs_data_set_string(settings, "directory", directory);
                        NativeObs.obs_data_set_string(settings, "format", "Rilmo Replay %CCYY-%MM-%DD %hh-%mm-%ss-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                        NativeObs.obs_data_set_string(settings, "extension", "mkv");
                        NativeObs.obs_data_set_bool(settings, "allow_spaces", true);
                        NativeObs.obs_data_set_int(settings, "max_time_sec", options.Seconds);
                        NativeObs.obs_data_set_int(settings, "max_size_mb", options.MaximumMegabytes);
                        replayOutput = NativeObs.obs_output_create("replay_buffer", "Replay buffer", settings, IntPtr.Zero);
                    }
                    finally { NativeObs.obs_data_release(settings); }
                    if (replayOutput == IntPtr.Zero) throw new InvalidOperationException("The replay buffer output is unavailable.");
                    NativeObs.obs_output_set_video_encoder(replayOutput, videoEncoder);
                    NativeObs.obs_output_set_audio_encoder(replayOutput, audioEncoder, UIntPtr.Zero);
                    if (NativeObs.obs_output_start(replayOutput))
                    {
                        replaySaved = new ManualResetEvent(false);
                        replaySavedCallback = delegate { try { replaySaved.Set(); } catch (ObjectDisposedException) { } };
                        NativeObs.signal_handler_connect(NativeObs.obs_output_get_signal_handler(replayOutput), "saved", replaySavedCallback, IntPtr.Zero);
                        EncoderId = id; EncoderName = FriendlyEncoderName(id); IsReplayBufferActive = true; return;
                    }
                    NativeObs.obs_output_release(replayOutput); replayOutput = IntPtr.Zero;
                    if (!shared) { NativeObs.obs_encoder_release(videoEncoder); videoEncoder = IntPtr.Zero; }
                }
                throw new InvalidOperationException("No replay encoder could start.");
            }
            catch { StopReplayBuffer(); throw; }
        }
        public string SaveReplay()
        {
            RequireSession();
            if (replayOutput == IntPtr.Zero || !NativeObs.obs_output_active(replayOutput)) throw new InvalidOperationException("Start the replay buffer before saving a clip.");
            replaySaved.Reset();
            NativeObs.CallData data = new NativeObs.CallData();
            try
            {
                IntPtr handler = NativeObs.obs_output_get_proc_handler(replayOutput);
                if (!NativeObs.proc_handler_call(handler, "save", ref data)) throw new InvalidOperationException("The replay save command is unavailable.");
                if (!replaySaved.WaitOne(15000)) throw new TimeoutException("Replay save has not completed. Keep the session open; check the output folder and engine log before trying again.");
                if (!NativeObs.proc_handler_call(handler, "get_last_replay", ref data)) throw new IOException("The saved replay path could not be read.");
                IntPtr path;
                if (!NativeObs.calldata_get_string(ref data, "path", out path)) throw new IOException("The replay output returned no saved path.");
                string result = NativeObs.Text(path);
                if (!File.Exists(result) || new FileInfo(result).Length < 1024) throw new IOException("The replay did not produce a valid file.");
                return result;
            }
            finally { if (data.Stack != IntPtr.Zero) NativeObs.bfree(data.Stack); }
        }
        public void StopReplayBuffer()
        {
            CheckThread();
            if (replayOutput != IntPtr.Zero)
            {
                StopNativeOutput(replayOutput, "Replay buffer");
                if (replaySavedCallback != null) NativeObs.signal_handler_disconnect(NativeObs.obs_output_get_signal_handler(replayOutput), "saved", replaySavedCallback, IntPtr.Zero);
                NativeObs.obs_output_release(replayOutput); replayOutput = IntPtr.Zero;
            }
            if (replaySaved != null) { replaySaved.Dispose(); replaySaved = null; }
            replaySavedCallback = null; IsReplayBufferActive = false; ReleaseUnusedQualityEncoder();
        }
        private static void StopNativeOutput(IntPtr nativeOutput, string name)
        {
            if (!NativeObs.obs_output_active(nativeOutput)) return;
            NativeObs.obs_output_stop(nativeOutput);
            Stopwatch wait = Stopwatch.StartNew();
            while (NativeObs.obs_output_active(nativeOutput) && wait.ElapsedMilliseconds < 8000) Thread.Sleep(30);
            if (NativeObs.obs_output_active(nativeOutput))
            {
                NativeObs.obs_output_force_stop(nativeOutput); wait.Restart();
                while (NativeObs.obs_output_active(nativeOutput) && wait.ElapsedMilliseconds < 5000) Thread.Sleep(30);
            }
            if (NativeObs.obs_output_active(nativeOutput)) throw new TimeoutException(name + " is still stopping. Keep the application open and try Stop again.");
        }
        public OutputStatus GetOutputStatus()
        {
            CheckThread();
            if (streamOutput != IntPtr.Zero && IsStreaming && !NativeObs.obs_output_active(streamOutput))
            {
                StopStreaming();
                if (String.IsNullOrEmpty(lastStreamError)) lastStreamError = "The stream disconnected. Check the server and network before starting it again.";
            }
            if (replayOutput != IntPtr.Zero && IsReplayBufferActive && !NativeObs.obs_output_active(replayOutput)) StopReplayBuffer();
            return new OutputStatus { Streaming = IsStreaming, ReplayBufferActive = IsReplayBufferActive, LastStreamError = lastStreamError,
                StreamBytes = streamOutput == IntPtr.Zero ? 0 : (long)NativeObs.obs_output_get_total_bytes(streamOutput),
                StreamDroppedFrames = streamOutput == IntPtr.Zero ? 0 : NativeObs.obs_output_get_frames_dropped(streamOutput) };
        }
        private string RedactSecrets(string message)
        {
            if (String.IsNullOrEmpty(message)) return String.Empty;
            foreach (string secret in streamingSecrets) if (!String.IsNullOrEmpty(secret)) message = message.Replace(secret, "[redacted]");
            return Regex.Replace(message, @"(?i)rtmps?://[^\s\""']+", "[stream destination]");
        }
    }
}
