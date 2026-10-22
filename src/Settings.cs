using System;
using System.IO;
using System.Text.Json;

namespace Rilmo
{
    public sealed class Settings
    {
        public static string DataDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rilmo Recorder"); } }
        public string OutputDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Rilmo Recordings");
        public int MonitorIndex { get; set; }
        public string MonitorDeviceName { get; set; }
        public string MonitorIdentity { get; set; }
        public int QualityIndex { get; set; }
        public bool SystemAudio { get; set; } = true;
        public bool Microphone { get; set; }
        public bool CaptureCursor { get; set; } = true;
        public bool Countdown { get; set; } = true;
        public bool MinimizeOnRecord { get; set; } = true;
        public bool SoftwareEncoder { get; set; }
        public bool UseSceneCollection { get; set; }
        public bool Animations { get; set; } = true;
        public string MicrophoneDeviceId { get; set; } = "default";
        public string SystemAudioDeviceId { get; set; } = "default";
        public bool FloatingControls { get; set; } = true;
        public int? FloatingX { get; set; }
        public int? FloatingY { get; set; }
        public double SystemVolume { get; set; } = 100;
        public double MicrophoneVolume { get; set; } = 100;
        public bool NoiseSuppression { get; set; }
        public bool NoiseGate { get; set; }
        public bool AudioLimiter { get; set; } = true;
        public bool AudioCompressor { get; set; }
        public bool MicrophoneMonitor { get; set; }
        public int SystemSyncOffsetMs { get; set; }
        public int MicrophoneSyncOffsetMs { get; set; }
        public bool SeparateAudioTracks { get; set; }
        public bool CameraEnabled { get; set; }
        public string CameraId { get; set; }
        public int CameraPosition { get; set; }
        public string OverlayText { get; set; } = "";
        public string LogoPath { get; set; } = "";
        public int OverlayPosition { get; set; }
        public int ScenePreset { get; set; }
        public bool ReplayWithRecording { get; set; }
        public int ReplaySeconds { get; set; } = 30;
        public string StreamServer { get; set; } = "";
        public int StreamBitrate { get; set; } = 4500;
        public HotkeyBinding[] Hotkeys { get; set; } = HotkeyBinding.Defaults();
        public static Settings Load()
        {
            try
            {
                string path = Path.Combine(DataDirectory, "settings.json");
                if (File.Exists(path))
                {
                    var value = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings();
                    if (string.IsNullOrWhiteSpace(value.OutputDirectory) || !Path.IsPathFullyQualified(value.OutputDirectory)) value.OutputDirectory = new Settings().OutputDirectory;
                    value.QualityIndex = Math.Clamp(value.QualityIndex, 0, 3);
                    value.SystemVolume = double.IsFinite(value.SystemVolume) ? Math.Clamp(value.SystemVolume, 0, 100) : 100;
                    value.MicrophoneVolume = double.IsFinite(value.MicrophoneVolume) ? Math.Clamp(value.MicrophoneVolume, 0, 100) : 100;
                    value.ReplaySeconds = Math.Clamp(value.ReplaySeconds, 5, 120);
                    value.StreamBitrate = Math.Clamp(value.StreamBitrate, 500, 20000);
                    value.ScenePreset = Math.Clamp(value.ScenePreset, 0, 2);
                    value.CameraPosition = Math.Clamp(value.CameraPosition, 0, 3);
                    value.OverlayPosition = Math.Clamp(value.OverlayPosition, 0, 3);
                    value.SystemSyncOffsetMs=Math.Clamp(value.SystemSyncOffsetMs,-1000,20000);
                    value.MicrophoneSyncOffsetMs=Math.Clamp(value.MicrophoneSyncOffsetMs,-1000,20000);
                    try { HotkeyBinding.Validate(value.Hotkeys); } catch { value.Hotkeys=HotkeyBinding.Defaults(); }
                    return value;
                }
            }
            catch (Exception ex) { App.WriteError(ex); }
            return new Settings();
        }
        public void Save()
        {
            Directory.CreateDirectory(DataDirectory);
            string path = Path.Combine(DataDirectory, "settings.json"), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
    }
}
