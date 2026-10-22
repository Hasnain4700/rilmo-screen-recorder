# Product and performance plan

Research date: 22 October 2026. Product direction: a Windows screen recorder with the depth of OBS and the clarity of a small capture utility. The user's latest direction is an original black/red recorder interface with draggable quick controls; it supersedes the earlier charcoal/mint player reference. The user approved an open-source product, permitting a GPL-compatible libobs architecture subject to the release licensing checklist. See design-direction.md and feature-coverage.md for current implementation status.

This is a design and engineering plan, not a claim that its features or performance targets already exist. Every feature below remains **planned until the implementation and its acceptance evidence are recorded**. A feature inherited through an engine still needs a usable interface, persistence, error handling, packaging, and tests before it is a product capability.

## 1. Product promise

The default journey is: choose what to capture, check sound, press Record, find the saved video. A returning user should be able to record with one click using their last valid setup. The first run can involve Windows permissions and source selection; the application must not imply that it can bypass those choices.

The core distinction is **simple access to a powerful engine**. OBS already uses GPU composition and hardware encoders. A simpler interface alone does not prove faster capture. Our opportunities are lower idle work, fewer active sources, less preview work, reliable automatic presets, reduced startup loading, and correct capture/encoder selection. OBS documents source complexity and GPU contention as meaningful performance costs. [OBS encoding performance](https://obsproject.com/kb/encoding-performance-troubleshooting), [OBS hardware encoding](https://obsproject.com/kb/hardware-encoding).

Do not claim “fastest recorder,” “zero lag,” or “all OBS features” without a defined comparison and passing evidence. A useful release claim will be specific, for example: “On the listed systems, the default 1080p30 capture used X% less median CPU than OBS version Y at matched settings.” Values must come from published measurements.

## 2. Interface structure

| Place | User goal | Visible by default | Revealed when needed |
| --- | --- | --- | --- |
| Record | Start a reliable capture | Display / Window / Area; microphone, system sound, camera; quality preset; Record | Source picker, device menu, sound test, output folder, countdown |
| Recordings | Find the result | Recent videos, duration, size, thumbnail, Play, Show in folder | Rename, remove from list, delete file with clear confirmation, export |
| Studio | Build a production | Scene strip, canvas, selected-source inspector, audio mixer | Filters, transitions, source groups, routing, output destinations |
| Settings | Change enduring preferences | Save location, shortcuts, appearance, default quality | Encoder, codec, color, audio routing, streaming, compatibility, diagnostics |
| During recording | Keep control | Elapsed time, Pause, Stop, microphone state | Compact status details, chapter marker, save replay |

Record is the initial workspace. Studio is a deliberate navigation choice, not a dock layout forced on every new user. Changing workspaces must preserve recording state and setup. Advanced choices remain discoverable through “More settings,” a searchable settings page, and explanatory labels.

### Record screen composition

1. A compact left navigation column: Record, Recordings, Studio, Settings.
2. A concise title and helper line, such as “Ready to record” and “Choose a screen. We’ll take care of the settings.”
3. A large capture card showing the selected target. Never display a fake live preview: either show actual capture, a marked snapshot, or an explicit empty state.
4. Source mode buttons with text labels: Display, Window, Area. The selection is clear without color alone.
5. Two sound rows with named devices and real level meters: Microphone and System sound. Camera is a compact optional row.
6. One quality control: Auto, Light, Balanced, High quality. “Auto” includes a short explanation of the selected resolution and frame rate.
7. One primary red Record button. Keep the save location accessible in preferences and technical health information in a quiet status area.

Recording controls must never shift because a meter changes width or status text grows. Start/stop clicks require explicit state transitions so double-clicks cannot create duplicate sessions. The app moves through Idle → Preparing → Recording ↔ Paused → Finalizing → Saved, or a clearly explained Error state.

### Visual direction from Rilmo and premium products

The earlier player reference has been superseded by the user's explicit request for a recorder-specific black/red interface. Current implementation uses task navigation, capture-source selection, a pinned recording transport and an independent floating menu. Official recorder design references are linked in design-direction.md.

| Token | Proposed value | Use |
| --- | --- | --- |
| Window | `#101313` | Main background |
| Surface | `#191E1C` | Main capture and settings cards |
| Raised surface | `#222A26` | Menus, hover state, secondary actions |
| Border | `#313C36` | Sparse separation and focus context |
| Primary text | `#F1F5F2` | Titles and controls |
| Secondary text | `#A7B3AB` | Supporting labels; verify contrast in rendered UI |
| Accent | `#A5E5C4` | Record action and selected state |
| On accent | `#102319` | Primary action label |
| Error | `#F28B82` | Actual errors and recording indicator with text/icon |

Use one system or correctly licensed font, 13–14px body text, 24–30px page titles, 6–10px corners, consistent 8px spacing, and 36–44px pointer targets. Prefer solid surfaces over blur, large shadows, animated gradients, and continuously moving decoration. Keyboard focus, Narrator labels, high contrast, text scaling, 125–200% DPI, reduced motion, and keyboard access are acceptance requirements.

Official premium products suggest useful patterns, not an asset-copying brief:

| Reference | Observed product pattern | Application to this recorder |
| --- | --- | --- |
| [Screen Studio](https://screen.studio/) | Capture followed by presentation polish, including framing and zooms | Keep capture controls compact. Put zoom, cursor styling, and decorative backgrounds in a later editing/export stage. |
| [Tella features](https://www.tella.com/features) | Explicit Record, Edit, Share stages and reusable layouts | Separate recording from editing. Offer a small number of understandable camera layouts before exposing manual transforms. |
| [CleanShot](https://cleanshot.com/) | Fast capture, immediate access to the result, optional polish | After stopping, show a useful saved-video card with Play and Show in folder. Avoid forcing users into a full editor. |

These are UX references, not Windows performance comparators. Screen Studio and CleanShot describe macOS products, and their marketing does not establish our performance or implementation choices.

## 3. One-click setup behavior

The setup service should detect display dimensions, active adapter, supported encoders, microphones, system output, camera availability, output-folder access, disk space, and battery/power state. It should probe whether an encoder can actually initialize, not merely whether a GPU name appears in a table. Save a capability record keyed by device/driver configuration and refresh it after relevant changes.

Suggested presets are initial engineering choices, not final benchmark conclusions:

| Preset | Starting policy | User-facing explanation |
| --- | --- | --- |
| Auto | Choose among measured, supported presets; retain a hardware encoder when stable | “Recommended for this PC: 1080p · 30 fps” or the real selected values |
| Light | Start at 720p30; no camera, preview throttled, costly filters off unless explicitly chosen | “Keeps more resources free for your other apps” |
| Balanced | Up to 1080p30 with hardware H.264 where available | “Clear video with everyday file sizes” |
| High quality | Higher resolution/frame rate only within verified device capabilities | “Sharper motion and detail; uses more resources” |

One-click setup is not silent modification of Windows settings, drivers, default audio devices, or other programs. Do not automatically close applications or request administrator privileges for ordinary capture. If a target cannot be captured, show a specific fallback and its consequence.

Preflight is asynchronous and cancellable. Report only actionable failures: unavailable target, no write access, insufficient disk space, encoder initialization failure, or missing requested audio device. A microphone that is silent is a visible hint, not necessarily an error. Record with no microphone is a valid choice.

Remember settings per scenario where helpful: Tutorial, Gameplay, Presentation. Do not create an extensive onboarding wizard. Users should see their actual chosen source and enabled inputs before recording starts.

## 4. OBS capability map and phased parity

Parity is bounded to the **audited Windows build and its bundled modules**. Third-party OBS plugins form an open-ended ecosystem and cannot be covered by “all features.” The REA audit should record the installed version, module inventory, source types, encoders, outputs, configuration behavior, and externally observable workflows. That inventory is the release checklist, not screenshots of OBS menus.

Phases describe dependency order, not promised dates:

- **P0 — Evidence and foundation:** identify the audited build, stabilize the native host boundary, create the black/red recorder shell, establish logs and measurements.
- **P1 — Dependable recorder:** reliable display/window/area capture, sound, hardware encoding, save/finalization, library, keyboard controls, and recovery.
- **P2 — Audio and composition:** camera, scenes, source layering, filters, advanced audio, profiles, and replay.
- **P3 — Broadcast production:** streaming, studio preview/program, transitions, virtual camera, monitoring, and projector workflows.
- **P4 — Expert compatibility:** uncommon sources, plugin compatibility, scripting, remote control, import/export, full encoder/output controls, and final audited parity.

Every row is **planned, not certified implemented** in this research document.

| OBS capability | Simple product entry point | Phase | Acceptance evidence needed |
| --- | --- | --- | --- |
| Display capture and cursor selection | Record → Display | P1 | Each monitor, mixed DPI, unplug/reconnect, lock/unlock, cursor on/off, capture excludes app when supported |
| Window capture and matching | Record → Window | P1 | Move/resize, minimize/restore, title changes, target close, GPU/browser windows |
| Area capture | Record → Area | P1 | Visible selection, correct physical pixels at mixed DPI, safe crop at screen edges; product convenience built on capture/crop |
| Windows game capture | Record → Game / Studio source | P2 | Supported graphics APIs and fullscreen modes; anti-cheat compatibility tested and accurately disclosed |
| Microphone and desktop output | Two Record sound controls | P1 | Device selection, independent levels/mute, unplug, default device change, monotonic timestamps |
| Per-application audio | Sound → Only this app | P2 | Supported apps, app process changes, no double-captured desktop mix; OS prerequisites checked |
| Hardware/software encoders | Quality → Advanced | P1 basic; P4 complete | Actual NVENC/QSV/AMF/software availability; initialization, fallback, correct output metadata |
| Recording containers and codecs | Save settings | P1 safe default; P4 complete | Supported combinations, file decode, interruption recovery, remux, editor compatibility |
| Pause/resume, split, chapters | Recording toolbar / shortcuts | P1 pause; P2 rest | Duration/timestamps, audio sync after repeated pauses, segment integrity, supported container metadata |
| Replay buffer | Enable “Save last…” | P2 | Memory cap, save during recording, requested duration, keyframe-safe beginning, bounded background use |
| Webcam, capture card, video devices | Camera row / Studio → Add | P2; specialist P4 | Device formats, USB limitations, source sharing, reconnect and audio sync |
| Text, image, solid color, slideshow | Studio → Add | P2 | Layer order, transparency, transforms, sizing, asset path persistence |
| Media and playlists | Studio → Add media | P2 basic; P4 playlists | Loop/restart, audio routing, deactivation, codec support, missing media recovery |
| Browser source | Studio → Web overlay | P3 | Isolated runtime loading, source lifecycle, permissions, settings, transparency and failure recovery |
| Scenes, groups, transforms, crop, locks | Studio canvas and scene strip | P2 | Nested composition, predictable selection, snapping, undo/redo, saved scene reconstruction |
| Video filters | Selected source → Effects | P2 common; P4 complete | Chroma/luma/color key, LUT, color correction, mask/blend, crop/pad, delay, scaling, scroll, sharpen |
| Audio filters | Sound → Voice cleanup / Advanced | P2 common; P4 complete | Gain, compressor/ducking, expander, limiter, gate, suppression, polarity, ordered filter persistence |
| VST 2.x filters | Studio → Audio extensions | P4 | Compatible plugin list, bitness/loading, editor hosting, failure handling, distribution rights |
| Audio monitoring, sync, track routing | Studio mixer → Routing | P2 | Mix track plus separate tracks, monitoring avoids feedback, offset and resampling tests |
| Surround sound and specialist layouts | Advanced audio | P4 | Channel mapping, codec/container support, downmix and playback validation |
| RTMP/RTMPS and supported services | Studio → Go live | P3 | Private test endpoint, service constraints, reconnect/backoff, health reporting, key protection |
| Other bundled stream/output protocols | Advanced destination | P4 | Compare audited module support; implement each supported protocol deliberately |
| Simultaneous recording/streaming | Studio output controls | P3 | Shared/separate encoder paths, encoder session limits, independent stop and failures |
| Scene transitions and stingers | Studio → Transition | P3 | Timing, audio, alpha/track matte where supported, interrupted switches |
| Preview/program Studio Mode | Studio → Rehearse | P3 | Editing preview cannot change program until Take; correct source audio behavior |
| Multiview and projectors | Studio → Monitor | P3 | Display selection, fullscreen/windowed behavior, view frame rate budget |
| Virtual camera | Studio → Use as camera | P3 | Supported Windows integration, client compatibility, installation/removal and packaged activation |
| Profiles and scene collections | Saved setups | P2 | Separate output vs scene settings, migrations, export/import, missing device handling |
| Extensive hotkeys, push-to-talk/mute | Settings → Shortcuts | P1 common; P4 complete | Conflict detection, focus-independent behavior, layouts, disabled/locked capture state |
| Scripting and native extensions | Settings → Extensions | P4 | Defined compatible API surface, version checks, capability boundaries; frontend API is not automatic |
| WebSocket remote control | Settings → Remote control | P4 | Explicit enablement, authentication, address/port scope, protocol version, request/state tests |
| Logs, statistics, diagnostics | Settings → Help / Health | P1 basic; P4 complete | Actual rendering/encoding/network counters, redacted support bundle, no fabricated percentages |
| Accessible/localized UI | Every workspace | Continuous | Narrator, keyboard-only workflows, high contrast, text scaling, translation-ready strings |

The map is grounded in [OBS sources](https://obsproject.com/kb/sources-guide), [OBS filters](https://obsproject.com/kb/filters-guide), [OBS audio documentation](https://obsproject.com/kb/category/7), [OBS Studio documentation](https://obsproject.com/kb/category/3), and the [OBS feature overview](https://obsproject.com/). The inventory must be extended if the audited installed version exposes additional capabilities.

Specific details that affect our interface:

- OBS supports isolated app audio and warns that capturing the same app through desktop and app capture can create duplicate sound. Our UI should detect obvious duplicate routing and explain the choice. [Application audio capture](https://obsproject.com/kb/application-audio-capture-guide).
- A normal player generally plays one audio track at a time. Our recording default should produce a complete mix on track 1, with isolated tracks available for editing. [Multiple audio tracks](https://obsproject.com/kb/multiple-audio-track-recording-guide).
- OBS Hybrid MP4/MOV supports recoverable fragmented writing followed by compatible finalization. A safe recording format deserves a first-class place in the product; do not default to a fragile write path merely because MP4 is familiar. Chapters have separate recovery limitations. [Hybrid MP4 and MOV](https://obsproject.com/kb/hybrid-mp4).

## 5. Architecture for low overhead

The recommended open-source route is a lightweight native Windows frontend around libobs, with a narrow, versioned host boundary. Preserve the engine’s mature device handling and encoding paths while replacing the complicated default workflow. There is no performance justification for starting an entire OBS desktop application behind a second UI.

Separate ownership into application state/UI, capture graph, encoder/output pipeline, device capability service, persistence, and measurement. Capture timing must not depend on the UI event loop. The native host should translate engine callbacks into bounded events, and the UI should observe sampled state instead of repainting on every audio sample or encoded packet.

The module policy must be intentional:

- Load the modules required for the current task. Browser runtime, scripts, specialist device SDKs, and unused extensions must not be startup requirements for simple recording.
- Deactivate unused sources where safe. Preserve deliberate prewarming for seamless scene transitions as an advanced tradeoff.
- Keep frames GPU-resident when the capture, compositor, and selected hardware encoder support it. Measure actual texture copies and adapter crossings; do not label a path “zero copy” without evidence.
- Use bounded frame/audio queues. Prefer a documented frame-drop policy over unlimited memory growth or progressively increasing latency.
- Render the preview at a lower cadence than the output when useful. Disable unnecessary preview rendering while minimized, provided output continues correctly. Record the real capture at the requested rate.
- Cache static overlay results and reuse buffers. Avoid CPU bitmap readback merely to populate a WPF preview.
- Sample UI meters at roughly 10–15 Hz and diagnostic text at 1 Hz. Stop visual timers while their view is hidden.
- Keep AI cleanup, animated zooms, heavy blur, transcription, and export effects off the live capture path by default. If later added, use an explicit post-recording queue with cancellation and power-aware scheduling.
- Preflight the capture/encoder adapter combination. Cross-adapter copies can be expensive; a laptop needs testing with both integrated and discrete graphics.

The Windows capture APIs expose D3D surfaces and timestamps and require careful frame lifetime and device-loss handling. Heavy per-frame work should not block the UI thread. These are useful constraints even when the immediate implementation delegates capture to a libobs module. [Microsoft screen capture](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture).

Do not promise unrestricted OBS plugin compatibility. Some plugins depend on OBS’s Qt frontend, frontend API, particular module versions, bundled browser components, or platform installers. A custom frontend must explicitly implement compatible behavior or publish the limitation.

## 6. Performance measurement before performance claims

### Test systems

Use physical machines with reproducible power and driver settings. A fast development computer is not enough.

| Class | Representative configuration | Purpose |
| --- | --- | --- |
| Low-end target | Intel N100-class CPU, integrated graphics, 8 GB RAM, SSD | Main low-cost Windows 11 usability target |
| Older laptop | Supported 8th-generation Intel mobile CPU, integrated graphics, 8 GB RAM | Thermals, old hardware encoder, battery constraints |
| Memory stress | Supported hardware with 4 GB available/configured RAM | Determine the real floor; do not advertise support before validation |
| Mainstream | Ryzen or Intel midrange CPU, 16 GB, integrated graphics | Typical tutorial capture |
| Gaming / hybrid | NVIDIA or AMD discrete GPU plus integrated adapter | Game capture, GPU saturation, adapter selection |
| ARM64 | Supported Windows ARM device, if shipped | Native/dependency compatibility; separate release gate |

Record exact CPU/GPU, RAM, OS build, driver, storage, display dimensions/refresh rate, scaling, power mode, connected power, thermal state, background processes, app build, encoder, codec, preset, bitrate/quality, and capture method. Do not aggregate unlike encoders or hardware into one percentage.

### Workload suite

| Workload | Duration | What it detects |
| --- | --- | --- |
| Idle, visible and minimized | 5 minutes each | Timer, preview, module, and wakeup waste |
| Static desktop with periodic cursor movement | 10 minutes | Unnecessary frame work and idle content efficiency |
| Scripted scrolling text/browser demonstration | 10 minutes | Text quality, changing regions, frame cadence |
| Motion test/video playback | 10 minutes | Encoder demand and audio synchronization |
| Fixed game benchmark, recorder off/on | At least 3 matched runs | Average FPS and 1% low regression, game/recorder contention |
| Screen + 720p camera + microphone/system audio | 30 minutes | Common tutorial setup and composition cost |
| Six-source production scene | 30 minutes | Scaling with scene complexity |
| Long recording | 2 hours | Memory slope, sync drift, file finalization |
| Failure exercise | Targeted runs | Device removal, capture close, disk full, encoder failure, sleep/resume, process crash |

Use deterministic local assets and scripts where possible. Do not use a user's real private desktop or unannounced public stream as benchmark content.

### Comparison rules

Compare against the audited OBS version with equivalent capture method, source set, output resolution/rate, audio, encoder generation/preset, codec, bitrate or matched visual quality, and preview state. Test our automatic setup separately against OBS automatic configuration; that measures product setup value rather than pure engine speed.

Run at least three measured repetitions after a warmup; record median and range. Randomize comparison order when practical and allow the device to reach a comparable temperature. Keep raw results, trace files, source recordings, and output files. Mark failed or unsupported runs rather than dropping them from charts.

Measure all processes belonging to the product, including browser/plugin helpers. A smaller main process is not a memory improvement if work merely moved into a child process.

### Metrics and provisional targets

The following are **engineering targets to validate, not measured results or Store claims**. Revise them only with recorded evidence and an explanation of the tradeoff.

| Metric | Initial target for simple capture | Measurement |
| --- | --- | --- |
| Warm launch to usable Record screen | ≤ 1.5 seconds on low-end target | Process start → responsive controls; separate device probe completion |
| Cold launch | ≤ 3 seconds on low-end target | Reboot/cold cache protocol documented |
| Idle CPU | Median < 0.5% of total machine CPU | 5-minute sample; specify 0–100% normalization across all logical cores |
| Idle private memory | ≤ 180 MiB for core recorder view | Sum of product process private bytes; report working set separately |
| 1080p30 simple recording memory | ≤ 350 MiB core path, excluding deliberate replay buffer | Private bytes, GPU dedicated/shared memory, peak and steady state |
| Recording CPU | Prefer ≤ 8% total on specified hardware with hardware encode | Matched workload; report actual hardware and quality |
| Missed output frames | < 0.1% during supported steady workloads | Distinguish capture, render, encode, and network losses |
| Start after a valid saved setup | ≤ 1 second to first accepted frame, excluding countdown | User action → capture/encoder timestamps |
| Audio/video drift | ≤ 50 ms after 60 minutes | Synchronized visual/audio markers at start/end |
| Game impact | Aim for ≤ 5% average FPS and ≤ 10% 1% low reduction | Same benchmark recorder off/on; no broad claim across games |
| Long-run memory growth | No sustained unbounded increase | 2-hour memory slope after stabilization |
| Stop/finalize | UI responds within 100 ms; finalization progress remains truthful | Click response measured separately from file readiness |

Do not achieve lower CPU by silently lowering output resolution, dropping requested frames, reducing quality, or omitting audio. Report quality together with resource measurements. Use frame comparisons (SSIM or another documented metric), bitrate/file size, and human inspection of small text, pointer movement, gradients, and fast motion. A numerical video-quality score alone is not enough for screen content.

### Instrumentation

Each recording session should have structured local counters: source frames received, frames composed, frames submitted to the encoder, encoded packets, video/audio queue depth, queue high-water marks, render/encode delay, selected adapter/encoder, output bytes, drop categories, start/finalization timings, and error codes. Keep coarse aggregate diagnostics separate from screen pixels, microphone samples, window titles, file paths, or stream keys.

Use ETW/WPR and WPA to diagnose CPU, scheduling, allocations, disk, and process work; use GPUView for GPU queue/synchronization questions. Add timestamps at pipeline stage boundaries so traces explain where time is spent. External profiling can affect timings, so retain unprofiled timing runs as well. [Windows Performance Recorder](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/windows-performance-recorder), [Windows Performance Analyzer](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/windows-performance-analyzer), [GPUView](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/using-gpuview).

### Automatic optimization rules

Auto setup should use a capability probe and a small, bounded calibration when necessary. It must not run a long hidden benchmark at every launch. Store the reason for the selected preset so the UI can explain it.

During recording, reduce nonessential preview and UI work before touching capture quality. If the requested output cannot be sustained, show a specific message and an optional recommendation. Do not silently change resolution mid-file. Any adaptive frame-rate or encoder behavior must be explicit, supported by the output path, and recorded in session metadata.

## 7. Release gates

1. **Recorder gate:** start/pause/resume/stop consistently; verify the produced file, audio, timestamps, and target selection. Restart after failure must not lose an existing recording.
2. **Usability gate:** a first-time user can select a source, verify sound, record, and find the file without explanation. A returning user starts from a valid saved setup with one click.
3. **Low-end gate:** run the physical-device suite and publish measured limits. Missing hardware evidence means “not yet validated,” not “optimized.”
4. **Parity gate:** each audited OBS capability has a named UI path, implementation, persistence story, error behavior, and passing evidence. Unsupported third-party extensions are documented individually.
5. **Store gate:** follow the dedicated packaging/licensing/policy review. Validate packaged permissions, install/uninstall, file access, virtual camera behavior, notices/source obligations, privacy disclosures, and accurate listing claims.

A first build can be useful and polished before full parity. Its interface and documentation must identify the actual supported capabilities. Disabled roadmap controls must not masquerade as working features; unfinished features should be omitted from the shipping surface or clearly marked as preview.
