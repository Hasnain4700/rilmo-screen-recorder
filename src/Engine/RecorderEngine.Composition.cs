using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Rilmo.Engine
{
    public sealed partial class RecorderEngine
    {
        private sealed class OverlayHandle { internal IntPtr Source, Item; }
        private readonly Dictionary<string, OverlayHandle> overlayHandles = new Dictionary<string, OverlayHandle>(StringComparer.Ordinal);
        private readonly HashSet<string> featureModules = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<IntPtr> desktopFilters = new List<IntPtr>(), microphoneFilters = new List<IntPtr>();
        private IntPtr desktopMeter, microphoneMeter;
        private NativeObs.MeterCallback meterCallback;
        private readonly object meterGate = new object();
        private float desktopPeak = -96, microphonePeak = -96;
        private long desktopMeterTime, microphoneMeterTime;
        private int desktopFilterMask = -1, microphoneFilterMask = -1;
        private bool desktopMuted, microphoneMuted;
        [StructLayout(LayoutKind.Explicit)] private struct FloatBits
        {
            [FieldOffset(0)] internal int Bits;
            [FieldOffset(0)] internal float Value;
        }

        private void RequireSession()
        {
            CheckThread();
            if (!initialized || scene == IntPtr.Zero) throw new InvalidOperationException("Start a capture session first.");
        }
        private void EnsureFeatureModule(string name)
        {
            if (featureModules.Contains(name)) return;
            LoadModule(name, true); NativeObs.obs_post_load_modules(); featureModules.Add(name);
        }
        private void InitializeCompositionAndAudio(RecorderOptions options)
        {
            if(options.SeparateAudioTracks)for(int i=0;i<2;i++)separateAudioEncoders[i]=CreateAudioEncoder(options.AudioBitrateKbps,i+1);
            meterCallback = OnMeter;
            if (desktopAudio != IntPtr.Zero) { SetAudioControls(AudioChannel.System, options.SystemAudioSettings ?? new AudioMixSettings()); desktopMeter = CreateMeter(desktopAudio, 0); }
            if (microphone != IntPtr.Zero) { SetAudioControls(AudioChannel.Microphone, options.MicrophoneSettings ?? new AudioMixSettings()); microphoneMeter = CreateMeter(microphone, 1); }
            if (options.SceneCollection == null && options.Overlays != null) foreach (SceneOverlay overlay in options.Overlays) AddOverlay(overlay);
        }
        public CaptureDevice[] GetCameraDevices()
        {
            RequireSession(); EnsureFeatureModule("win-dshow");
            return EnumerateCameraDevices();
        }
        private CaptureDevice[] EnumerateCameraDevices()
        {
            var result = new List<CaptureDevice>();
            IntPtr props = NativeObs.obs_get_source_properties("dshow_input");
            if (props == IntPtr.Zero) throw new InvalidOperationException("Camera capture is unavailable in this runtime.");
            try
            {
                IntPtr list = NativeObs.obs_properties_get(props, "video_device_id");
                if (list == IntPtr.Zero) return result.ToArray();
                for (ulong i = 0; i < NativeObs.obs_property_list_item_count(list).ToUInt64(); i++)
                {
                    string id = NativeObs.Text(NativeObs.obs_property_list_item_string(list, new UIntPtr(i)));
                    if (id.Length != 0) result.Add(new CaptureDevice { Id = id, Name = NativeObs.Text(NativeObs.obs_property_list_item_name(list, new UIntPtr(i))) });
                }
                return result.ToArray();
            }
            finally { NativeObs.obs_properties_destroy(props); }
        }
        public void AddOverlay(SceneOverlay overlay)
        {
            RequireSession();
            if(sceneCollection!=null) throw new InvalidOperationException("Use scene source operations for a named scene collection.");
            if (overlay == null || String.IsNullOrWhiteSpace(overlay.Id) || overlay.Id.Length > 100) throw new ArgumentException("Each overlay needs an ID of 1–100 characters.");
            if (overlayHandles.ContainsKey(overlay.Id)) throw new ArgumentException("An overlay already uses this ID.");
            if (overlayHandles.Count >= 16) throw new InvalidOperationException("This canvas supports up to 16 overlays.");
            ValidateTransform(overlay.Transform);
            string sourceType;
            if (overlay.Kind == OverlayKind.Camera)
            {
                bool found = false;
                foreach (CaptureDevice device in GetCameraDevices()) if (device.Id == overlay.DeviceId) found = true;
                if (!found) throw new InvalidOperationException("The selected camera is unavailable. Connect it and refresh the camera list.");
                sourceType = "dshow_input";
            }
            else if (overlay.Kind == OverlayKind.Image)
            {
                if (String.IsNullOrWhiteSpace(overlay.FilePath) || !File.Exists(overlay.FilePath)) throw new FileNotFoundException("The overlay image is missing.");
                if (new FileInfo(overlay.FilePath).Length > 32 * 1024 * 1024) throw new ArgumentException("Choose an image smaller than 32 MB.");
                EnsureFeatureModule("image-source"); sourceType = "image_source";
            }
            else if (overlay.Kind == OverlayKind.Text)
            {
                if (String.IsNullOrEmpty(overlay.Text) || overlay.Text.Length > 4096 || overlay.FontSize < 8 || overlay.FontSize > 256) throw new ArgumentException("Text overlays need 1–4096 characters and a font size from 8 to 256.");
                EnsureFeatureModule("obs-text"); sourceType = "text_gdiplus_v3";
            }
            else throw new ArgumentException("Unsupported overlay kind.");
            IntPtr settings = NativeObs.obs_data_create(), source = IntPtr.Zero, item = IntPtr.Zero;
            try
            {
                if (overlay.Kind == OverlayKind.Camera)
                {
                    NativeObs.obs_data_set_string(settings, "video_device_id", overlay.DeviceId);
                    NativeObs.obs_data_set_int(settings, "res_type", 0); // Device default: avoid requesting an unsupported capture mode.
                    NativeObs.obs_data_set_bool(settings, "deactivate_when_not_showing", true);
                    // An explicitly empty custom audio ID makes win-dshow skip its
                    // audio configuration; camera audio must not open implicitly.
                    NativeObs.obs_data_set_bool(settings, "use_custom_audio_device", true);
                    NativeObs.obs_data_set_string(settings, "audio_device_id", "");
                }
                else if (overlay.Kind == OverlayKind.Image)
                {
                    NativeObs.obs_data_set_string(settings, "file", Path.GetFullPath(overlay.FilePath));
                    NativeObs.obs_data_set_bool(settings, "unload", false);
                }
                else
                {
                    NativeObs.obs_data_set_string(settings, "text", overlay.Text);
                    uint rgb = overlay.TextColor;
                    NativeObs.obs_data_set_int(settings, "color", ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF));
                    IntPtr font = NativeObs.obs_data_create();
                    try { NativeObs.obs_data_set_string(font, "face", "Segoe UI"); NativeObs.obs_data_set_int(font, "size", overlay.FontSize); NativeObs.obs_data_set_obj(settings, "font", font); }
                    finally { NativeObs.obs_data_release(font); }
                }
                source = NativeObs.obs_source_create(sourceType, "Overlay " + overlay.Id, settings, IntPtr.Zero);
                if (source == IntPtr.Zero) throw new InvalidOperationException("The overlay source could not be created.");
                NativeObs.obs_source_set_audio_mixers(source, 0); // Never capture an implicit camera microphone.
                item = NativeObs.obs_scene_add(scene, source);
                if (item == IntPtr.Zero) throw new InvalidOperationException("The overlay could not be added to the canvas.");
                Stopwatch wait = Stopwatch.StartNew();
                while (NativeObs.obs_source_get_width(source) == 0 && wait.ElapsedMilliseconds < 5000) Thread.Sleep(40);
                if (NativeObs.obs_source_get_width(source) == 0) throw new InvalidOperationException("The overlay produced no video. Check camera permissions, another app using the camera, or the image format.");
                ApplyTransform(item, overlay.Transform);
                overlayHandles.Add(overlay.Id, new OverlayHandle { Source = source, Item = item });
            }
            catch { if (item != IntPtr.Zero) NativeObs.obs_sceneitem_remove(item); if (source != IntPtr.Zero) NativeObs.obs_source_release(source); throw; }
            finally { NativeObs.obs_data_release(settings); }
        }
        public void UpdateOverlay(string id, SourceTransform transform)
        {
            RequireSession(); ValidateTransform(transform);
            OverlayHandle overlay;
            if (id == null || !overlayHandles.TryGetValue(id, out overlay)) throw new ArgumentException("The overlay does not exist.");
            ApplyTransform(overlay.Item, transform);
        }
        public void RemoveOverlay(string id)
        {
            RequireSession(); OverlayHandle overlay;
            if (id == null || !overlayHandles.TryGetValue(id, out overlay)) throw new ArgumentException("The overlay does not exist.");
            NativeObs.obs_sceneitem_remove(overlay.Item); NativeObs.obs_source_release(overlay.Source); overlayHandles.Remove(id);
        }
        public void UpdateCaptureTransform(SourceTransform transform) { RequireSession(); if(captureItem==IntPtr.Zero) throw new InvalidOperationException("Use scene source transforms for this canvas."); ValidateTransform(transform); ApplyTransform(captureItem, transform); }
        private static void ValidateTransform(SourceTransform transform)
        {
            if (transform == null || !Finite(transform.X) || !Finite(transform.Y) || !Finite(transform.Width) || !Finite(transform.Height) || !Finite(transform.Rotation) || transform.Width < 1 || transform.Height < 1 || transform.Width > 16384 || transform.Height > 16384 || Math.Abs(transform.X) > 16384 || Math.Abs(transform.Y) > 16384)
                throw new ArgumentException("Choose finite canvas coordinates and overlay dimensions from 1 to 16384.");
        }
        private static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
        private static void ApplyTransform(IntPtr item, SourceTransform transform)
        {
            NativeObs.Vec2 position = new NativeObs.Vec2 { X = transform.X, Y = transform.Y }, bounds = new NativeObs.Vec2 { X = transform.Width, Y = transform.Height };
            NativeObs.obs_sceneitem_set_alignment(item, 5); NativeObs.obs_sceneitem_set_bounds_alignment(item, 0);
            NativeObs.obs_sceneitem_set_bounds_type(item, 2); NativeObs.obs_sceneitem_set_bounds(item, ref bounds);
            NativeObs.obs_sceneitem_set_pos(item, ref position); NativeObs.obs_sceneitem_set_rot(item, transform.Rotation);
            NativeObs.obs_sceneitem_set_scale_filter(item, 3); NativeObs.obs_sceneitem_set_visible(item, transform.Visible);
        }
        public void SetAudioControls(AudioChannel channel, AudioMixSettings settings)
        {
            RequireSession();
            if (settings == null || !Finite(settings.Volume) || settings.Volume < 0 || settings.Volume > 2) throw new ArgumentException("Audio volume must be between 0 and 2.");
            if (channel != AudioChannel.System && channel != AudioChannel.Microphone) throw new ArgumentException("Unknown audio channel.");
            IntPtr source = channel == AudioChannel.System ? desktopAudio : microphone;
            if (source == IntPtr.Zero) throw new InvalidOperationException("This audio input was disabled when the session started.");
            if(settings.SyncOffsetMs < -1000 || settings.SyncOffsetMs>20000)throw new ArgumentException("Audio sync offset must be -1000 to 20000 milliseconds.");
            NativeObs.obs_source_set_sync_offset(source,settings.SyncOffsetMs*1000000L);
            if(settings.Monitor&&!NativeObs.obs_set_audio_monitoring_device("Default","default"))throw new InvalidOperationException("The default audio monitoring device is unavailable.");
            NativeObs.obs_source_set_monitoring_type(source,settings.Monitor?2:0);
            int mask = (settings.NoiseSuppression ? 1 : 0) | (settings.NoiseGate ? 2 : 0) | (settings.Compressor ? 4 : 0) | (settings.Limiter ? 8 : 0);
            int existingMask = channel == AudioChannel.System ? desktopFilterMask : microphoneFilterMask;
            if (mask == existingMask)
            {
                NativeObs.obs_source_set_volume(source, settings.Volume); NativeObs.obs_source_set_muted(source, settings.Muted);
                if (channel == AudioChannel.System) desktopMuted = settings.Muted; else microphoneMuted = settings.Muted;
                return;
            }
            var filters = channel == AudioChannel.System ? desktopFilters : microphoneFilters;
            var replacements = new List<IntPtr>();
            if (settings.NoiseSuppression || settings.NoiseGate || settings.Compressor || settings.Limiter) EnsureFeatureModule("obs-filters");
            try
            {
                if (settings.NoiseSuppression) replacements.Add(CreateFilter("noise_suppress_filter_v2", "Noise suppression", true));
                if (settings.NoiseGate) replacements.Add(CreateFilter("noise_gate_filter", "Noise gate", false));
                if (settings.Compressor) replacements.Add(CreateFilter("compressor_filter", "Compressor", false));
                if (settings.Limiter) replacements.Add(CreateFilter("limiter_filter", "Limiter", false));
            }
            catch { foreach (IntPtr filter in replacements) NativeObs.obs_source_release(filter); throw; }
            foreach (IntPtr filter in filters) { NativeObs.obs_source_filter_remove(source, filter); NativeObs.obs_source_release(filter); }
            filters.Clear();
            foreach (IntPtr filter in replacements) { NativeObs.obs_source_filter_add(source, filter); filters.Add(filter); }
            if (channel == AudioChannel.System) desktopFilterMask = mask; else microphoneFilterMask = mask;
            NativeObs.obs_source_set_volume(source, settings.Volume); NativeObs.obs_source_set_muted(source, settings.Muted);
            if (channel == AudioChannel.System) desktopMuted = settings.Muted; else microphoneMuted = settings.Muted;
        }
        private static IntPtr CreateFilter(string type, string name, bool suppression)
        {
            IntPtr settings = NativeObs.obs_data_create();
            try
            {
                if (suppression) { NativeObs.obs_data_set_string(settings, "method", "speex"); NativeObs.obs_data_set_int(settings, "suppress_level", -30); }
                IntPtr filter = NativeObs.obs_source_create(type, name, settings, IntPtr.Zero);
                if (filter == IntPtr.Zero) throw new InvalidOperationException(name + " is unavailable in this runtime.");
                return filter;
            }
            finally { NativeObs.obs_data_release(settings); }
        }
        private IntPtr CreateMeter(IntPtr source, int channel)
        {
            IntPtr meter = NativeObs.obs_volmeter_create(2);
            if (meter == IntPtr.Zero) throw new InvalidOperationException("The audio meter could not initialize.");
            if (!NativeObs.obs_volmeter_attach_source(meter, source)) { NativeObs.obs_volmeter_destroy(meter); throw new InvalidOperationException("The audio meter could not attach."); }
            NativeObs.obs_volmeter_add_callback(meter, meterCallback, new IntPtr(channel)); return meter;
        }
        private void OnMeter(IntPtr channel, IntPtr magnitude, IntPtr peak, IntPtr inputPeak)
        {
            // Callback must never throw across the native audio thread boundary.
            try
            {
                FloatBits left = new FloatBits { Bits = Marshal.ReadInt32(peak) }, right = new FloatBits { Bits = Marshal.ReadInt32(peak, 4) };
                float value = Math.Max(left.Value, right.Value); if (!Finite(value)) value = -96; value = Math.Max(-96, value);
                lock (meterGate) { if (channel == IntPtr.Zero) { desktopPeak = value; desktopMeterTime = Stopwatch.GetTimestamp(); } else { microphonePeak = value; microphoneMeterTime = Stopwatch.GetTimestamp(); } }
            }
            catch { }
        }
        public AudioLevels GetAudioLevels()
        {
            CheckThread();
            lock (meterGate) { long now = Stopwatch.GetTimestamp(); return new AudioLevels { SystemPeakDb = !desktopMuted && now - desktopMeterTime < Stopwatch.Frequency ? desktopPeak : -96, MicrophonePeakDb = !microphoneMuted && now - microphoneMeterTime < Stopwatch.Frequency ? microphonePeak : -96 }; }
        }
        private void ReleaseCompositionAndAudio()
        {
            if (desktopMeter != IntPtr.Zero) { NativeObs.obs_volmeter_remove_callback(desktopMeter, meterCallback, IntPtr.Zero); NativeObs.obs_volmeter_destroy(desktopMeter); desktopMeter = IntPtr.Zero; }
            if (microphoneMeter != IntPtr.Zero) { NativeObs.obs_volmeter_remove_callback(microphoneMeter, meterCallback, new IntPtr(1)); NativeObs.obs_volmeter_destroy(microphoneMeter); microphoneMeter = IntPtr.Zero; }
            foreach (IntPtr filter in desktopFilters) NativeObs.obs_source_release(filter); desktopFilters.Clear();
            foreach (IntPtr filter in microphoneFilters) NativeObs.obs_source_release(filter); microphoneFilters.Clear();
            foreach (OverlayHandle overlay in overlayHandles.Values) { NativeObs.obs_sceneitem_remove(overlay.Item); NativeObs.obs_source_release(overlay.Source); }
            overlayHandles.Clear(); featureModules.Clear(); captureItem = IntPtr.Zero;
            desktopFilterMask = microphoneFilterMask = -1;
            desktopMuted = microphoneMuted = false;
            lock (meterGate) { desktopPeak = microphonePeak = -96; desktopMeterTime = microphoneMeterTime = 0; }
            GC.KeepAlive(meterCallback); meterCallback = null;
        }
    }
}
