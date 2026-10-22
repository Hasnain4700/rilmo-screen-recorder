# Engine v2 integration contract

All instance operations run sequentially on `EngineWorker`. Options are read when a session starts; UI changes afterwards use the explicit update methods below. Existing `Start(RecorderOptions)` and `Stop()` remain compatible.

## Independent outputs

- `StartSession(RecorderOptions options)`: acquire capture/audio and create the canvas, without creating a recording file. `IsSessionActive` becomes true.
- `StartRecording()`: start an MKV file in the session output directory. `IsRecording`, `OutputPath`, `EncoderName` retain their existing meaning.
- `StopRecording()`: finalize only the recording; the session remains available. Existing `Stop()` finalizes every output and releases the entire session.
- `StartStreaming(StreamOptions options)`, `StopStreaming()`, `IsStreaming`: custom RTMP/RTMPS, H.264/AAC. `StreamOptions` has `ServerUrl`, `StreamKey`, optional `Username`, `Password`, `VideoBitrateKbps` (default 4500), and `AudioBitrateKbps` (default 160). Streaming creates a separate CBR encoder. Never persist credentials or include them in UI error diagnostics.
- `StartReplayBuffer(ReplayOptions options)`, `StopReplayBuffer()`, `IsReplayBufferActive`: encoded in-memory replay buffer. `ReplayOptions` has `Seconds` (default 30, 5–300), `MaximumMegabytes` (default 128, 16–1024), and optional `OutputDirectory` (default session folder).
- `string SaveReplay()`: save buffered content to MKV, wait for mux completion and return the actual path. Run on worker, show progress/busy UI. Replay and recording share the quality encoder to avoid duplicate GPU encoding.
- `GetOutputStatus()` returns `OutputStatus`: `Streaming`, `ReplayBufferActive`, `StreamBytes`, `StreamDroppedFrames`, `LastStreamError` (redacted). Poll on the same worker.
- `SetPaused(bool)` is supported only for recording without streaming/replay; reject pause in combined sessions, because libobs encoder pause is shared.

## Composition

`RecorderOptions.Overlays` is a `List<SceneOverlay>`. `SceneOverlay` has `Id`, `Kind` (`OverlayKind.Camera`, `Image`, `Text`), `DeviceId`, `FilePath`, `Text`, `FontSize` (default 32), `TextColor` (RGB value default 0xFFFFFF), and `Transform` (`SourceTransform`). `SourceTransform` has `X`, `Y`, `Width`, `Height`, `Rotation`, `Visible` (default true); coordinates are output-canvas pixels and aspect ratio is retained inside the bounds.

`AddOverlay(SceneOverlay)`, `UpdateOverlay(string id, SourceTransform)`, `RemoveOverlay(string id)` operate on an active session. `GetCameraDevices()` returns `CaptureDevice[]` with `Id`, `Name`; it loads only the DirectShow camera module and requires an active session. A configured camera must match an enumerated ID. Missing/blocked cameras fail explicitly. No camera audio is opened; microphone selection remains separate. Text and image paths are local. `UpdateCaptureTransform(SourceTransform)` adjusts the base display/window item.

## Audio

`RecorderOptions.SystemAudioSettings` and `.MicrophoneSettings` are `AudioMixSettings`: `Volume` linear 0–2 (default 1), `Muted`, `NoiseSuppression`, `NoiseGate`, `Compressor`, `Limiter`. Initial audio source enable/device fields remain unchanged. `SetAudioControls(AudioChannel.System|Microphone, AudioMixSettings)` updates gain/mute and rebuilds selected filters. An input disabled at session start cannot be enabled by this method.

`GetAudioLevels()` returns `AudioLevels` with `SystemPeakDb`, `MicrophonePeakDb` (silence −96 dB). Metering is native sample-peak at low update frequency; polling does not capture extra audio. Filters use libobs built-in noise suppression, noise gate, compressor, limiter with upstream defaults; suppression uses Speex for lower CPU cost.

## Encoding options

`RecorderOptions.Quality` (default 23, 14–35) controls CRF/CQP. `VideoBitrateKbps` (default 0) selects CBR when nonzero (500–100000). `AudioBitrateKbps` (default 160, 64–320). H.264/AAC/MKV are supported; use the existing explicit remux exporter for MP4. HEVC/AV1, HDR, browser/game/VST sources, transitions and multi-track audio are not claimed by this increment.

UI must only enable operations after successful engine return. `GetOutputStatus` cleans up an output that disconnected; the capture session remains active until the UI calls `Stop`. If native stop times out, keep the UI in a cleanup/retry state while `IsSessionActive` remains true.

## Validation on 22 October 2026

Built the isolated .NET 10 x64 harness against the selected app-local OBS 32.2.2 runtime, with native helpers beside the executable. No installed OBS lookup, managed helper relay, Qt, CEF or browser source is used. Machine: Ryzen 3 3200G / Radeon Vega 8, driver 27.20.1034.6, Windows 10 build 19045. A separate existing user recording remained running, so these observations are not a controlled comparison against OBS.

- `tools/engine-features-smoke.ps1 -Mode CompositionReplay`: native image/text overlays, transform/visibility updates, source removal, four audio filters, replay-only capture, recording + replay sharing one encoder, independent recording stop, repeated replay save, and pause/resume after replay stops passed. A decoded frame confirmed the green image and white title placement. Four files decoded fully with FFmpeg, H.264 1280×720 + AAC; replay clips were 3.050 and 4.966 seconds.
- `-Mode Streaming`: a local FFmpeg RTMP listener on 127.0.0.1:19359 received the stream. No external endpoint was contacted. Streaming-only, then simultaneous streaming + recording + replay, independent recording stop and full session teardown passed. Received clip 5.439 seconds; simultaneous recording 2.986 seconds and replay 2.751 seconds. All fully decoded without errors. The native RTMP connection is asynchronous: startup waits for transmitted bytes or the stop signal, rather than treating the initial inactive flag as failure.
- `-Mode Failure`: a refused localhost connection returns an error, releases streaming resources, leaves the session usable, and a subsequent recording succeeds. Fake stream keys were absent from both successful and failed stream logs. Server/key/password/user values are redacted from native log messages; connection fields are held in memory only.
- `-Mode Audio`: a quiet generated loopback tone measured −36.33 dB sample peak; volume 0.1 measured −56.33 dB. Decoding the saved audio yielded −39.37 dB RMS, then −59.37 dB RMS, then digital silence in the muted interval. Gain/mute updates avoid rebuilding an unchanged filter chain. Native meter callbacks allocate no arrays; the getter reports −96 dB for a muted or stale channel.
- `tools/engine-smoke.ps1 -Mode WindowCloseStats -Seconds 2 -Runs 1 -SkipPause`: the window-close finalization regression still passes after independent-output lifecycle changes (72 preserved final frames, zero capture/output and encoder skips in the sampled interval).
- Native camera enumeration returned zero devices. The missing-camera rejection path was tested; physical camera capture has not been verified on this machine. RTMPS is supported by the pinned native RTMP module but has not been tested against a TLS endpoint. No external streaming credentials were used.

`tools/engine-features-performance.ps1 -Seconds 60` recorded an owned animated 800×450 test window into 1280×720 at 30 FPS using AMD AMF. Across 24 steady two-second samples, the **console engine test host plus its native mux helper**, excluding the WPF front end, averaged 3.38% CPU of the four-core machine (maximum 5.32%), with mean 97.65 MiB and maximum 98.78 MiB working set. These are not full-app memory/CPU figures. The target window process, DWM, drivers, unrelated processes and the pre-existing user recording are also excluded. Startup took 2254 ms. The finalized file was 61.439 seconds / 2,266,209 bytes, H.264/AAC at 30 FPS, and fully decoded without errors. Native final frame count was 1844. The run observed 19 rendering-lag frames and 19 video-output/encoder skipped frames; these counters can overlap and must not be added. Most sampled render times were 1–2 ms, with spikes up to 8.9 ms and active FPS down to 23.2 during competing desktop work. This run does not establish zero-drop recording or comparative performance superiority. Raw evidence: `artifacts/engine-features/performance-summary.json`, `performance-samples.csv`, `performance-run.txt`.

The replay budget is a compressed-packet target, not a strict process-memory ceiling: keyframe boundaries and a concurrent save can temporarily add memory. Session startup and output stop wait loops are bounded, but the native RTMP connect-thread join and mux/output destruction can still block in a hung driver/network/helper. UI must preserve retry state; a future isolated engine process is needed for a hard wall-clock termination guarantee. Stream reconnect is deliberately disabled, so a disconnection is reported and does not silently transmit again.

The API currently supports one composed canvas and H.264/AAC. It does not implement OBS scene collections/transitions, game/browser sources, VST, HDR, multi-track audio, HEVC/AV1 selection, virtual camera or scheduled streaming. Module availability alone is not feature validation.

Verified upstream ABI/settings references: [obs.h](https://github.com/obsproject/obs-studio/blob/32.2.2/libobs/obs.h), [audio controls](https://github.com/obsproject/obs-studio/blob/32.2.2/libobs/obs-audio-controls.h), [RTMP output](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/obs-outputs/rtmp-stream.c), [custom RTMP service](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/rtmp-services/rtmp-custom.c), [replay buffer](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/obs-ffmpeg/obs-ffmpeg-mux.c), [DirectShow source](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/win-dshow/win-dshow.cpp), [device IDs](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/win-dshow/encode-dstr.hpp), [text source](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/obs-text/gdiplus/obs-text.cpp).
