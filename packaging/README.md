# Microsoft Store packaging scaffold

This folder prepares an eventual **x64 MSIX desktop package**. It is intentionally blocked for the current development build. It does not copy the installed OBS runtime, sign a package, install an app, submit to Partner Center, or certify the product.

The full preview now includes an independent Windows 10 DirectShow virtual camera. Its administrator-approved setup and global device uninstall are **not integrated into this MSIX scaffold**. Preflight rejects a camera-equipped build rather than creating a package with an unusable setup action. A signed EXE/MSI Store installer is a candidate for the full Windows 10 feature set; its offline/silent setup, versioned hosting, trusted PE signing, upgrade/rollback, device cleanup, and certification must be implemented and verified. The standalone setup script is a reviewable local setup tool, not a complete Store installer.

Optional browser and camera component provenance must also pass source/redistribution review. The browser inventory is hash-checked separately from core OBS. The privacy/network review must cover the automatic win-capture compatibility request as well as user-selected browser and streaming URLs; do not claim zero automatic network traffic.

`build-offline-preview.ps1` and `offline-preview.nsi` prepare an **unsigned local offline EXE installer candidate** using pinned NSIS. Its design includes Program Files installation, Start menu/Add or Remove Programs entries, silent `/S` operation, exact-file cleanup, and delegation to the owned camera cleanup script during uninstall. Camera registration remains a separate user-approved action. This is a concrete installer candidate for testing, not the public release path: compilation does not establish install/upgrade/rollback/uninstall behavior, clean-machine compatibility, trusted signing of every PE, or Store acceptance.

## Files

- `AppxManifest.template.xml`: unresolved Partner Center identity and tested-OS placeholders.
- `release-config.example.json`: release inputs, artifact locations, and evidence locations.
- `runtime-inventory.example.json`: shape of the required runtime file/hash and component/license inventory; examples are deliberately incomplete.
- `generate-assets.ps1`: generates six original geometric PNG logos at 100% and 200% scale.
- `package.ps1`: validates release gates; after they pass, stages and invokes the Windows SDK's MakeAppx to create an **unsigned candidate**.
- `release-checklist.md`: work required for a one-click install and Store release.

All paths in the configuration are relative to the configuration file. Keep a filled configuration beside the example, normally `packaging/release-config.json`. Do not commit publisher secrets or signing credentials; this script does not need them.

Run the current preflight from the workspace:

```powershell
.\packaging\package.ps1 -CheckOnly
```

An error listing missing publisher, runtime, source, privacy, and test artifacts is the expected result today. No package is produced. Do not replace missing evidence with empty files or mark an untested release as complete.

After the checklist is actually complete:

```powershell
.\packaging\package.ps1 -ConfigPath .\packaging\release-config.json -CheckOnly
.\packaging\package.ps1 -ConfigPath .\packaging\release-config.json
```

The script finds `makeappx.exe` on PATH or in the existing local Windows SDK buildtools folder; another SDK may be selected with `-MakeAppxPath`. It uses normal MakeAppx validation, with no `/nv` bypass. Each attempt gets a unique `packaging/out/<version>-<id>` staging directory. It does not delete earlier artifacts.

The config/evidence checks catch missing inputs; they cannot prove that a public URL serves the required source, that source matches a binary, that a review was competently performed, or that the application passes certification. Those remain release-review responsibilities. A successful `-CheckOnly` is not a Store approval.

The application gate requires a self-contained .NET 10 publish: a readable `Rilmo Recorder.runtimeconfig.json` (up to 64 KiB), `tfm: net10.0`, and `includedFrameworks` containing `Microsoft.NETCore.App` and `Microsoft.WindowsDesktop.App` with stable `10.0.x` versions. Missing, malformed, duplicate, or non-.NET-10 framework entries are rejected, as are framework-dependent `framework`/`frameworks` references. The native `hostfxr.dll`, `hostpolicy.dll`, and `coreclr.dll` must also report .NET 10.0 file and product versions. Publish into a clean directory when upgrading .NET; editing runtimeconfig alone cannot make an older runtime valid. This gate checks the runtime generation, not complete binary/source consistency or runtime functionality.

The corresponding-source ZIP must place `LICENSE`, `THIRD-PARTY-NOTICES.md`, `build.ps1`, `src/Rilmo.Recorder.csproj`, and `release-source-manifest.json` at those root-relative paths. The release source manifest should map each shipped component to its included corresponding source/build instructions, explaining any applicable system-library exception. These structural checks do not establish completeness by themselves.

## Bundled runtime and remaining release review

The engine and MP4 exporter now resolve only the application directory and its `runtime/obs` subdirectory. Native helpers and their dependency DLLs sit beside the application executable, so no managed forwarding process, installed OBS application, PATH change, or `RILMO_OBS_ROOT` override is used. See [standalone runtime preparation](../docs/standalone-runtime.md).

The local runtime cache is hash-inventoried, but its current fallback provenance and incomplete corresponding-source/redistribution review still block a public Store candidate. `correspondingSourceComplete` and `publicDistributionApproved` must remain false until those reviews are actually completed. The staging contract is:

```text
AppxManifest.xml
Assets/
app/
  Rilmo Recorder.exe
  Rilmo Recorder.dll
  ...self-contained .NET files...
  obs.dll
  ...selected native dependency DLLs, including app-local VC++ runtime...
  obs-ffmpeg-mux.exe         # genuine native helper
  ...selected native probe helpers...
  LICENSE
  THIRD-PARTY-NOTICES.md
  RELEASE-LINKS.txt
  runtime-inventory.json
  runtime/obs/
    obs-plugins/64bit/
    data/libobs/
    data/obs-plugins/
    licenses/
```

The script requires a complete runtime inventory and rejects runtime roots under Program Files. Every payload file must have a matching SHA-256; the embedded `runtime-inventory.json` must match the configured inventory. Component entries must identify version, license, source, and included license text. `tools/stage-runtime.ps1` maps the cache's `bin/64bit` files to the app directory and other files to `runtime/obs`, avoiding duplicate native libraries. Optional encoders need the exact helpers and dependent DLLs they use. Test helper DLL discovery from the final packaged location and on the clean-machine matrix.

The current local preview implements this layout. Public source delivery, the complete third-party component audit, redistribution rights, and installed MSIX validation remain separate release requirements.

## Manifest choices

- `Windows.Desktop`, x64, Windows 10 **19041** minimum. This product baseline supports the explicit `uap10:RuntimeBehavior="packagedClassicApp"` and `uap10:TrustLevel="mediumIL"` declarations. It is more specific than OBS's Windows 10/11 requirements and must be validated against the bundled native build and the supported hardware. `MaxVersionTested` is deliberately unresolved until the test matrix is completed. [Microsoft activation schema](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-f-application), [OBS requirements](https://obsproject.com/kb/system-requirements)
- `runFullTrust` is required for the full-trust WPF/native process. `microphone` and `webcam` reflect optional audio and camera-source features. A medium-integrity desktop app is not an AppContainer; do not add `internetClient`, `broadFileSystemAccess`, or video-library permissions just because it writes files or uses streaming. Review device consent/denial against the exact installed-package APIs. [Capability model](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations)
- Native Win32/libobs capture is not the same API contract as `GraphicsCapturePicker` or `GraphicsCaptureItem.TryCreateFromWindowId`. If the engine changes APIs, review whether `graphicsCapture`, `graphicsCaptureProgrammatic`, or `graphicsCaptureWithoutBorder` is needed. No border suppression is requested by this template.
- No administrator elevation, startup task, NT service, driver, virtual-camera registration, or external updater is declared. Any such future feature requires a new packaging and consent review.

Proposed certification note, to use **only after it matches the release**: “Rilmo Recorder is a full-trust WPF desktop screen recorder. Native libobs modules acquire the user's selected display and optional system/microphone audio, then write a local recording to the chosen folder. Full trust is used for the desktop capture/encoding pipeline and local file access. Recording begins through the user's Record action and remains visible until stopped. The package includes its versioned runtime and works without installing OBS separately.”

Replace that note if the actual behavior differs. Do not paste it into Partner Center until the full package passes the remaining release gates.
