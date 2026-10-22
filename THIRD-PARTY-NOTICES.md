# Third-party notices and bundled runtime

The application developed in this repository is licensed under **GNU GPL version 2 or, at your option, any later version** (`GPL-2.0-or-later`), unless a file carries a different compatible notice. The complete GPL version 2 text is provided in `LICENSE`. Third-party components retain their own copyrights and licenses. This application is independently developed and is not an official OBS Project product.

## OBS Studio / libobs

- Upstream: [OBS Studio](https://github.com/obsproject/obs-studio)
- Runtime baseline: **32.2.2**, upstream commit `ba2f32bdf791005443988a4955e963663e16b1ed`
- License: **GPL-2.0-or-later**, as declared by [libobs/obs.h at 32.2.2](https://github.com/obsproject/obs-studio/blob/32.2.2/libobs/obs.h)
- License text: [COPYING at 32.2.2](https://github.com/obsproject/obs-studio/blob/32.2.2/COPYING)
- Copyright: OBS contributors; preserve the notices in each upstream file. The libobs public header includes Lain Bailey's copyright notice.

The standalone application loads bundled native libraries and genuine native helpers. It does not locate or launch an installed OBS application. Runtime preparation prefers the pinned official portable archive. An explicit local fallback can copy inventoried files from an existing OBS 32.2.2 installation and Microsoft-signed VC++ runtime files; the resulting provenance identifies that fallback and does not claim those files match the official archive. This describes acquisition at build time, not an end-user installation dependency.

Exact runtime files, SHA-256 hashes, import closure, and acquisition origins are recorded in `runtime/obs/runtime-inventory.json`, `runtime/obs/native-imports.json`, and `runtime/obs/provenance.json` in a published local preview. `runtime-staging.json` maps cache paths to application-relative locations. **Public release remains gated:** complete the precise binary/component audit, corresponding dependency source and build scripts, redistribution rights, supported updates, and compliant source delivery. The local preview's `correspondingSourceComplete` and `publicDistributionApproved` flags remain false.

## Codec and optional dependencies

The selected binaries include FFmpeg, x264, curl, RIST/SRT, zlib, pthreads, and other code compiled into the selected OBS modules. The exact obligations depend on the build configuration. [FFmpeg's license inventory](https://github.com/FFmpeg/FFmpeg/blob/master/LICENSE.md) describes configuration-dependent LGPL/GPL terms and nonredistributable nonfree configurations. Audit the actual shipped build rather than assigning a license from a filename.

The pinned shipped `avcodec-62.dll` was queried directly on 22 October 2026: `avcodec_license()` reports **GPL version 3 or later** and `avcodec_configuration()` includes `--enable-gpl --enable-version3`. Its SHA-256 is `fc92fdfab7b36d35adbe2670238e03da2fb640b4f84fb575baa3e5440168c1ff`. The combined native distribution therefore requires the GPL version 3 or later election permitted by this project's GPL-2.0-or-later grant; GPL version 2 alone is insufficient for that combined payload. Full GPL version 3 text is included in `third_party/supplemental/GPL-3.0.txt`. This does not finish the dependency correspondence or distribution review.

## Rilmo Virtual Camera and browser component

The app-owned DirectShow component is derived from pinned OBS and LGPL-2.1-or-later libdshowcapture sources. Its modifications, source files, rebuild scripts, static compiler-runtime notices and exact hashes are in `native/virtual-camera`. The published folder includes those notices under `virtual-camera/LICENSES`; device installation is a separate explicit user action. No OBS device identity or default camera is changed.

The optional browser bundle includes CEF/Chromium and Qt 6.11.1 Core, Gui and Widgets. Its `runtime/browser/licenses` contains the matching CEF license, full extracted Chromium credits, Qt license texts, third-party notices and source-header evidence. `release/browser-source-inputs/manifest.json` records exact archives and hashes. The Chromium dependency checkout/build correspondence, Qt enabled-feature/relink review and final combined distribution audit remain incomplete. Full browser licensing cannot be inferred from CEF's BSD license alone.

`third_party/obs-32.2.2` contains verbatim upstream notice files checked against their Git blob hashes, the OBS `CMakePresets.json` dependency selection, and `source-notices.json` with exact URLs and SHA-256 hashes. Runtime preparation copies this collection under `runtime/obs/licenses/upstream`. It includes selected OBS source notices and the broader **obs-deps 2026-07-15** notice collection at commit `8683107a02300923abe4f293920f4b5edc8cb624`. A notice's presence does not mean every component from that broader dependency build is shipped. This is not a complete corresponding-source archive.

The final release must include the exact component/version/license mapping, copyright and patent notices, corresponding sources, and required build/modification details. Microsoft VC++ app-local DLLs and the self-contained .NET runtime retain their applicable Microsoft terms. Verify VC++ redistribution entitlement and security-update responsibilities before public distribution; a valid Microsoft signature does not by itself establish redistribution rights. [Microsoft redistribution guidance](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files)

## Doppio One interface font

Doppio One is copyright 2011 Sorkin Type Co, with the reserved font name Doppio, and is distributed under the SIL Open Font License 1.1. The unchanged Google Fonts font is embedded locally; no font request is sent at runtime. The full notice is in `assets/fonts/OFL.txt` and the installed `licenses/DoppioOne-OFL.txt`. Pinned revision, download URLs and hashes are recorded in `assets/fonts/provenance.json`.

## Main license provenance

The `LICENSE` file was retrieved verbatim from OBS Studio tag `32.2.2`'s `COPYING`, which contains the Free Software Foundation's GNU General Public License version 2 text. Its permission permits verbatim reproduction of the license document. The application's “or later” election is stated above; it does not change the GPL text itself.

No public source site or Microsoft Store release is created by these scripts. See [docs/standalone-runtime.md](docs/standalone-runtime.md) for the bundle and [docs/store-and-licensing.md](docs/store-and-licensing.md) for unfinished release gates.
