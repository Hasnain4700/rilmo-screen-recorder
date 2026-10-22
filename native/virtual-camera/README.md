# Rilmo Virtual Camera

This is an app-owned, video-only Windows DirectShow camera for Windows 10/11 desktop camera consumers. It is derived from OBS Studio 32.2.2 and libdshowcapture, compiled locally from source, and does not use or register the OBS camera identity.

- Device: **Rilmo Virtual Camera**
- CLSID: `{9E7FF7BC-AA7A-4B1D-8A9E-DDDFB0692DFD}`
- Shared memory: `Local\RilmoVirtualCamVideo_9E7FF7BC_AA7A_4B1D_8A9E_DDDFB0692DFD`
- `rilmo-virtual-camera64.dll`: x64 filter and native libobs producer.
- `rilmo-virtual-camera32.dll`: x86 camera consumers. Both components have only Windows system DLL imports; external camera hosts need no OBS/.NET installation.

Starting the output attaches a native NV12 callback through `obs_add_raw_video_callback`, which activates the main canvas even when no recording encoder is running. It requires a first frame within five seconds before reporting success. There is no managed per-frame transition, encoding, network transmission, or microphone device. The triple buffer copies packed NV12 rows to shared memory; clients receive the selected composed scene. Stopping disconnects the callback under libobs's callback mutex before closing the buffer. An inactive neutral slate replaces the last picture in connected clients.

The device remains unregistered until the user explicitly runs setup. In a built app, review `tools/setup-virtual-camera.ps1 -Action Review`. The app's setup button explains the machine change and asks Windows for administrator approval. The script's `Install` and `Uninstall` actions require `-AcknowledgeSystemDeviceChange`; it never elevates itself. Install places versioned components under `%ProgramFiles%\Rilmo\Virtual Camera`, registers only the Rilmo CLSID in x64/x86 views, and never changes the default camera or begins capture. Its normal registry APIs add no kernel driver or NT service. Close camera clients before uninstalling. Setup is prepared; it has **not** been executed as part of automated testing.

## Rebuild

The exact upstream revisions, binary hashes, imports, and modified-source hashes are in `source-provenance.json`. `upstream/` contains 68 Git-blob-verified files from OBS commit `ba2f32bdf791005443988a4955e963663e16b1ed` and libdshowcapture commit `8878638324393815512f802640b0d5ce940161f1`. Only the filter/output-format/media-type code is linked; the unused device-specific submodules are not required.

Use the official [Zig 0.14.1 Windows archive](https://ziglang.org/download/0.14.1/zig-x86_64-windows-0.14.1.zip), SHA-256 `554f5378228923ffd558eac35e21af020c73789d87afeabf4bfd16f2e6feed2c`, extracted to `.tools/zig-x86_64-windows-0.14.1`. Run `tools/build-virtual-camera.ps1 -Architecture both`, then `tools/virtual-camera-smoke.ps1`. No global compiler installation is required. Build and setup scripts must be part of the corresponding-source release.

Changes made on 22 October 2026: separate device name/CLSID/IPC; independent native producer; original neutral slate; first-run default 720p formats without a persisted OBS settings file; correct stream-relative DirectShow timestamps; requested-interface handling in the class factory; padded NV12 row copying; duplicate-producer race rejection; shared mapping bounds checks; memory-order barriers; failed-allocation unmapping. On the pinned Clang toolchain, optimized x64 object initialization exhibited heap corruption; only that constructor uses `optnone`. Per-frame paths stay optimized. Release validation must retain repeated activation/stream/cleanup tests when changing compilers.

## Evidence and limits

Registration-free x86 and x64 tests instantiate the DLL directly through `DllGetClassObject`, connect it to the Windows Sample Grabber and Null Renderer, negotiate NV12, verify live frames, padded-width correctness, stopped slate, and COM cleanup. Separate engine tests send real libobs output through both consumer architectures and verify restart/duplicate-start/stop behavior. These tests do **not** establish successful persistent registration, browser/Teams/Zoom compatibility, Windows Camera/UWP compatibility, or certification. No claim of universal camera-app support is made.

The current MSIX scaffold does not install this external DirectShow registration. A release installer must own machine registration, upgrade, rollback, and uninstall; a signed EXE/MSI Store submission is a candidate route requiring policy review. The Windows 11 Media Foundation virtual-camera API needs build 22000+, so it does not replace this Windows 10 implementation.

Licenses are in `LICENSES/`; unchanged headers retain upstream notices. The combined camera component is distributed under GPL-2.0-or-later, with LGPL-2.1-or-later libdshowcapture, permissive utility code, and compiler-runtime notices. The complete source-delivery and static-runtime license review remain release gates. These local binaries are unsigned and are **not an approved public release**.
