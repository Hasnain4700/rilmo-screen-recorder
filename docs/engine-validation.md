# Recording engine validation

**Historical first-pass evidence:** installed-runtime/managed-relay descriptions below refer to the original implementation and are retained with their test results. The current 0.2 build uses app-local native libraries/helpers; see engine-v2-contract.md and standalone-runtime.md. Current UI integration results are under artifacts/ui-smoke and include independent replay, screenshots and bundled-runtime checks.

Validated locally on 22 October 2026. These are functional checks and short observations on one machine, not a comparative benchmark or a claim of performance superiority over OBS.

## Runtime and machine

- Windows 10, build 19045; AMD Ryzen 3 3200G, four cores/four threads; approximately 14 GiB usable RAM.
- AMD Radeon Vega 8; driver 27.20.1034.6.
- Installed OBS runtime: `C:\Program Files\obs-studio`, `obs.dll` file/product version **32.2.2**. No OBS UI process is launched.
- Standalone harness: x64 .NET Framework compiler. Current application integration: **.NET 10.0.13 Windows apphost**, built with SDK 10.0.402. Earlier integration checks used .NET 8; the current supported-LTS build passed again. The application must launch through its own `.exe`; launching its DLL through `dotnet.exe` gives native OBS helpers the wrong executable directory and is explicitly rejected.

The engine directly uses the libobs C ABI. It loads `win-capture`, `win-wasapi`, `obs-ffmpeg`, and `obs-x264`; optional `obs-nvenc` and `obs-qsv11` perform capability checks. AMD AMF is registered by `obs-ffmpeg`. Tests inspected loaded process modules and found no Qt, CEF, or OBS browser module. Construction and Win32 window discovery do not initialize libobs. Stop releases the output, encoders, sources, scene, and entire OBS context.

## Confirmed behavior

| Check | Result and evidence |
|---|---|
| Display capture | Two consecutive recordings on the same engine instance, each with pause/resume and stop, produced decodable H.264/AAC MKV files. |
| AMD hardware encoding | `h264_texture_amf`; CQP 23, speed preset, no B frames, pre-analysis disabled; NV12 GPU textures. |
| Software fallback | Explicit `software` selected `obs_x264`, CRF 23, ultrafast, two encoding threads. Recording and pause/resume completed. |
| Capture compatibility | On this driver, DXGI duplication initially returned `0x887A0004`. Automatic fallback to Windows Graphics Capture succeeded. The working WGC selection is remembered for subsequent starts on that engine instance. |
| System audio | The resulting AAC stereo stream was 48 kHz. Existing system-audio recording measured mean -14.3 dB and peak -0.7 dB with FFmpeg `volumedetect`. |
| Audio disabled | No WASAPI capture source is created. The muxer still receives a silent AAC stream, because this OBS muxer requires an audio encoder; measured mean/peak -91.0 dB. This is a silent stream, not removal of the audio track. |
| Microphone enabled | Both system and microphone sources initialized, recorded, paused, and stopped. This confirms device capture initialization, not a controlled spoken-voice quality or synchronization test. |
| Window capture | Recorded a separate controlled 800 x 450 test window through WGC. Its title included both `#` and `:`. The decoded 1280 x 720 frame contained exactly the window surface. |
| Region capture | Recorded display region `(260,190,320,180)` against the controlled window. The decoded frame showed the expected 75% orange / 25% blue split and correct text crop. |
| Window close regression | Closing the test window then calling `Stop` exercised `Stop → GetStats → Stop`. A second test exercised `GetStats` first. Both finalized, preserved 71 output frames, and tolerated another `Stop`. Both complete files decoded without errors. |
| Cleanup | No owned test window, `EngineSmoke`, or OBS mux/probe helper process remained after the completed checks. |

All inspected recordings were H.264, 1280 x 720, 30 fps, with AAC. Display pause/resume recordings measured 4.333 and 4.233 seconds. Window/region recordings measured 4.181 and 4.202 seconds. Window-close regression files measured 2.346 and 2.282 seconds. Full-file FFmpeg decode returned no errors. The pause interval was excluded from recorded duration.

Short standalone observations, including other applications running on this machine:

| Workload | Process working set | Process CPU / all logical cores | Observed render time |
|---|---:|---:|---:|
| Initial display capture, AMD GPU, two runs | 66–76 MiB | 2.62%, 3.20% | approximately 1–2.5 ms steady state |
| Display, x264 ultrafast | 74–75 MiB | 5.21% | approximately 1 ms |
| Controlled static window, AMD GPU | 62 MiB | 2.15% | 1.4–3.0 ms |
| Controlled static region, AMD GPU | 61 MiB | 1.41% | approximately 0.9–2.0 ms |

CPU is the harness process's processor-time delta divided by elapsed wall time and logical-core count. It excludes child helper processes, GPU utilization, other processes, and driver work outside the process. Working set is sampled process memory, not system or dedicated GPU memory. Content differs between tests; these numbers must not be presented as an apples-to-apples comparison. Startup in the observed runs ranged approximately 2.1–4.1 seconds, including initialization and any capture fallback. There is no long-duration or minimum-spec certification yet.

## Reproducing the functional checks

### Full application integration

`tools/ui-smoke.ps1` passed on the .NET 10 build: idle engine loading was absent, AMD H.264 recording started, pause/resume succeeded, Stop finalized the MKV, and the same process exported an MP4. No Qt/CEF modules were loaded. The evidence is `artifacts/ui-smoke/results-20261021-214137-410/result.json`; the UTC directory timestamp is the previous date relative to the local Pakistan date. Observed working set was 103.4 MiB idle and 153.2 MiB during recording, including test snapshot allocations. These are point samples from one short functional run, not a comparative performance benchmark. Native-library checks only establish the modules observed in this tested flow.

After the final UI state fixes, `artifacts/ui-smoke/results-20261021-215215-858/result.json` passed again, including a missing display-selection rejection before native initialization and clearing a stale preview. Changing quality while no display is selected preserved the original display identity. This synthetic selection test does not replace a physical monitor-hotplug test. The earlier .NET 10 MP4 was fully decoded with FFmpeg without errors: H.264 1280×720/30 fps and AAC 48 kHz stereo, 4.138 seconds.

Run from the repository root with the installed OBS 32.2.2 x64 runtime:

```powershell
.\tools\engine-smoke.ps1 -Seconds 3 -Runs 2
.\tools\engine-smoke.ps1 -Seconds 3 -Runs 1 -Encoder software -NoAudio
.\tools\engine-smoke.ps1 -Seconds 3 -Runs 1 -Microphone
.\tools\engine-smoke.ps1 -Seconds 3 -Runs 1 -Mode Window -NoAudio
.\tools\engine-smoke.ps1 -Seconds 3 -Runs 1 -Mode Region -NoAudio
.\tools\engine-smoke.ps1 -Seconds 2 -Runs 1 -Mode WindowClose -NoAudio
.\tools\engine-smoke.ps1 -Seconds 2 -Runs 1 -Mode WindowCloseStats -NoAudio
```

These commands actually record the screen or an owned test window. They save local files under `artifacts/test-recordings`. The window and region modes show a temporary controlled test window. The final two modes close it during recording to test cleanup. Logs are under `%LOCALAPPDATA%\Rilmo Recorder\Logs`. Sample decoded frames are `artifacts/engine-smoke/window-validation.png` and `region-validation.png`; desktop frames and recordings are local test evidence and should not be published with the source.

Validate each output using a local FFmpeg toolchain, not a player merely opening the file:

```powershell
ffprobe -v error -show_entries 'format=duration,size:stream=index,codec_name,width,height,r_frame_rate,sample_rate,channels' -of json 'recording.mkv'
ffmpeg -v error -i 'recording.mkv' -f null -
ffmpeg -hide_banner -i 'recording.mkv' -vn -af volumedetect -f null -
ffmpeg -v error -ss 2 -i 'recording.mkv' -frames:v 1 -update 1 'frame.png'
```

## Public contract and source references

`RecorderEngine.Start`, `Stop`, `SetPaused`, `GetStats`, and `Dispose` run sequentially on one dedicated worker thread. `RecorderOptions` selects monitor, output dimensions/frame rate, cursor, system/microphone inputs and optional device IDs. `WindowHandle != IntPtr.Zero` selects a window; otherwise `MonitorIndex` selects a display. Crop coordinates are physical pixels relative to the source, not desktop coordinates; zero crop width and height mean the full source. A scene item applies the crop and fits the result into the output while preserving aspect ratio. `GetCaptureWindows()` uses Win32 discovery without loading the capture runtime.

Window selectors follow upstream `title:class:executable` encoding (`#` → `#22`, `:` → `#3A`). The adapter rejects ambiguous window titles and verifies the native lookup returns the requested handle. Minimized or closed windows are rejected at start. A window closed or minimized during recording is detected on the next statistics poll and triggers finalization. Protected, elevated, cloaked, UWP-hosted, or inaccessible windows may be rejected; display capture is the available fallback. Only the ordinary controlled-window case was validated here.

`TotalFrames` is the output's frame count. `DroppedFrames` currently combines rendering-lag delta with the output plugin's dropped-frame counter; the local FFmpeg muxer normally reports zero output drops. `EncoderSkippedFrames` separately reports libobs video-output skipped frames relative to the successful output attempt. These counters describe different stages and must not be summed into a unique percentage of lost frames. `ActiveFps` and `RenderMilliseconds` describe the libobs graphics loop, not decoded-file fps or total encoding latency. `OutputBytes` is muxer-reported encoded data and can differ from the finalized file size due to container overhead.

ABI and settings were checked against official, version-pinned source:

- [libobs public API and scene-item crop](https://github.com/obsproject/obs-studio/blob/32.2.2/libobs/obs.h)
- [Video-output skipped-frame counter](https://github.com/obsproject/obs-studio/blob/32.2.2/libobs/media-io/video-io.h)
- [Display capture settings and DXGI/WGC selection](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/win-capture/duplicator-monitor-capture.c)
- [Window capture settings](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/win-capture/window-capture.c)
- [Window-selector encoding and lookup](https://github.com/obsproject/obs-studio/blob/32.2.2/libobs/util/windows/window-helpers.c)
- [AMF encoder IDs/settings](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/obs-ffmpeg/texture-amf.cpp)
- [WASAPI source types and device lists](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/win-wasapi/win-wasapi.cpp)
- [FFmpeg muxer lifecycle and helper invocation](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/obs-ffmpeg/obs-ffmpeg-mux.c)

## Packaging and remaining limits

The current runtime is a development dependency on the user's existing OBS installation. `MuxRelay.cs` is our small managed bridge, compiled beside the application under four names: `obs-ffmpeg-mux.exe`, `obs-amf-test.exe`, `obs-nvenc-test.exe`, and `obs-qsv-test.exe`. It forwards to the installed OBS helpers because upstream resolves their location relative to the host executable. No installed OBS binary was copied into the source tree. The probe helpers exit after checking capabilities; the mux bridge and native mux process run only while finalizing/recording. The bridge adds process and encoded-byte-copy overhead. A distributable build should reproducibly build and package its licensed native runtime and helpers in the correct layout, eliminating the bridge and external-install dependency. This has not been completed or Store-certified.

Graceful stop polls native output activity for 15 seconds, then requests a forced stop and polls for another 5 seconds. A forced stop is reported as unconfirmed completion. **The subsequent native output destruction and helper-process join are not bounded by a managed timeout.** A blocked filesystem, faulty driver, or hung native muxer can therefore leave finalization waiting beyond those intervals. Normal stop/pause/restart and window-close cases passed; disk-full, unplugged-drive, process-crash, GPU-reset, and native-helper-hang recovery remain untested. MKV reduces the damage from interruption but is not a guarantee of successful finalization. Release work should isolate native recording in a supervised worker process with an explicit completion protocol before claiming bounded shutdown.

This scope does not implement webcam composition, scenes saved by the user, transitions, filters, game injection, browser sources, streaming, replay buffers, audio mixing controls, or complete OBS feature parity. NVIDIA/Intel encoder paths use upstream IDs but were not exercised on this AMD-only machine. Long recordings, multi-monitor/DPI configurations, resolution changes during capture, audio sync, multiple GPUs, thermal behavior, battery impact, and output quality need dedicated validation before release.
