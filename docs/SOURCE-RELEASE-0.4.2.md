# Rilmo Screen Recorder 0.4.2 source delivery

Publisher: Muhammad Hasnain — hasnainiqbal4665733@gmail.com.
This source version is used by the first Store package, whose separate MSIX
version is 1.0.0.0. The combined OBS/FFmpeg application uses GPL-3.0-or-later;
original application files also permit GPL-2.0-or-later. Component copyrights,
licenses and font notices remain applicable.

## Download and layout

The source release is at
https://github.com/Hasnain4700/rilmo-screen-recorder/releases/tag/v0.4.2-source.
The source-and-build-inputs ZIP contains the application, source manifests,
OBS complete sources and dependency recipes/patches, acquired core dependency
sources, Qt sources and patch, CEF sources and Chromium credits. Its manifest
also identifies large externally hosted source inputs by exact revision, URL,
byte length and SHA-256. The Chromium root source and external Git checkouts
are supplied by equivalent public source access, not claimed to be physically
inside this ZIP. There is no charge or sign-in requirement for those sources.

The application repository is
https://github.com/Hasnain4700/rilmo-screen-recorder. Use the commit recorded in
the release, not an arbitrary future main-branch version.

## CEF and Chromium

The unchanged CEF binary SDK identifies CEF
127.145.7+g2b7d20b+chromium-127.0.6533.120, CEF commit
2b7d20b8cdcba6e4e9507a1015b8262515a196c6, and Chromium commit
c51f2916e4a14157c276fd33c8bfb24155aac1ad (127.0.6533.120).
The CEF source contains the OBS shared-texture/stutter patches used by this
revision; a newer fork head is not substituted.

- CEF source and patch series:
  https://codeload.github.com/chromiumembedded/cef/zip/2b7d20b8cdcba6e4e9507a1015b8262515a196c6
- Complete Chromium root source (external Git dependencies are separate):
  https://codeload.github.com/chromium/chromium/tar.gz/c51f2916e4a14157c276fd33c8bfb24155aac1ad
  SHA-256: 916b55c8af15b48f4efe6a54dc325691f1393227b2ef3f9c433ecaa7ca5eec40.
- Chromium Git repository:
  https://chromium.googlesource.com/chromium/src.git
- Chromium FFmpeg source is separately included at commit
  bc1ee906cede137c061bea5a5e8d0743374392a5.

`release/browser-source-inputs/Chromium-DEPS` is the exact root dependency
lockfile. `source-access-verification.json` records successful public commit
lookups for all 121 Git inputs selected by its Windows x64 conditions, and
copies of all four selected recursive DEPS files. Android/iOS/internal-only
inputs excluded by those conditions are recorded separately. gclient follows
the selected recursive DEPS and retains their pinned revisions. The full
unfiltered dependency index is also included. The lookup verification did not
execute DEPS, fetch every dependency checkout or execute its build hooks.

To prepare the full CEF/Chromium source checkout on a Windows build machine:

```powershell
pwsh ./tools/fetch-cef-corresponding-source.ps1 -DownloadDirectory C:/RilmoCefSource
```

The pinned upstream automation selects depot_tools 22f6c9d8e9, CEF 6533 and the
exact Chromium revision, resolves recursive DEPS, and applies the CEF patch
series. Install the prerequisites documented by this revision: Python, Git,
Visual Studio 2022 C++ tools and the Windows SDK (CEF 6533 documents VS 17.9.2
and SDK 22621). A full checkout/build requires substantial disk space; use a
build machine, not a recorder user's low-resource PC. `-BuildRelease` enables
the release build after preparation. The supplied GN profile uses
`is_official_build=true`. This is a rebuild instruction, not a claim that the
original producer's complete GN command line was recovered or that outputs
will be byte-identical. Rilmo makes no local changes to those vendor binaries.

## OBS, Qt and codec libraries

OBS 32.2.2 is ba2f32bdf791005443988a4955e963663e16b1ed. Its included complete
source archive contains its submodules, including obs-browser
3f0a2cdf378939ebe3c6f9ab36d4ea100c25aac2. Build with the actual upstream
`windows-x64` CMake configure/build presets and `RelWithDebInfo` configuration.

The dependency source, build scripts and patches are pinned to obs-deps
8683107a02300923abe4f293920f4b5edc8cb624 (2026-07-15). Its PowerShell 7.2+
entry point is `Build-Dependencies.ps1 -Target x64 -Configuration Release`
with `-PackageName dependencies`, `ffmpeg` and `qt`, respectively. The core
source manifest supplies the exact input versions and SHA-256 values. FFmpeg
8.1.2's inspected build enables GPL and version 3, and matches the included
recipe. RIST's vendored cJSON/mbedTLS and SVT-AV1's cpuinfo/clog source are in
their acquired archives. The selected mbedTLS recipe disables generated
files, programs and tests (`GEN_FILES=OFF`); the pre-generated library files
are supplied in that source archive.

Qt6Core/Gui/Widgets 6.11.1 use the included qtbase complete source,
`qt6.ps1`, and `0001-QTBUG-146665.patch`. Use the GPLv3 option for this combined
GPL application. The script specifies bundled double-conversion, JPEG,
PCRE2, PNG and zlib, HarfBuzz and Schannel; it disables OpenSSL, FreeType,
Brotli, D-Bus, GLib, ICU, PDF/print/SQL and test/deployment tools. The complete
qtbase source and attribution files include the enabled bundled components.

## Build, install and modify Rilmo

Install the .NET SDK in `global.json`. Restore the locked project and publish:

```powershell
dotnet publish ./src/Rilmo.Recorder.csproj -c Release -r win-x64 --self-contained true -p:RilmoStoreEdition=true -p:RilmoTestMode=false -p:RestoreLockedMode=true -o ./dist/source-build
```

`prepare-runtime.ps1`, `prepare-browser-runtime.ps1`, `stage-runtime.ps1` and
`stage-browser-runtime.ps1` document the selected native layout. For modified
native libraries, replace the upstream build outputs and regenerate their
file inventories; do not pretend their hashes match the unchanged upstream
release. `configure-msix-payload.ps1` removes the four legacy OBS camera files
and eight app-local VC runtime copies. The Store package declares the separate
Microsoft desktop framework. There is no virtual-camera registration,
driver/service, proprietary activation key or Rilmo signature check required
to run a source build. You can run your modified unpackaged build directly,
or package under your own independent development identity/certificate.
Microsoft does not give third parties the publisher's Store signing keys.

The Microsoft framework and .NET's separately licensed runtime are not
relicensed by the GPL. Included license/notice files describe their terms.
Native vendor libraries were mapped to their versioned source/patch/build
inputs; a complete native toolchain rebuild was not performed here. This
source release does not assert timestamp-identical binary reproduction or a
physical-device compatibility matrix. Report source-link failures to the
public support address so the retained pinned source can be restored.
