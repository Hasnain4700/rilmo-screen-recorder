# Rilmo 0.4 versus OBS 32.2.2

Measured locally on 22 October 2026: Ryzen 3 3200G, four logical cores, Radeon Vega 8, Windows 10 build 19045. **Rilmo did not beat OBS's recording CPU or working set in this test.** This is evidence for one modest PC and one workload, not a universal low-end-PC guarantee.

| Recording mode | App | Mean CPU, all four cores | Mean working set | Mean private bytes | Native dropped / skipped frames |
| --- | --- | ---: | ---: | ---: | --- |
| AMD H.264, two runs | Rilmo | 2.50% | 173.09 MiB | 155.92 MiB | 0 / 0 |
| AMD H.264, two runs | OBS | 2.23% | 117.77 MiB | 139.21 MiB | 0 / 0 |
| x264, one run | Rilmo | 4.44% | 186.84 MiB | 153.67 MiB | 0 / 0 |
| x264, one run | OBS | 4.39% | 132.26 MiB | 136.43 MiB | 0 / 0 |

For the two GPU trials, idle CPU averaged 0.53% for Rilmo and 1.06% for OBS; idle working set averaged 110.37 and 86.61 MiB respectively. Rilmo's idle private bytes were lower (53.59 versus 98.09 MiB), which does not imply lower total RAM use. Working set and private bytes measure different things.

The UI-plus-all-child-process scope includes native mux/browser helpers if present. CPU is normalized across the four logical cores, so 100% means the whole CPU. Summed working sets can double-count shared pages; private bytes are reported separately. The synthetic target, DWM, GPU driver and other apps are excluded. Background system CPU during recording varied around 26–36%; this desktop was not a clean laboratory environment. Differences this small in CPU are not statistically conclusive.

## Matched workload

- One fresh animated 1280x720 owned window per trial, captured using Windows Graphics Capture, client area, cursor off. Moving blocks are a repeatable synthetic workload, not a demanding game.
- Canvas/output 1280x720, 30 fps, NV12, Rec.709 partial range, bicubic scaling. Preview off, UI minimized, floating controls off, no audio inputs; silent 48 kHz AAC track at 160 kbps.
- AMD H.264: CQP 23, speed preset, high profile, keyframe interval 2 seconds, B-frames 0, pre-analysis/lookahead off. Software: x264 CRF 23, ultrafast, zerolatency, high profile, keyframe interval 2 seconds, two encoder threads.
- Ten-second idle sample; first two samples discarded. Forty-five-second recording; first five discarded. Start/stop finalization adds a small amount to file duration. GPU order Rilmo/OBS/OBS/Rilmo, then one Rilmo/OBS software pair.
- Actual Rilmo WPF frontend and official OBS portable frontend. Rilmo benchmark entry is compiled only under `RILMO_TEST`; the shipped build excludes it. The benchmark OBS copy has its own config and does not modify the user's OBS installation or profile.
- Startup measurements are intentionally not compared: Rilmo initializes capture and waits for encoded frames, whereas OBS's websocket reports an active, already initialized output. These are different boundaries.

All six output files decoded completely with FFmpeg, contained nonblank moving video, and reported 30 active fps and zero native drop/skip counters. Raw evidence: `artifacts/matched-benchmark-20261022-070302/results.json`, per-trial `samples.json`, encoder/capture settings and `artifacts/recording-validation-0.4.json`. Harness: `tools/matched-benchmark.py`, `src/UiBenchmark.cs` and `tools/verify-current-recordings.py`.

The harness initially hit asynchronous OBS frontend startup timeouts. Those attempts are retained as diagnostic directories and were not measured successes. Successful retries use fresh app-owned portable configuration and a five-second startup settling period. No result was selected based on speed.

## Product implications

Rilmo's on-demand preview, lazy native initialization, default 720p30 hardware preset, inactive media cleanup and suppressed hidden UI work are useful resource controls. They do not erase WPF frontend overhead. Browser pages, image caches, active filters, multiple audio encoders and high resolutions add real cost. A lightweight product claim needs broader repeated tests with audio, complex scenes, games, multiple GPUs, thermals and memory pressure. Store certification is a separate requirement.
