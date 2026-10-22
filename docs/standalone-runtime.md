# Standalone native runtime

Capture and MP4 export now share `RuntimeLocator`, which resolves only the application's directory and its `runtime/obs` subdirectory. It has no installed-OBS, current-directory, PATH, or `RILMO_OBS_ROOT` fallback. Helpers are genuine native OBS executables, not managed relays.

## Build and acquisition

```powershell
.\tools\fetch-runtime-notices.ps1
.\tools\prepare-runtime.ps1 -VcRuntimeDirectory 'C:\path\to\audited\x64\VC-redist'
.\build.ps1 -Publish
```

The notice fetch needs PowerShell 7 and internet access. Runtime preparation resumes the pinned official portable archive download, verifies its SHA-256, and extracts selected components; no OBS or VC++ installer runs. The VC++ directory must contain architecture-matched redistributable DLLs. Signature validation is necessary but does not establish redistribution entitlement. End users do not need the SDK, PowerShell, or acquisition tools.

For an explicitly inventoried local build when the download is unavailable or impractical:

```powershell
.\tools\prepare-runtime.ps1 -NoDownload -AllowInstalledFallback
.\build.ps1 -Publish
```

That fallback reads the existing OBS 32.2.2 installation and Microsoft-signed VC++ DLLs, then creates a separate cache. The resulting application does not use those machine-local paths. On 22 October 2026 the official 187,851,583-byte archive finished downloading and matched upstream SHA-256 `4d6e40e3ab155f56b30de517380566a206d74b63cdf5ad49aa596924768f97e1`. A separate official preparation was compared against the tested fallback: all 1,107 selected payload files matched byte-for-byte; only provenance metadata differed. Canonical cache metadata now records the official OBS acquisition. The three VC++ runtime files still have explicit signed-System32 fallback provenance and require a distribution-rights review. Evidence is in `artifacts/runtime-official-comparison.json`.

Preparation refuses to overwrite an existing cache. Use `-Destination` with a new workspace path to reacquire, then `build.ps1 -RuntimeDirectory <path>` to choose that cache. Staging verifies every payload hash. Publish builds into a new folder and retains the previous distribution; if the previous app is running, the new standalone build gets a separate directory. Failed builds leave the prior working app intact.

## Pinned provenance

| Artifact | Pin |
| --- | --- |
| [OBS 32.2.2 source](https://github.com/obsproject/obs-studio/tree/32.2.2) | Commit `ba2f32bdf791005443988a4955e963663e16b1ed` |
| [Windows x64 portable ZIP](https://github.com/obsproject/obs-studio/releases/download/32.2.2/OBS-Studio-32.2.2-Windows-x64.zip) | SHA-256 `4d6e40e3ab155f56b30de517380566a206d74b63cdf5ad49aa596924768f97e1` |
| [OBS source archive](https://github.com/obsproject/obs-studio/releases/download/32.2.2/OBS-Studio-32.2.2-Sources.tar.gz) | SHA-256 `ec81fb66b03e75ddb3076b576f62679c39262e0e9960cef3e17a40dc5d68e6b4` |
| [OBS dependency selection](https://github.com/obsproject/obs-studio/blob/32.2.2/CMakePresets.json) | Prebuilt obs-deps `2026-07-15`; Windows x64 archive SHA-256 `6f90e9598fa10cff5ad23cdcfae49b87868c07bf896b02cd464582b4ce2f2ba9` |
| [obs-deps notices/build source](https://github.com/obsproject/obs-deps/tree/2026-07-15) | Commit `8683107a02300923abe4f293920f4b5edc8cb624` |

Release archive hashes came from the official GitHub release API. The dependency ZIP hash comes from OBS's pinned CMake configuration. Source URLs and hashes establish provenance; the complete corresponding-source delivery has not been assembled or published. Verbatim upstream notices and their file hashes are included under `third_party/obs-32.2.2` and copied into the runtime. This broader notice collection is not a declaration that all listed components are shipped.

## Layout

```text
Rilmo Recorder.exe
Rilmo Recorder.dll
...self-contained .NET 10 runtime...
obs.dll
libobs-d3d11.dll
libobs-winrt.dll
...selected native dependency DLLs, including VC++...
obs-ffmpeg-mux.exe
obs-amf-test.exe
obs-qsv-test.exe
obs-nvenc-test.exe
runtime-staging.json
runtime/obs/
  obs-plugins/64bit/
  data/libobs/
  data/obs-plugins/
  licenses/
  runtime-inventory.json
  provenance.json
  native-imports.json
```

The core selected plugin set is `win-capture`, `win-wasapi`, `obs-ffmpeg`, `obs-x264`, `obs-nvenc`, `obs-qsv11`, `win-dshow`, `image-source`, `obs-text`, `obs-filters`, `rtmp-services`, `obs-outputs`, and `obs-transitions`. Presence alone does not claim every upstream feature is exposed or tested. Feature code initializes modules when needed. Browser/CEF/Qt and the independent Rilmo Virtual Camera are separately inventoried and staged components in the full build; see [browser and game sources](browser-and-game-sources.md) and [virtual camera](virtual-camera.md). OBS's application interface and unrelated plugins are excluded.

`PeImports.cs` reads normal and delay-load PE imports without executing binaries. Preparation follows the selected native dependency closure and records remaining Windows imports. D3D11/WinRT libraries are explicit dynamic seeds. Module data, shaders, locales, and x86/x64 capture hooks/helpers are retained. GPU driver libraries remain supplied by supported GPU drivers; this bundle installs no drivers.

Cache `bin/64bit` files map directly beside the app, where libobs launches helpers. Shared DLLs resolve there without a forwarding process, changed PATH, or duplicate native library tree. Other files map under `runtime/obs`.

## Validation and release boundary

The isolated native-load audit at `artifacts/runtime-validation/0c268c39d1fe4b6da135151ad7edfe0f/native-load-validation.json` loaded the three core/graphics libraries and all selected plugin DLLs. It recorded 32 app-local native modules and no installed OBS module. The native mux helper started and returned its expected missing-filename error. This validates dependency loading; it initializes no capture session and does not prove plugin functionality.

Engine, UI, and remux smoke scripts now stage the same runtime rather than helper relays. Application tests must validate actual recording, device handling, composition, replay, streaming, and export. The current machine still has OBS installed: removal of fallback and observed app-local modules are local evidence, while an OBS-free clean VM and installed MSIX remain necessary release tests.

This is a standalone local preview, not a Store-approved distribution. Complete the binary/component license mapping, corresponding dependency source/build scripts, VC++ redistribution entitlement, source delivery, clean-machine support matrix, MSIX installation/update tests, and product checks. `correspondingSourceComplete` and `publicDistributionApproved` remain false; packaging rejects them for a public candidate.
