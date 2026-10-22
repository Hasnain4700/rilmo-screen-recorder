using System;
using System.Collections.Generic;

namespace Rilmo.Engine
{
    public sealed partial class RecorderOptions
    {
        public List<SceneOverlay> Overlays { get; set; } = new List<SceneOverlay>();
        public AudioMixSettings SystemAudioSettings { get; set; } = new AudioMixSettings();
        public AudioMixSettings MicrophoneSettings { get; set; } = new AudioMixSettings();
        public int Quality { get; set; } = 23;
        public int VideoBitrateKbps { get; set; }
        public int AudioBitrateKbps { get; set; } = 160;
        public bool SeparateAudioTracks { get; set; }
    }
    public enum OverlayKind { Camera, Image, Text }
    public enum AudioChannel { System, Microphone }
    public sealed class SourceTransform
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; } = 320;
        public float Height { get; set; } = 180;
        public float Rotation { get; set; }
        public bool Visible { get; set; } = true;
    }
    public sealed class SceneOverlay
    {
        public string Id { get; set; }
        public OverlayKind Kind { get; set; }
        public string DeviceId { get; set; }
        public string FilePath { get; set; }
        public string Text { get; set; }
        public int FontSize { get; set; } = 32;
        public uint TextColor { get; set; } = 0xFFFFFF;
        public SourceTransform Transform { get; set; } = new SourceTransform();
    }
    public sealed class CaptureDevice { public string Id { get; set; } public string Name { get; set; } }
    public sealed class AudioMixSettings
    {
        public float Volume { get; set; } = 1;
        public bool Muted { get; set; }
        public bool NoiseSuppression { get; set; }
        public bool NoiseGate { get; set; }
        public bool Compressor { get; set; }
        public bool Limiter { get; set; }
        public int SyncOffsetMs { get; set; }
        public bool Monitor { get; set; }
    }
    public sealed class AudioLevels
    {
        public float SystemPeakDb { get; set; } = -96;
        public float MicrophonePeakDb { get; set; } = -96;
    }
    public sealed class StreamOptions
    {
        public string ServerUrl { get; set; }
        public string StreamKey { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public int VideoBitrateKbps { get; set; } = 4500;
        public int AudioBitrateKbps { get; set; } = 160;
    }
    public sealed class ReplayOptions
    {
        public int Seconds { get; set; } = 30;
        public int MaximumMegabytes { get; set; } = 128;
        public string OutputDirectory { get; set; }
    }
    public sealed class OutputStatus
    {
        public bool Streaming { get; set; }
        public bool ReplayBufferActive { get; set; }
        public long StreamBytes { get; set; }
        public int StreamDroppedFrames { get; set; }
        public string LastStreamError { get; set; }
    }
}
