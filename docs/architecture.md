# Rilmo Recorder — implementation decisions

Date: 22 October 2026. Current user direction: an original black/red recorder interface, draggable quick controls and an open-source distribution model. This supersedes the earlier charcoal/mint direction.

## Product intent
One-click local recording first; the full OBS feature surface remains the long-term requirement. This repository must distinguish working features from planned parity. No claim of best-in-class performance is justified without repeatable comparisons.

## Engine and interface
- Native libobs 32.2.2 with an original WPF interface. GPL-compatible distribution, no OBS branding or copied UI assets.
- .NET 10 LTS for the application host. Initial engine/UI evidence was gathered with .NET Framework/.NET 8 and remains labeled accordingly; the .NET 10 build has since passed recording, pause/resume, stop and MP4-export integration checks. Microsoft's [support policy](https://dotnet.microsoft.com/en-us/platform/support/policy) gives .NET 8 an end-of-support date of 10 November 2026, so it is unsuitable as the foundation for this new Store release.
- Initialize capture on explicit Record, Replay, or Go live. No idle capture, Chromium browser process, or continuously rendered preview in the default workspace.
- Load only required capture, audio, encoding and mux modules. Camera/image/text composition, selected audio filters, replay, and RTMP/RTMPS streaming now have explicit feature paths and validation; browser sources and virtual camera remain separate milestones. Linking libobs alone does not establish complete OBS parity.
- Prefer hardware H.264 when supported; use a conservative software fallback with clear reporting. Defaults target 720p/30 for low-end machines. Quality controls remain explicit.
- Work off the UI thread; serialize engine operations. Stop must await file finalization before reporting success. Recording state must be visible in the window and tray.
- Keep recordings local; transmit selected picture/audio only when the user starts live streaming to a chosen endpoint. No analytics account, automatic background upload, or automatic startup.

## Delivery sequence and gates
1. Artifact identity + source cross-check, licensing and Store requirements.
2. Real record/pause/stop lifecycle, monitor and audio selection, presets, hotkeys and local library.
3. Profile measured CPU/RAM/GPU, frame timing, startup, output correctness and repeated lifecycle stability on low-end hardware.
4. Expand feature parity in independently tested batches; see product-and-performance-plan.md.
5. The local preview now bundles a pinned, hash-inventoried runtime and native helpers. Complete the reproducible binary/source audit, corresponding-source delivery, clean-machine tests, signing, and Store certification before public distribution. See standalone-runtime.md for the explicit local fallback provenance and removed installed-OBS dependency.

## Current REA boundary
REA identified the installed obs64.exe and inspected its PE resource tree. Deep decompilation is unavailable because no Ghidra/IDA provider is configured. Static resource evidence does not explain performance or runtime behavior. Evidence is retained in ../obs-rea-evidence.json; further findings must retain this distinction.

## Benchmarks
Use identical capture content, dimensions, encoder, quality target and FPS against OBS, repeated at least three times. Record mean/p95 CPU and RAM, capture/encode lag, output frame count, audio synchronization, file size, GPU engine load and cold/warm launch. Never compare unlike quality settings and call it an optimization.
