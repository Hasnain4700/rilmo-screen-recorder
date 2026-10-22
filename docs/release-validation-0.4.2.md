# 0.4.2 local validation — 23 October 2026

This release continues the existing charcoal/coral UI and bundled Doppio One font. It adds an MSI-owned camera lifecycle and Windows Installer repair in the app. It does not claim complete OBS parity or Microsoft Store approval.

| Check | Observed result |
| --- | --- |
| Production self-contained publish | Succeeded, no build warnings/errors; no separate OBS install used |
| Real WPF integrated suite with browser | Passed: recording, pause/resume, independent replay, screenshot/library, MP4 export, lazy loading, scene editing/persistence/live transitions, browser host cycles and reduced motion |
| Typography/layout/hotkeys | Passed; 1900×1000 view fills its 744px viewport, preview is 434px; custom/disabled/invalid/conflicting hotkeys tested |
| Game capture | Animated owned Direct3D 11 target, app-local hook and decoded output passed |
| Media/filter/audio suite | Nested scenes, three additional transitions, changing local video/slideshow, chroma key, saturation and separate audio tracks passed |
| File decoding | All ten selected new MKV/MP4 recordings fully decoded without decoder errors |
| Virtual camera | x64/x86 direct and registered COM consumers; padded NV12, live frames, inactive slate, duplicate/restart/stop and cleanup passed |
| Non-elevated camera | Public StartVirtualCamera/StopVirtualCamera methods sent real libobs frames to both registered client architectures; 46 frames each in this run |
| MSI lifecycle | Install, registration repair, major upgrade, deliberately failed-upgrade rollback, exact uninstall and baseline reinstall passed on this PC |
| Signing inventory | 531 payload PE files: 527 valid upstream signatures, four unsigned Rilmo files; MSI and embedded launch-guard signature also pending |
| WACK | Full desktop-mode run completed: 19 PASS, 4 WARNING, no FAIL. This is not a clean pass. The report applies only to the original unsigned candidate described below. |

The fullscreen test initially failed its old 440px threshold after the control styling changed. Captured geometry and screenshot showed the workspace filled correctly at 434px. The regression now checks the actual gap, viewport fill and growth when resizing. No production layout was weakened to satisfy a fixed threshold.

The MSI's original WindowsBuild check rejected Windows 10 because MSI reports compatibility values 6.3/9600. Standard AppSearch now reads the OS-maintained CurrentBuildNumber. A deliberate active-app mutex test confirmed build 19045 is accepted and the running-app guard rejects installation before mutation.

Evidence is under `artifacts/ui-smoke/results-20261022-215002-209`, `artifacts/advanced-sources/game-results`, `artifacts/feature-expansion/results-20261023-025534`, `artifacts/recording-validation-0.4.2.json`, `artifacts/signature-inventory-0.4.2.json` and `artifacts/msi-lifecycle-20261023-025854`. UTC folder timestamps can show 22 October while local Pakistan time is 23 October.

The machine is Windows 10 build 19045 with AMD graphics. There is no spare PC or VM. This does not test a clean Windows 10/11 image, physical webcam/monitoring hardware, NVIDIA/Intel encoding hardware, all games/anti-cheat modes, external RTMPS services, or third-party meeting/browser camera clients. The previous matched OBS benchmark remains the performance evidence; it measured OBS with lower CPU/working set. These short regression runs are not a new performance comparison.

All 34 selected core source inputs are now hash-verified locally. Exact combined-runtime source/build correspondence, CEF/Qt/static-runtime review and source delivery remain unfinished; original application source on GitHub is not a substitute. Native source-completeness approval flags were not changed.

The WACK 10.0.28000.2957 report has SHA-256 `6139b48d7a8762ae34c8ef9fc240eb35fbc5c9964031ed1bbbee38e9b10925b6` and `PARTIAL_RUN=FALSE`. It tested MSI SHA-256 `0ba5ec4f978f6c81d313e925cb3c2e02c96bf55a218140999a1a715b7b62fc6f`. Its warnings concern install-location metadata, unsigned Rilmo files, x86 virtual-camera SafeSEH, and high DPI declarations/legacy upstream DPI API imports. The installer process exiting zero does not turn these warnings into a pass. WACK restored its temporary service/diagnostic settings and the installed baseline app.

Follow-up changes add explicit ARPINSTALLLOCATION, native product/version resources, and the legacy true/pm DPI manifest entry alongside PerMonitorV2. These changes require a new candidate and new validation; the earlier report is not evidence that changed bytes passed. Upstream signed libraries are not modified to hide static DPI warnings. Trusted signing and the x86 SafeSEH toolchain warning remain open.
