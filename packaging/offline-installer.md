# Offline preview installer

This is an unsigned local review candidate, built with the verified portable NSIS 3.13 compiler. The installer carries the selected standalone .NET 10 application, OBS runtime, optional browser runtime and Rilmo camera components. It downloads nothing during setup. The running application can still request OBS compatibility data, user-selected browser URLs and streaming destinations; “offline installer” does not mean the application never uses the network.

## Build and inspect

```powershell
# Generate the exact payload manifest and NSIS includes without compiling.
.\packaging\build-offline-preview.ps1 -AppDirectory 'C:\Video Recorder\dist\Rilmo Recorder standalone-20261022-052151' -CheckOnly

# Compile only. This command never launches the resulting installer.
.\packaging\build-offline-preview.ps1 -AppDirectory 'C:\Video Recorder\dist\Rilmo Recorder standalone-20261022-052151'
```

Omitting `AppDirectory` uses the validated published-build pointer. The script requires a self-contained .NET 10 payload inside this workspace's `dist` folder, rejects linked paths before descending into them, includes hidden files, and rejects a previously installed tree. It checks the pinned compiler and license hashes. The default executable is `.tools/nsis-3.13/Bin/makensis.exe`; the acquisition receipt and complete upstream license are beside that compiler's root. Arbitrary compiler overrides are rejected.

Output is a new `packaging/out/offline-preview-<timestamp>-<id>/` directory containing generated includes, `payload-manifest.json`, the setup executable and a successful `installer-receipt.json`. The app DLL determines the product/file version. The script hashes every input before compilation and rechecks those files afterwards; a changed input prevents a successful receipt. The receipt records the installer hash, payload count and explicit false install/uninstall/rollback/public-distribution qualification flags. NSIS uses zlib compression. Its complete `COPYING` file is embedded as `_installer/NSIS-COPYING.txt`, alongside the exact payload manifest. The license page displays the bundled GPL-3.0 text for this combined binary distribution's selected license route; original application/upstream license texts remain included in the payload.

## Implemented installation behavior

- Requires administrator elevation, x64 Windows build 19041 or later, and a fixed destination under 64-bit Program Files. The candidate ignores a custom `/D` destination.
- Refuses an existing installation, a nonempty destination or nonempty matching Start menu folder. In-place upgrades are deliberately unsupported; use the previous uninstaller first. No automatic rollback is claimed.
- Checks the recorder's current-session mutex and serializes its own installers/uninstallers with a machine-wide setup mutex. It does not terminate recording or camera processes. Another user's running session can instead cause file locks; this still needs multiuser qualification.
- Checks paths and ancestors for reparse points or inspection errors. Checks copy, uninstaller, registry and shortcut failures and returns nonzero status. It writes the exact-file uninstaller before payload extraction so a partial copy can be removed.
- Adds the app and uninstall Start menu shortcuts plus the Programs and Features removal entry. It does not start capture, launch the app, register a camera, install a service or alter the default camera.

## Implemented removal behavior

The uninstaller only accepts the fixed install root. It checks all owned payload/metadata paths and their ancestors before deletion. Its generated file list is embedded in the uninstaller; it does not trust an editable disk manifest to choose deletion targets. It deletes exact filenames and only attempts nonrecursive directory removal. Unrelated files, user recordings and preferences survive.

If Rilmo's camera CLSID or retained camera-cleanup receipt exists, removal first invokes the bundled x64 PowerShell cleanup script. That script validates its own CLSID, fixed root and exact owned file receipt. Failure to launch it, a nonzero exit or a retained cleanup receipt stops app removal while the cleanup tool is still available. Camera removal is never performed while compiling this installer.

An ordinary payload deletion failure retains the embedded uninstaller, removal registration, manifest and camera cleanup script for retry. The script is deleted after the remaining payload succeeds. Metadata and removal-registration cleanup occur at the end; failure to delete the uninstaller itself attempts to restore the removal entry. Empty-directory cleanup is best effort, allowing custom files to remain. These are implemented safeguards, not proof of crash-atomic installation or removal.

Exit statuses include `0` for successful completion, `1603` for a failed operation/path check, `1618` for a running recorder or unavailable setup lock, `1633` for an unsupported platform, and `1638` for an existing installation. Silent `/S` uses the same checks and nonzero failures.

## Validation and remaining gates

The template has been compiled with NSIS 3.13 using a minimal compile-only payload; this checks its instruction syntax, generated uninstaller and zlib selection. Exact manifest generation has been exercised against the current standalone output. The final candidate's successful compile/hash receipt is the evidence for the full payload build. Neither the installer nor uninstaller has been executed on this workstation, and no camera has been registered by this packaging work.

Before external distribution, test on disposable clean Windows 10/11 machines: interactive and silent install/uninstall, Unicode/spaced paths, standard-user elevation, app/camera locks, partial copies, disk full/access denied, malicious or stale junctions, missing cleanup tools, retry after interruption, custom-file preservation, multiuser sessions and older-build removal followed by reinstall. Include camera installation/removal and camera-client locking only in that explicit test environment. Qualify signing/SmartScreen and final Microsoft Store submission separately. In-place upgrades and automatic rollback remain unsupported. Source-completeness, corresponding-build and third-party license gates in the runtime/source manifests remain authoritative; compiling an installer does not clear them.
