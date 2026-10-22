# Rilmo Recorder 0.4 feature coverage

Reviewed 22 October 2026 against OBS 32.2.2. The requested scene, transition, game/browser and virtual-camera implementations are present. This is a standalone development build, **not full OBS parity or a certified Microsoft Store release**.

| Task | Interface and behavior | Evidence / limit |
| --- | --- | --- |
| Display/window/area | Explicit source selection, pointer toggle, area picker, missing-target guards | Native display and owned-window recordings; broader DPI/monitor/GPU matrix pending |
| Recording | H.264/AAC MKV, GPU preference/CPU fallback, presets, countdown, pause/resume, finalized stop | AMD AMF and x264 exercised; NVENC/QSV need their hardware |
| MP4 export | Lossless remux with cancellation; preserves MKV | Actual files fully decoded |
| Streaming | Independent RTMP/RTMPS controls; key stays in memory and is redacted | Local RTMP tested; external services/RTMPS not qualified |
| Replay | 5–120 seconds / 128 MB, save clip, independent stop | File/replay coexist and share quality encoder |
| Scenes | Named scenes, duplicate/remove, sources, drag/resize, numeric transform/crop, visibility, order and nested scenes | Native pixels, live edits, atomic rollback; nested references validated against cycles and limited to eight levels |
| Scene files | Atomic persistence; JSON import/export with canvas dimensions | Roundtrip and malformed/offline-document validation; Rilmo format, not OBS migration |
| Transitions | Cut, Fade, Swipe, Slide and Fade to black; quick picker uses 300 ms Fade | Native endpoint pixels and recording continuity; custom stingers remain unsupported |
| Preview | Lightweight layout map; requested native frame; temporary preview excludes audio | First-frame readiness and graph cleanup tested |
| Game source | Explicit game window, limited FPS, cursor choice, app-local hook | Animated Direct3D 11 target and decoded recording; no Vulkan setup/fullscreen auto-selection/anti-cheat variant/universal compatibility claim |
| Browser source | URL/local HTML, size/FPS, page-control disabled, muted audio, inactive-page shutdown | Three native sessions and two actual WPF-host cycles with expected pixels and helper cleanup |
| Browser process | Fresh authenticated current-user host; per-host temporary profile | EOF finalization, host-crash cleanup/restart; abrupt death can lose output and leave small profile residue |
| Camera/image/text/color | Device/file/text controls, size/color, transform/crop | Native color/image/text; physical webcam hardware not available here |
| Local media / slideshow | Looping local video, opt-in media audio; 1–20 slide images, interval and pause while hidden | Actual changing red/blue pixels and finalized recording. Hardware decoding requested, software fallback observed on this PC. Slideshow caches cost memory |
| Video filters | Opt-in brightness/contrast/saturation/opacity and green chroma key | Native grayscale and transparent-key pixel checks; not the complete OBS filter catalog |
| Simple layouts | Existing camera/text/logo overlays and three presets | For Display/Window/Area modes; scene collections replace these overlays |
| Audio | Devices, gain/mute, meters, suppression/gate/compressor/limiter; sync offsets, optional mic monitoring; mixed/computer/mic recording tracks | Three AAC streams decoded. Track 1 is mixed, 2 computer, 3 microphone; media stays on mixed. Replay/streaming stay mixed. Physical monitoring and acoustic sync/routing qualification pending; per-app capture unsupported |
| Virtual camera | Independent Rilmo DirectShow device; explicit setup/remove; independent output | x64/x86 consumers plus real libobs producer verified without registration; install/uninstall, packaged and meeting-app qualification pending |
| Floating controls | Movable/persisted/monitor-clamped button; record/live/replay/screenshot/scenes/camera/settings | Accessible Invoke/focus and exclusion API tested; manual pointer/mixed-DPI qualification pending |
| Captures | Local search/library, screenshots, open/reveal, remux | WPF integration and actual files |
| Animation | Short press/page/menu feedback; Windows/reduced-motion checks; disabled during capture | Suppression/disabled-motion behavior tested; no decorative timer |
| Preferences / typography | Locally embedded Doppio One, taller controls, expanding aspect-preserving preview, editable five-action shortcuts and developer credit | Bundled font resolved, large/compact renders, duplicate/invalid shortcut rejection, persistence and native registration/conflict/release checks |

## Resource design and measurement

Normal sessions run directly without OBS's frontend or browser subprocesses. Native capture initializes on demand. Preview is requested rather than continuous. Composition stays on the GPU; unchanged inputs are reused. Inactive cameras deactivate and inactive browser pages shut down. UI statistics update once a second; volume changes are coalesced and unchanged filters retained.

Hidden/minimized windows now skip unnecessary UI metric/meter refresh, and already loaded core modules are not initialized twice for optional sources. The matched 720p30 frontend-plus-children GPU comparison measured **2.50% CPU / 173.09 MiB working set for Rilmo**, versus **2.23% / 117.77 MiB for OBS**. Both had zero native drop/skip counters. OBS currently has the lower measured CPU and working set; Rilmo is not claimed faster or lighter. See [matched benchmark](performance-comparison-0.4.md) for all settings, private memory, limitations and raw evidence. Browser functionality has an additional real resource cost.

## Remaining OBS scope

VLC/network media, custom/stinger transitions, the remaining advanced video/audio filter catalog, arbitrary audio bus routing/per-app audio, HEVC/AV1/HDR/10-bit workflows, encoding profiles/OBS migration, third-party frontend plugins, scripting/websocket/scheduled capture and OBS's complete Studio Mode/multiview/projector workflow remain unimplemented. Nested scenes offer group transforms; native OBS group import is not supported. These are product scope gaps, not Microsoft Store requirements.

## Distribution gates

Runtime does not require installed OBS. Clean-machine standard-user installation/update/uninstall, camera registration in the selected distribution route, signing, exact corresponding-source/license delivery, publisher identity/public URLs, accessibility/WACK and Store certification remain separate gates. Upstream capture may request OBS compatibility updates; browser pages and streams also use the network. This build is not described as fully offline.

See [0.4 validation](release-validation-0.4.md), [0.3 baseline validation](release-validation-0.3.md), [Store/licensing](store-and-licensing.md), [scene engine](scene-engine-contract.md), [browser/game sources](browser-and-game-sources.md) and [release checklist](../packaging/release-checklist.md).
