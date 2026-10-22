# MSI development candidate

The Windows 10/11 DirectShow camera needs machine-visible registration. This MSI candidate includes the standalone app, bundled runtimes and both camera architectures. Windows Installer owns their files, class/category keys, repair and removal. Camera registration does not start capture, change a default camera, install a kernel driver or add an app service.

Build with `packaging/build-msi-candidate.ps1` after `build.ps1 -Publish`. WiX 4.0.6 and the pinned .NET SDK must be under `.tools`. Validation remains enabled. The native `camera-filter-data64/32.exe` helpers ask the Windows filter mapper to serialize the pin declaration without writing any registration. Both architectures must agree.

Each candidate has a fresh ProductCode and stable UpgradeCode. Each file has a stable component identity. `MajorUpgrade` runs inside the MSI transaction so a later failure restores the prior product. A read-only native launch action detects the current user's Rilmo mutex; setup refuses to interrupt an active app. Restart Manager shutdown is disabled. Existing portable/NSIS camera registrations and the earlier NSIS app are rejected rather than silently taken over. Runtime settings and recordings are outside the MSI component set.

`windows-installer.json` is generated into the MSI, contains its ProductCode and enables the app's Windows Installer repair path. The older portable setup script refuses Install/Uninstall when this marker is present. All camera keys are under the independent Rilmo CLSID `{9E7FF7BC-AA7A-4B1D-8A9E-DDDFB0692DFD}`. Both registry views are handled. The shared DirectShow category is never removed.

## Local test evidence

On 23 October 2026, the unsigned 0.4.2 candidate passed current-PC install, x86/x64 registered moniker binding, frame/stride/slate/COM cleanup checks, non-elevated real-libobs output, repair of a deliberately missing registration, upgrade to a separately labelled 0.4.3 fixture, rollback after an intentional 0.4.4 fixture failure, and exact-file/registration uninstall. The baseline 0.4.2 app was then installed again. No test fixture is a release.

Evidence: `artifacts/msi-lifecycle-20261023-025854/result.json` and the accompanying MSI/consumer logs. The candidate is `packaging/out/msi-candidate-20261023-025151-d33d56/Rilmo-Screen-Recorder-0.4.2-unsigned-candidate.msi`, SHA-256 `0ba5ec4f978f6c81d313e925cb3c2e02c96bf55a218140999a1a715b7b62fc6f`. The active-app refusal was separately verified. An earlier test invocation incorrectly disabled NSIS's temporary self-copy; that test launch was fixed before the successful lifecycle run. Failed attempts are preserved.

The test builder's `-TestPackageVersion` and `-TestFailAfterFiles` create explicit upgrade/rollback fixtures. The rollback error is absent from normal candidates. `tools/msi-lifecycle-test.ps1` is an explicitly invoked elevated test workflow, not an installer custom action. It preserves recordings/preferences and restores a baseline app after the test.

## Release conditions

This is an unsigned development candidate, not a public or Store-approved installer. Current-machine success is not a clean Windows 10/11 result. WACK evidence, trusted signatures, complete source/license delivery, immutable HTTPS installer hosting and the correct Partner Center product route must be completed. The reserved product currently uses MSIX/PWA; its package form cannot accept this MSI. Do not delete the reservation to free the name without a migration decision.
