# Rilmo Recorder

A Windows recorder with an original black/red interface, locally bundled Doppio One typography, floating quick controls and a bundled OBS capture engine. **Version 0.4.2 is a working development build; full OBS feature parity and Microsoft Store certification remain incomplete.** Application code uses GPL-2.0-or-later.

The 0.4 update adds custom hotkeys, nested scenes, local video/slideshow sources, Swipe/Slide/Fade-to-black transitions, green-screen/color filters and advanced audio controls. See [feature coverage](docs/feature-coverage.md), [validation](docs/release-validation-0.4.md) and the [matched OBS performance comparison](docs/performance-comparison-0.4.md). OBS currently has lower measured recording working set on the tested PC; this project does not claim universal performance superiority.

## Run on this development PC

The latest published build launches with **`Start Rilmo.cmd`**. Its .NET 10 desktop runtime, selected OBS 32.2.2 libraries and native helpers are included. **A separate OBS installation is not needed at runtime.** `dist/latest-build.txt` identifies the latest successfully published folder. If an older build is recording, publishing preserves it and creates a separate new folder.

```powershell
& 'C:\Video Recorder\build.ps1'
& 'C:\Video Recorder\run.ps1'
```

The build uses the **.NET 10 LTS** SDK under `.tools/dotnet`, or on PATH. `tools/install-dotnet.ps1` downloads the official Windows x64 SDK, verifies its published SHA-512 and extracts it locally. The build script disables SDK telemetry and development-certificate generation for its child build process. Runtime preparation/staging verifies an inventory of app-local files; see [standalone runtime](docs/standalone-runtime.md). Launch the executable/apphost, not `dotnet "Rilmo Recorder.dll"`, because native helpers resolve relative to that executable.

To publish a local self-contained build: `./build.ps1 -Publish`. The selected OBS payload was checked against the hash-verified official 32.2.2 archive and matched the previously tested files. Acquisition and Microsoft runtime origins are recorded separately; this does not make it a source-complete public release. Production installation follows the remaining gates in [packaging/README.md](packaging/README.md).

## Working preview

- Display, application window and selected-area capture.
- H.264 recording with a preferred hardware encoder and a software fallback.
- Computer audio and microphone toggles; cursor capture toggle.
- Named input/output audio device selection without starting capture during discovery.
- Light, balanced, smooth and original-resolution presets.
- Optional countdown and minimize-on-record.
- Pause/resume, stop with file finalization, visible recording/tray state.
- Starting a recording waits for usable encoded video before showing active recording; short recordings can join an existing replay session without missing the initial keyframe.
- Global start/stop (`Ctrl+Shift+R`) and pause/resume (`Ctrl+Shift+P`) shortcuts.
- Local recording library, search, open folder and play using the chosen Windows player.
- One-shot display/area/scene preview. Temporary scene preview releases its capture graph and does not open microphone or system audio.
- MKV output to reduce crash-related recording loss. A crash can still lose buffered data.
- Lossless MP4 export from the recording library, with cancellation and preservation of the original MKV.
- Independent RTMP/RTMPS streaming controls: enter a server and stream key on Live. Keys remain in memory and are redacted from native diagnostics.
- Replay buffer with 5–120 seconds / 128 MB cap, clip saving and independent stop. Recording and replay share an encoder; streaming uses a separate encoder.
- Camera overlay, screen/camera layout presets, text and image overlays.
- Scene studio: named scenes; source creation/editing/removal; drag/resize, numeric position/rotation, crop, visibility and layer order; JSON save/import/export. Cut and Fade transitions work during capture.
- Selected-window game capture, tested with an animated Direct3D 11 target.
- Browser URL/local HTML layers with size/FPS settings, inactive-page shutdown and a fresh isolated worker per browser session. Browser audio is currently muted.
- Independent Rilmo Virtual Camera, with separate start/stop. The MSI candidate owns x64/x86 device registration and repair; portable previews use explicit setup. Registered consumers and non-elevated libobs output were tested on the development PC.
- Subtle press/page/menu animations respect Windows preferences and stop during active capture.
- Live system/microphone volume, mute at zero, audio meters and optional microphone noise suppression, gate and limiter.
- Draggable floating control with record/stop, pause, stream destination, screenshot, replay/save, open and settings actions. Position persists and is clamped to connected displays.
- Region screenshots saved as PNG and listed alongside recordings in Captures.
- Quick controls (`Ctrl+Shift+Q`), screenshot (`Ctrl+Shift+S`) and replay (`Ctrl+Shift+B`) shortcuts.

Closing the main window keeps the floating controls available when enabled. Use the tray's Exit action to quit; active outputs require a deliberate stop. Capture configuration is locked during a session, while output stop/save controls remain available. Pause is unavailable while streaming, replay or virtual camera shares the capture session. Scene editing and switching remain available during scene sessions. Adding the first browser source to an active ordinary session requires finishing that session first.

The app does not start the OBS user interface. Ordinary capture runs directly; browser sessions run in a dedicated process with app-local CEF/Qt components. Tests confirm that the main UI process remains free of Qt/CEF. The engine is loaded only when capture starts; camera/audio discovery stays independent. The capture graph is released when its last output ends. Preview is on demand, stats update once per second during capture, volume changes are coalesced, and filters are rebuilt only when their selection changes. These choices do not establish superior performance across hardware.

## Evidence and tests

`tools/engine-smoke.ps1` exercises the actual native engine against this machine's desktop. It records local test files; run it only when the displayed content is appropriate for a recording test. It checks hardware/software paths and repeated lifecycle as selected by parameters.

`tools/ui-smoke.ps1` builds a separate instrumented UI executable and tests recording, pause/resume, remux, replay-only and combined outputs, screenshot files, missing-display safeguards, invalid stream settings, lazy loading, standalone module paths and full/compact layouts. It saves snapshots/results under `artifacts/ui-smoke`. The unattended recording switch is compiled only with `RilmoTestMode=true`; ordinary builds do not include it. Tests capture local screen content, so run them on an appropriate desktop.

- [Engine validation](docs/engine-validation.md)
- [Product and performance plan](docs/product-and-performance-plan.md)
- [Architecture decisions](docs/architecture.md)
- [REA investigation](docs/obs-investigation.md)
- [Store and licensing requirements](docs/store-and-licensing.md)
- [Store release checklist](packaging/release-checklist.md)
- [Current feature coverage and remaining OBS features](docs/feature-coverage.md)
- [Engine output/composition contract and validation](docs/engine-v2-contract.md)
- [Design direction and official references](docs/design-direction.md)

## Still required

Network/VLC media, the remaining advanced filter catalog and arbitrary audio routing, additional codecs/HDR, OBS frontend plugin compatibility, native OBS group import, stinger transitions, named encoding profiles and OBS configuration migration remain unfinished. Nested scenes and mixed/computer/microphone recording tracks are implemented. Scene collections have their own JSON import/export format. This PC has no physical camera and only local RTMP has been exercised. Broader hardware/service compatibility is not implied by libobs reuse.

Current measurements are single-machine validation runs, not matched comparisons against OBS. A 60-second engine-host test excludes WPF and ran alongside an existing user recording; its render/encoder counters showed some timing misses. Extended endurance, A/V synchronization, power use, multi-GPU/hybrid-laptop behavior, camera/RTMPS compatibility and broader accessibility testing remain release gates.

The full build bundles core capture, the optional-use browser component and both virtual-camera DLLs. The new MSI candidate installs the camera as part of the same Windows Installer transaction. Install, missing-registration repair, major upgrade, failed-upgrade rollback and exact uninstall were exercised on Windows 10 build 19045. Clean Windows 10/11 and third-party meeting-app qualification remain release gates. The upstream capture module may retrieve OBS compatibility data; browser pages and streams also use the network. See the privacy policy.

App-local dependency resolution is tested. Public original application source is at https://github.com/Hasnain4700/rilmo-screen-recorder and public privacy/support pages are at https://rilmo-screen-recorder.wigglymaple8.chatgpt.site. The reserved Microsoft Store identity is recorded locally. Complete combined-runtime corresponding source, trusted signing, clean-machine testing and Store certification remain incomplete. No app binary has been uploaded to the Store. See [0.4.2 validation](docs/release-validation-0.4.2.md) and [MSI candidate](packaging/msi-candidate.md).

An offline EXE installer preview is also prepared for review, with explicit camera cleanup on uninstall. It is unsigned and has not been installed on a clean machine; see [offline installer status](packaging/offline-installer.md). Its compiler and all application dependencies are acquired at build time, so the resulting installer does not download components during setup.

## Code signing policy

The selected Store route is MSIX, with a Windows 11 virtual-camera target and Windows 10 recording/streaming retained. Microsoft signs certified Store packages; the earlier EXE/MSI signing work is an alternative development route. Current Rilmo development binaries are unsigned. See [Code signing policy](CODE-SIGNING-POLICY.md) and [MSIX camera qualification](docs/msix-camera-qualification.md). Component CI retains evidence; it does not publish an installer or prove a final Windows 11 camera release.

## License

Rilmo Recorder application code is licensed under **GPL-2.0-or-later**. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Selling a GPL application is permitted, subject to the license's source and recipient-rights obligations. Before distribution, provide the complete corresponding source for the binaries actually shipped, with the required notices/build material.

## Scene studio and camera

Choose **Scenes** on Record, then **Edit…**. Add a source, choose its device/window/file/page, and adjust its position and crop. **Apply scene** saves the collection. **Preview frame** takes one actual frame; the canvas itself is a lightweight layout map. Studio's scene picker switches the live output with a fade. Import/export carries local paths and browser URLs, so review scene files before sharing.

In **Studio → Virtual camera**, start the camera, then choose **Rilmo Virtual Camera** in your meeting application; choose your microphone there separately. MSI installations include the device and offer Windows Installer repair if registration is missing. Portable previews show a setup action with an explanation and administrator prompt. Both routes use an independent Rilmo identity. The portable setup tool refuses to alter an MSI-owned installation. Removal details are in `docs/virtual-camera.md`.

`tools/ui-smoke.ps1 -IncludeBrowser` additionally exercises the real WPF app's repeated browser-host sessions and native scene editing/preview. `tools/engine-features-scenes.ps1`, `tools/advanced-sources-smoke.ps1` and the virtual-camera checks document targeted native evidence. See [0.3 validation](docs/release-validation-0.3.md), [scene engine contract](docs/scene-engine-contract.md), and [browser/game behavior](docs/browser-and-game-sources.md).
