# Browser and game sources

Rilmo uses native sources from the pinned OBS 32.2.2 runtime. `SceneSource.Kind=Game` selects `game_capture`; `SceneSource.Kind=Browser` selects `browser_source`. These are scene layers, with the same transform, crop, ordering and visibility model as other sources. This implementation does not launch the OBS application or install a third-party plugin.

## Game capture contract

The user selects a visible, restored window. `WindowHandle` is resolved to its title/class/executable selector, uniqueness is checked, and the upstream matcher must resolve back to that HWND before capture starts. `GameCaptureMode` is currently `window` only. There is no automatic foreground-game selection and no fallback to another window. Capture initialization waits up to eight seconds for a hooked frame and reports incompatibility if none arrives.

The source limits capture to the recording frame rate, obeys `CaptureCursor`, and does not capture overlay applications or game audio. System audio remains controlled separately. It uses the normal upstream hook path; the alternative anti-cheat compatibility hook setting is disabled. Game compatibility and permission level still depend on the target application and driver. A compatible test target does not establish universal game compatibility.

Official implementation: [OBS 32.2.2 game-capture.c](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/win-capture/game-capture.c).

## Browser contract and resource use

`FilePath` selects a local `.html`/`.htm` file; otherwise `BrowserUrl` must be an HTTP(S) URL without embedded credentials. Dimensions are 16–8192 pixels per side, frame rate is 1–60 with a 30 fps default, and the source's webpage control level is None. The browser is a visual layer: audio is rerouted away from Windows playback, muted, and excluded from audio mixers. This avoids unexpected browser audio being captured again through system loopback.

`shutdown=true` makes upstream destroy a page when its source stops showing. Returning to that scene loads the page again, so in-memory page state is not preserved. Hidden pages are not intentionally kept rendering. Page readiness is asynchronous: configured width is not proof that content loaded. Initial creation loads CEF; ordinary source discovery, the empty dashboard, and sessions without any browser source do not load CEF.

The genuine pinned browser module imports CEF, Qt6Core/Gui/Widgets and `obs-frontend-api.dll`. The latter is a support API DLL; no OBS frontend executable is packaged or launched. All of these dependencies stay in the optional `runtime/browser` component. The Windows module uses its dedicated CEF manager thread. Upstream CEF itself is built without the Chromium sandbox; this is an inherited property of this OBS browser baseline, not a security guarantee. Re-evaluate the browser dependency's security support before release.

Native testing exposed a lifecycle limitation: `obs_shutdown` leaves module DLLs loaded, and the browser module cannot restart its manager after shutdown. Explicit DLL unloading also crashed on CEF reinitialization. The application therefore uses `RecorderClient`: ordinary sessions run directly in-process, while a collection containing a browser source runs in a new hidden `--engine-host` child per session. Adding the first browser layer to an already active direct session requires stopping that session first. No CEF or Qt modules enter the UI process in this path.

The host uses a current-user-only named pipe, a random token, mutual process-ID verification, an explicit operation allowlist, bounded packets and response timeouts. A host-owned Windows job contains its CEF descendants. A parent disconnect triggers graceful native finalization; a host crash terminates descendants. A forced crash can lose pending muxer writes, including leaving an empty file. The client retains the known path but explicitly refuses to label an interrupted recording as saved. Each host gets a separate temporary profile under Rilmo's `RuntimeSessions` directory; normal stop removes only that generated profile. Browser cookies and page storage are not reused across complete sessions. An abrupt parent crash can leave small profile files locked until host exit, so crash-residue cleanup remains best effort; no broad cleanup of unrelated runtime folders occurs.

The original `win-capture` module also attempts an automatic compatibility-data update from `https://obsproject.com/obs2_update/win-capture/v1/package.json`. The pinned module has no runtime disable switch in its initialization code. This is separate from user-chosen browser URLs and streaming; this build must not be described as fully offline. No packet-payload privacy audit has been performed. Source: [pinned win-capture module initialization](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/win-capture/plugin-main.c).

Sources: [pinned obs-browser source](https://github.com/obsproject/obs-browser/tree/3f0a2cdf378939ebe3c6f9ab36d4ea100c25aac2), [browser source lifecycle](https://github.com/obsproject/obs-browser/blob/3f0a2cdf378939ebe3c6f9ab36d4ea100c25aac2/obs-browser-source.cpp), [CEF initialization and shutdown](https://github.com/obsproject/obs-browser/blob/3f0a2cdf378939ebe3c6f9ab36d4ea100c25aac2/obs-browser-plugin.cpp), [OBS pinned dependencies](https://github.com/obsproject/obs-studio/blob/32.2.2/CMakePresets.json).

## Preparing and staging the optional component

The core runtime remains unchanged. Prepare once, then stage explicitly into a built/published application's directory:

```powershell
# Prefer the pinned official portable archive, checked before extraction.
.\tools\prepare-browser-runtime.ps1 -VcRuntimeDirectory C:\path\to\audited-x64-redist

# Local development alternative: explicit installed OBS fallback with provenance.
.\tools\prepare-browser-runtime.ps1 -NoDownload -AllowInstalledFallback

.\tools\stage-browser-runtime.ps1 -AppDirectory C:\path\to\Rilmo-output
```

Preparation refuses to replace an existing prepared component. The inventory includes SHA-256 for every native binary, CEF resource, locale and data file, the normal/delay import closure, and acquisition provenance. Microsoft runtime files are taken from the audited core inventory where possible; additional VC++ files require a valid Microsoft signature. Staging validates the entire inventory and rejects missing, altered, extra or path-escaping source files before copying. It rejects reparse points in source/destination ancestry. Loading uses an application-local directory; no installed-OBS path, environment override or machine registration is used at runtime.

The pinned upstream archive is [OBS-Studio-32.2.2-Windows-x64.zip](https://github.com/obsproject/obs-studio/releases/download/32.2.2/OBS-Studio-32.2.2-Windows-x64.zip), SHA-256 `4d6e40e3ab155f56b30de517380566a206d74b63cdf5ad49aa596924768f97e1`. OBS revision: `ba2f32bdf791005443988a4955e963663e16b1ed`. Browser submodule: `3f0a2cdf378939ebe3c6f9ab36d4ea100c25aac2`. CEF dependency: 6533, Windows x64 revision 2, upstream dependency archive SHA-256 `922efbda1f2f8be9e5b2754d878a14d90afc81f04e94fc9101a7513e2b5cecc1`.

The prepared component now records `official-portable-archive`. All 139 selected browser/CEF/Qt/data payload files were compared byte-for-byte against the SHA-256-verified OBS32.2.2 official portable archive; every file matched. Only provenance metadata was changed, with no executable changes. The complete inventory contains 280 files (approximately 311 MiB), including separately sourced Microsoft runtimes and additional notices. `artifacts/browser-official-comparison.json` records the comparison. This establishes binary acquisition provenance; corresponding-source/build review remains separate. The collector verifies the exact CEF archive, full CEF source/patch archive and QtBase 6.11.1 source archive. It extracts the complete 9.35 MB Chromium credits HTML from the exact CEF resource pack, whose hash also matches the prepared runtime, plus the CEF license and QtBase license/attribution files. The pinned Chromium DEPS file and Qt Windows build patch are included. Run `tools/fetch-browser-release-inputs.ps1 -NoDownload`, then `tools/complete-browser-notices.ps1`, to verify and reproduce these notice inputs; `release/browser-source-inputs/manifest.json` records all hashes and upstream locations.

`correspondingSourceComplete=false` and `publicDistributionApproved=false` remain accurate. The full Chromium checkout and relevant dependency sources, CEF patch/build arguments, Qt enabled-feature and static-dependency mapping, and exact binary/source build correspondence still require completion. Full Chromium credits include multiple platforms and do not establish which components are present in this Windows build. The selected license obligations, source/relinking presentation and Microsoft redistributable terms also need a final package audit. Verified archive hashes and complete notice texts alone do not approve public distribution.

## Reproducible native checks

```powershell
.\tools\advanced-sources-smoke.ps1 -Mode Browser
.\tools\advanced-sources-smoke.ps1 -Mode Game -SkipBuild
```

The isolated browser harness uses a generated local HTML file and loopback heartbeat server. It checks rendered green pixels, records and decodes a frame, switches scenes and verifies the page's timer stops, resumes the page, and checks helper-process cleanup across three fresh sessions. It also samples aggregate host/helper memory; this is an observation under that scene, not a performance benchmark, and summed working sets can double-count shared pages. The game harness launches only its own Direct3D 11 window, checks changing captured pixels, records, decodes a frame, and closes that owned process. It does not hook unrelated applications or connect to an external streaming service. The upstream compatibility update described above may still run.

Game hook initialization logs a missing stock-layout `../../data/obs-plugins/win-capture/graphics-hook` path. Upstream then uses its app-local module data fallback, which the Direct3D test exercises. Do not recreate that stock relative layout: the initializer would then copy hooks into shared ProgramData and attempt Vulkan implicit-layer registration. This implementation does not provide that Vulkan setup path.

The test target follows Microsoft's [D3D11CreateDeviceAndSwapChain](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-d3d11createdeviceandswapchain) and [ClearRenderTargetView](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-clearrendertargetview) interfaces. Results and recorded evidence live under `artifacts/advanced-sources/`; test status is reported separately from this implementation description.

## Local validation on 22 October 2026

- Three fresh browser sessions passed after the isolated host and per-host profile fixes. Every session rendered the expected local HTML pixels, produced a decoded MKV frame, stopped HTML heartbeat requests when inactive, resumed when shown, and left no browser helper process. Evidence: `artifacts/advanced-sources/browser-results/stdout.log`, preview PNGs and `.mkv.decoded.png` files.
- The owned Direct3D 11 target produced changing captured pixels and a decoded MKV frame. Evidence: `artifacts/advanced-sources/game-results/`, native log `engine-2026-10-22-042713-495.log`.
- Active 640×360 local-HTML samples showed six host/helper processes, 276–285 MB summed working set and 172–182 MB private bytes. These are single-machine observations, excluding the UI, not a comparative benchmark or a “lightest recorder” claim. Chromium is an optional cost.
- The independent isolated-engine harness verified HWND transport, replay/recording, pause, scene transitions, graph rollback, retained final statistics, host-crash child cleanup, and a successful fresh session after host failure. A parent-only crash produced a finalized, fully decoded 2.069-second MKV and no remaining host/helper. Evidence: `artifacts/isolated-engine/results-20261022-042741` and `results-20261022-043117`.
- Staging rejects a corrupted file and `../` inventory path before application output is changed. Source scripts parse without errors; native integration harness builds with zero warnings/errors.

