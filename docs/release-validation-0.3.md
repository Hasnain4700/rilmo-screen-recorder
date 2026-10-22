# Rilmo Recorder 0.3 validation

Local evidence, 22 October 2026. Ryzen 3 3200G, integrated Vega 8, Windows 10 build 19045. Tests use the actual bundled native engine and WPF implementation; they do not establish universal compatibility or Store certification.

## Integrated application

`tools/ui-smoke.ps1 -IncludeBrowser` creates an instrumented executable. The unattended recording switch is excluded from ordinary builds. It captures local content and writes `artifacts/ui-smoke`.

Passing run: `artifacts/ui-smoke/results-20261022-001900-663/result.json` (UTC directory stamp). All eight resulting MKV/MP4 files, including replay and both browser sessions, decoded completely with FFmpeg `-xerror`; details are in that run's `decode-validation.json`.

- Record/pause/resume/finalized stop and MP4 remux.
- Independent replay/file lifecycle; pause disabled while outputs share the encoder.
- Invalid stream rejection and key absent from serialized preferences.
- Missing-display guard, stale-preview invalidation, app-local runtime and lazy loading.
- Scene roundtrip, malformed canvas rejection and native one-shot preview cleanup.
- Live text edit, fade switching and continued recording.
- Normal/compact main and scene editor layouts, source dialog and floating menu renders.
- Two browser recordings through the actual WPF executable, native page pixels, unique host IDs and clean stop; no CEF/Qt in the main UI.
- Floating-menu accessibility/focus, screenshot library and capture-exclusion calls.
- Reduced/disabled-motion paths and capture-active animation suppression.
- Unregistered virtual-camera controls explain setup and reject starting capture before registration.
- Importing a portrait collection during landscape recording retains the native session dimensions through later edits and shared outputs. A subsequent fresh session adopts the new canvas ratio.
- Copying/deleting a selected scene updates the persisted selection; actual export/readback passes.

An earlier passing run (`results-20261021-233826-364`) sampled 109.47 MiB idle and 165.05 MiB during recording for the WPF process only. These snapshots exclude helper processes and are not resource limits.

Initial testing exposed a first-frame scene timing bug; preview now waits for actual video ticks with a two-second bound. Visual review caught clipped text fields and missing large layout-map text; field templates and static vector text drawings corrected them. Temporary native preview now disables microphone/system audio.

## Native tests

`tools/engine-features-scenes.ps1`: color/image/text, order/visibility/crop, deep clones, atomic rejection, rapid fades, selected/inactive window closure and decoded frame colors. See [scene contract](scene-engine-contract.md). Repeated synchronous preview stress is functional evidence, not a low-load benchmark.

`tools/advanced-sources-smoke.ps1`: generated local HTML/heartbeat server and owned animated D3D11 target. Three isolated browser sessions passed actual pixels, inactive timer cessation/resumption, recording decode and helper cleanup. Direct in-process CEF restart previously crashed; production uses fresh isolated hosts. [Browser/game documentation](browser-and-game-sources.md) records networking and compatibility limits.

The isolated-client harness tested HWND serialization, preview, scene edits, recording/replay/pause, native errors, final stats, crash/restart and parent-only crash. Parent pipe EOF finalized a fully decodable 2.069-second recording and cleaned helpers. Killing the engine host itself after 800 ms left a zero-byte file: a retained path is diagnostic, not a recovery guarantee.

Short recordings joining a running replay encoder exposed missing initial keyframes. Start now waits for actual encoded output before reporting ready, with a five-second limit; startup failure cleans only the new file output. Six immediate/one-second stop cases across AMD AMF and x264 preserved replay and produced decodable recordings. All eight files, including two replay saves, decoded at `artifacts/short-recordings/results-20261022-001642-473`. See [short recording validation](short-recording-validation.md).

Virtual camera: `artifacts/virtual-camera-engine/6705c684545944ada81834f8e5759fdc/result.log`. Real libobs NV12 frames reached independent x64/x86 DirectShow consumers without registry installation. Both consumed 45 live frames without stopped placeholders. Duplicate start, restart, stop and session cleanup passed. Separate filter tests cover padded 642-pixel frames and clean unload. No Windows device registration was performed.

## Scene performance

`artifacts/scene-performance/results-20261022-043419/summary.json` and samples: three scenes, shared window source, two fades, 720p30 AMD AMF. Full 46.421-second/1392-frame recording decoded, with native zero lag/skip counters. Console client plus mux only (WPF/target/DWM/drivers excluded): CPU mean2.81%/max5.68% of all four cores, working set mean111.17/max111.89MiB, mean render1.294ms, startup2236ms. User's pre-existing recorder was left untouched; no competing owned capture was running. This is not a comparison against OBS or an endurance test.

## Published build

`tools/validate-published.ps1` passed for `dist/Rilmo Recorder standalone-20261022-052151` at `artifacts/published-validation/20261022-052519/result.json`. It verified 1,108 core files, 280 browser files and both camera DLLs against their inventories, then rendered Record, Scene studio and virtual-camera controls using the production apphost. Each process loaded its own bundled `coreclr.dll` with external .NET roots disabled and PATH limited to Windows System32. The folder was approximately 544 MiB before later documentation/provenance refreshes. This is app-local dependency evidence, not a substitute for a clean Windows machine.

The root launcher selects this completed publish; the previous running build is preserved. No user recording or installed OBS application was stopped.

## Remaining qualification

No physical webcam, external RTMPS service, third-party meeting client, elevated camera install/uninstall, clean-machine package installation, Store certification, NVENC/QSV hardware, hybrid GPU, HDR, multi-hour endurance or complete accessibility audit was available. Earlier Computer Use screenshot/pointer geometry was unavailable; rendered-layout/in-app tests are not a manual pointer/DPI qualification.

Account details and release gates are tracked by packaging checks. No public source repository or Microsoft Store submission was created.
