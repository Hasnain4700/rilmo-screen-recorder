# Scene engine contract and validation

The scene layer uses libobs 32.2.2 directly. It does not invoke the OBS frontend. All instance operations, including previews and reads, belong on the existing EngineWorker. The native canvas and encoder dimensions remain fixed for the duration of a session.

## Models and operations

`RecorderOptions.SceneCollection = null` retains the simple display/window/region canvas and legacy overlays. A non-null collection replaces that canvas and its legacy overlays. System/microphone audio controls remain global to the session; individual visual scene sources never open an implicit microphone or route browser audio.

- `SceneCollection`: `CanvasWidth`, `CanvasHeight` (reference dimensions for persistence/UI), `Scenes`, `SelectedSceneId`.
- `SceneDefinition`: `Id`, `Name`, `Sources`. Sources are ordered **back to front**, with index zero at the bottom.
- `SceneSource`: `Id`, `Name`, `Kind`, `Transform`, `Crop`, and source settings. Kinds: Display, Window, Game, Browser, Camera, Image, Text, Color.
- Capture settings: `MonitorIndex`, `MonitorIdentity`, `WindowHandle`, `WindowTitle`, `WindowClass`, `WindowExe`, `CaptureCursor`, `GameCaptureMode` (currently `window` only), `DeviceId`.
- Asset settings: `FilePath`, `Text`, `FontSize`, `TextColor` (RGB), `Color` (ARGB), `BrowserUrl`, `BrowserWidth`, `BrowserHeight`, `BrowserFps`. A nonempty browser `FilePath` selects local HTML instead of URL.
- `SourceTransform`: `X`, `Y`, `Width`, `Height`, `Rotation`, `Visible`. Images/video fit inside the bounds while preserving aspect ratio. Solid color layers fill the bounds as rectangles.
- `SourceCrop`: `Left`, `Top`, `Right`, `Bottom`, measured in **original source pixels**, before fitting/rotation.

`WindowHandle` is `[JsonIgnore]` for persistent documents. The UI resolves current windows using exact title/class/executable metadata and resolves monitor identity before passing a collection to the engine. IPC must explicitly preserve live HWND values separately; a default JSON round trip intentionally loses them.

The engine exposes:

```csharp
static void ValidateSceneCollectionDefinition(SceneCollection collection,
                                             bool checkAvailability = true);
void SetSceneCollection(SceneCollection collection);
SceneCollection GetSceneCollection(); // deep clone; null for legacy canvas
string SelectedSceneId { get; }
void SwitchScene(string id, SceneTransition transition = SceneTransition.Cut,
                 int durationMs = 300);
void AddScene(SceneDefinition scene);
void RemoveScene(string id);
void AddSceneSource(string sceneId, SceneSource source);
void UpdateSceneSource(string sceneId, SceneSource source);
void RemoveSceneSource(string sceneId, string sourceId);
void MoveSceneSource(string sceneId, string sourceId, int index);
void SetSceneSourceTransform(string sceneId, string sourceId, SourceTransform transform);
void SetSceneSourceCrop(string sceneId, string sourceId, SourceCrop crop);
ScenePreviewFrame CapturePreview(int maxWidth = 640, int maxHeight = 360);
```

`ScenePreviewFrame` contains `Width`, `Height`, `Stride`, and `BgraPixels` (packed BGRA8). Preview captures the actual program transition, not a schematic layout. It is a synchronous GPU readback with no background timer. Request frames deliberately; repeated readbacks can stall rendering. The UI must discard results from obsolete source revisions.

After source/scene edits, preview waits for two real native video ticks (bounded to two seconds) so an immediate first preview sees initialized scene geometry/show callbacks. This is not a guarantee that a remote browser page has finished loading.

## Validation and lifecycle

Managed validation does not initialize libobs. It bounds collections to 1–16 scenes, 32 layers per scene and 128 layers total, checks unique IDs, names, finite transforms, crop ranges, canvas dimensions (16–8192 per side), text settings, browser settings, and camera device IDs. `checkAvailability=false` permits disconnected windows/displays and missing files in saved/imported libraries. Apply/Start additionally checks availability and actual native source creation/readiness.

`SetSceneCollection` creates a replacement graph before changing program output. Missing images, invalid devices, unsupported capture, or other creation failures release the incomplete graph and leave the previous graph running. Unchanged source references are retained. Identical camera/display/window/game inputs are shared across scenes to avoid opening duplicate capture devices; each scene item keeps independent geometry. Visible shared inputs remain showing across a graph swap. `GetSceneCollection` returns independent nested lists, transforms, and crops.

The fade source remains the program output, including for cuts. A new fade retargets according to libobs' transition behavior. The recording/stream/replay encoders and output dimensions do not restart during a scene switch. Hidden sources follow native showing state. The window capture source stops its normal tick when hidden; this does not prove that every underlying Windows Graphics Capture allocation is released. Monitor capture explicitly releases capture data while hidden. Camera settings request deactivation while hidden. Browser shutdown while hidden is configured by the separate advanced-source implementation. Image assets currently stay resident until their source is removed or the session stops, favoring predictable transitions over unloading/reloading them on every switch.

All configured capture sources are validated when applying a collection, including inactive scenes. Creating camera/game/window/display sources briefly warms them to verify availability; inactive browsers are not warmed with a showing reference. Browser dimensions alone do not prove page readiness. Selecting an unavailable window scene fails before the active scene changes. If a visible selected or transitioning window closes/minimizes, the next `GetStats` stops active outputs, preserves finalized recording counters, and returns a clear error; inactive windows do not stop another scene's recording.

Stop ends virtual camera before stopping stream/replay/recording and releasing scenes. A failed output shutdown retains the graph for retry. Existing native mux/RTMP teardown joins remain unbounded; this layer does not claim crash-proof completion. Browser lifetime isolation is documented separately and must be used by the product facade because upstream CEF cannot safely restart after shutdown in the same process.

## Functional evidence (2026-10-22)

Run `tools/engine-features-scenes.ps1` for isolated native tests; `-Mode Validation` checks managed validation without opening capture, and `-Mode WindowClose` uses only a test-owned window. Harness output is under `artifacts/engine-scenes/out`; recordings stay under `artifacts/test-recordings/scenes`.

Two complete native color/image/text sessions passed:

- Real red/blue scene cuts, a one-second fade, and a second fade while the first was running.
- Recording and replay stayed active through scene/source edits; both finalized successfully.
- Preview pixels checked cropped image content, ordering, visibility, and translated bounds.
- Text content replacement, scene add/remove, source add/remove, deep-clone isolation, missing-image rollback, and native unavailable-camera rollback passed.
- Both recordings and both replays fully decoded with FFmpeg, H.264 + AAC at 640×360. Recording durations were 7.530s and 4.757s; replay durations were 3.967s and 4.159s.
- Decoded center pixels (not only preview) contained red, blue, and intermediate fade frames: first recording 225 decoded frames, including 24 mixed frames; second recording 142 frames, including 23 mixed frames.

The first stress run reported 67 render-lag frames and the second reported 3. These runs repeatedly read back GPU previews and edited sources amid other desktop work. They are functional validation, not an isolated performance comparison or a zero-drop claim. Earlier console engine/mux benchmark figures exclude the WPF frontend, browser, scene editor, virtual camera, driver/DWM, and competing applications.

Two additional owned-window sessions passed: actual WGC pixels matched the target (RGB255,140,0); closing the selected target stopped/finalized output and preserved stats on repeated Stop; closing an inactive target rejected both Switch and Apply while the active red scene kept recording, and removing the missing scene restored an editable valid collection. The owned target processes were closed; unrelated running recordings were untouched.

The follow-up regression run also passed immediate preview with no caller delay and a thin solid-color rectangle pixel check; it produced a valid recording/replay and reported one render-lag/encoder-skipped frame. Subsequent hardware input sharing preserves a single native capture reference across matching window/display/game/camera layers in different scenes.

## Browser process boundary checks

`tools/isolated-engine-smoke.ps1` exercises the `RecorderClient` facade and authenticated local `RecorderHost` pipe using local HTML only. Artifacts from the completed test are under `artifacts/isolated-engine/results-20261022-042741`:

- Ordinary color scenes stayed in-process without Chromium/Qt modules.
- Two browser sessions used different host PIDs. Native HWND values round-tripped through the IPC-specific JSON contract, including the same owned window in two scenes.
- Real green HTML pixels, cuts/fades, recording, replay, pause/resume, validation failure with outputs preserved, and final stats/output path after host shutdown passed.
- Both completed recordings (3.093s, 3.050s) and both replays fully decoded. Each host and its Chromium helpers exited; no Chromium/Qt module loaded in the parent.
- Deliberately terminating the owned host was reported as failure, cleared active state, preserved the path for inspection, and allowed a new isolated session. The interrupted 800ms recording was **zero bytes**. Abrupt native-host failure can lose buffered recording data; path retention does not promise a recoverable video.

`tools/isolated-engine-smoke.ps1 -ClientCrashOnly` separately killed the test-owned UI/client process. The host saw pipe EOF, finalized a 2.069s recording (full decode passed), exited and removed its browser helpers. Evidence is under `artifacts/isolated-engine/results-20261022-043117`. This successful parent-disconnect path does not guarantee recovery after host/driver/power failure.

Isolated hosts use separate per-session Chromium configuration directories to avoid concurrent-cache locks. The native-host cleanup job owns only its descendant processes. The facade treats lost recording finalization as an error; a later no-op StopRecording cannot turn a host-crashed file into a confirmed save.

The focused `-HostCrashOnly` regression subsequently verified that calling `StopRecording` after an actual host termination throws instead of falsely confirming a save; the path remains available across cleanup, a new session resets the error, and a new browser page renders successfully. Evidence: `artifacts/isolated-engine/results-20261022-043656`.

## Limited three-scene resource measurement

`tools/engine-features-scenes-performance.ps1 -Seconds 45` measures a console RecorderClient and its native mux helper. It intentionally excludes WPF, the separate animated test window, DWM, graphics drivers, and unrelated applications. The test-owned animation, capture, and mux processes exited afterward. Other owned capture tests were paused; the user's existing application (PID4380) was left running, with no unrelated mux/CEF process present at the checks. This is a simple synthetic-window workload on the Ryzen3 3200G/Vega8 machine, not gameplay, a matched OBS comparison, or a resource guarantee.

Evidence: `artifacts/scene-performance/results-20261022-043419/summary.json` and `samples.json`.

| Measurement | Result |
| --- | --- |
| Constructed idle client, 10 seconds | No libobs module initialized; mean27.12MiB working set, mean0.47% CPU including measurement instrumentation |
| Native capture/record startup | 2,236ms |
| Encoder/output | AMD `h264_texture_amf`, 1280×720, 30fps; audio sources disabled |
| Recording CPU (41 steady samples) | Mean2.81%, max5.68%, normalized across all four cores |
| Recording working set | Mean111.17MiB, max111.89MiB, client + mux sum |
| Mean native render time | 1.294ms |
| Native render-lag/output-drop and encoder-skip counters | 0 and0 in this run |
| Final file | 46.421s, 1,392 decoded video frames, 480,190 bytes; full decode passed |

Three scenes were used for roughly15seconds each: full animated window, the same window fitted beside a title, and a static slate. Two300ms fades occurred during recording. The native log contains one Window source creation shared by the first two scenes. No browser/frontend modules loaded. Native finalized-output counters and decoded-frame counts can differ by one at the mux boundary; the decoded file is the validation source for duration/frame count.

Primary ABI/behavior references: [scene/transition API](https://github.com/obsproject/obs-studio/blob/32.2.2/libobs/obs.h), [transition implementation](https://github.com/obsproject/obs-studio/blob/32.2.2/libobs/obs-source-transition.c), [scene implementation](https://github.com/obsproject/obs-studio/blob/32.2.2/libobs/obs-scene.c), [upstream screenshot rendering](https://github.com/obsproject/obs-studio/blob/32.2.2/frontend/utility/ScreenshotObj.cpp), [camera show/hide callbacks](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/win-dshow/win-dshow.cpp), [monitor capture lifecycle](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/win-capture/duplicator-monitor-capture.c).
