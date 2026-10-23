# Runtime source/build correspondence — 0.4.2 review

This records inspected inputs and upstream build entry points. It is not a completed native rebuild, a claim of complete transitive source delivery, or permission to distribute an unfinished combined binary. Timestamp-identical output hashes are not required for corresponding source, but the actual sources, changes and necessary build/install instructions must be supplied or clearly accessible.

## OBS and selected modules

The official OBS 32.2.2 complete-source archive includes submodules and is pinned in `release/source-inputs.manifest.json`. The selected upstream revision is `ba2f32bdf791005443988a4955e963663e16b1ed`. The Windows preset in its actual `CMakePresets.json` is `windows-x64`; the matching build preset uses `RelWithDebInfo`. The upstream entry points are `cmake --preset windows-x64` and `cmake --build --preset windows-x64`. Read that source tree's documented compiler/SDK prerequisites first. Its preset can acquire prebuilt dependencies; that alone does not rebuild those libraries from source.

The exact dependency recipe archive is obs-deps revision `8683107a02300923abe4f293920f4b5edc8cb624`, release 2026-07-15. Its `Build-Dependencies.ps1` requires PowerShell 7.2 or later and accepts `-Target x64`, `-Configuration Release`, and `-PackageName dependencies`, `ffmpeg` or `qt`. The source, checksum and patch recipes are included, rather than replaced with an upstream homepage. These entry points have been inspected; a complete Windows toolchain rebuild of the native set has not been run here.

`tools/prepare-runtime.ps1` selects win-capture, win-wasapi, obs-ffmpeg, obs-x264, obs-nvenc, obs-qsv11, win-dshow, image-source, obs-text, obs-filters, rtmp-services, obs-outputs and obs-transitions. It includes the upstream mux and encoder probe helpers and computes the import closure. `tools/stage-runtime.ps1` records their final placement. Store staging removes legacy virtual-camera registration files and uses the Microsoft desktop framework for C++ runtime imports. The original acquisition inventory remains distinct from the staged Store scope.

## FFmpeg and static inputs

The shipped avcodec library reports GPL version 3 or later. Its inspected configure arguments match `deps.ffmpeg/99-ffmpeg.ps1` at the pinned obs-deps revision: MSVC x86_64/win64, shared libraries, GPL/version3 enabled, libaom, SVT-AV1, Theora, LAME, x264, Opus, Vorbis, VPX, RIST, SRT and zlib; JACK, SDL2, docs and debug are disabled. Core sources and exact patches/checksums are collected by `prepare-release-sources.ps1`. The combined application uses the GPLv3-or-later election.

Inspected transitive-source examples: the RIST source archive contains its vendored cJSON and mbedTLS code under `contrib`; SVT-AV1 includes cpuinfo/clog source; mbedTLS 3.6.4 includes Everest and p256-m source. mbedTLS's separate framework submodule/build generation still needs a build-input determination (`GEN_FILES=OFF` in the selected recipe). libdatachannel is present in the broader acquired dependency set but no libdatachannel DLL is in the selected core import inventory; do not treat every dependency recipe as a shipped component. A full component mapping remains open.

## Qt

Only Qt6Core, Qt6Gui and Qt6Widgets 6.11.1 are selected. Their full qtbase source and the exact `0001-QTBUG-146665.patch` are pinned. The Windows `qt6.ps1` recipe uses shared libraries, bundled double-conversion, JPEG, PCRE2, PNG and zlib, enables HarfBuzz and Schannel, disables OpenSSL, FreeType, Brotli, D-Bus, GLib, ICU, PDF, print support, SQL, qmake, testlib and deployment tooling. The source/license notices cover a broader set than those enabled features. The selected combined GPLv3 distribution can use Qt's GPLv3 option; do not impose proprietary-application LGPL assumptions on that election. Third-party notices and the final enabled-feature correspondence still require reconciliation.

## CEF and Chromium

The binary distribution identifies CEF `127.145.7+g2b7d20b+chromium-127.0.6533.120`, repository `https://github.com/pkviet/cef.git`, commit `2b7d20b8cdcba6e4e9507a1015b8262515a196c6`. The acquired source mirror uses the same Git commit. Chromium is `c51f2916e4a14157c276fd33c8bfb24155aac1ad`, tag 127.0.6533.120; depot_tools compatibility is `22f6c9d8e9`. The exact Chromium root source archive is acquired and its DEPS matches the pinned file. It excludes external Git/CIPD checkouts.

The pinned CEF source includes `tools/automate/automate-git.py` and its Chromium patch series. Its inspected options support `--branch=6533`, `--url=https://github.com/pkviet/cef.git`, `--checkout=2b7d20b8cdcba6e4e9507a1015b8262515a196c6`, `--chromium-checkout=c51f2916e4a14157c276fd33c8bfb24155aac1ad`, `--x64-build`, `--no-build` and `--no-distrib`. Those last two permit preparing sources before an intentional build. Use the pinned script's help and compatibility file rather than downloading current master. Do not execute a large checkout/build on a user's PC without checking its toolchain/storage requirements.

Remaining: establish the relevant Windows DEPS/recursive source access and exact shipped CEF build configuration, then provide verified acquisition/build instructions with the release. `Chromium-DEPS-index.json` is a non-executing review aid, not a completed dependency checkout. The minimal CEF binary ZIP contains wrapper sources and notices, not all Chromium sources. The public application repository and a credits page alone do not close this work.
