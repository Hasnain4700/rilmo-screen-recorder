# Rilmo Virtual Camera

Rilmo includes its own video-only DirectShow camera for Windows 10 and Windows 11 desktop consumers. It sends the current composed scene without encoding it. It uses the independent name **Rilmo Virtual Camera**, CLSID `{9E7FF7BC-AA7A-4B1D-8A9E-DDDFB0692DFD}`, and a separate shared-memory channel. It does not use or replace an OBS camera registration.

Choose **Set up camera…** in the app, review the described change, then continue to the Windows administrator prompt. Setup installs x64 and x86 components under `%ProgramFiles%\Rilmo\Virtual Camera` and adds this device to DirectShow. It does not change the default camera or start capture. Restart camera clients after setup. Start the virtual camera explicitly in Rilmo, then select **Rilmo Virtual Camera** in the receiving app. Microphone selection remains separate. Stop sends an inactive slate instead of retaining the last captured picture. Recording, streaming, and virtual camera are independent outputs.

The equivalent reviewed setup commands, from the published app folder, are:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\setup-virtual-camera.ps1 -Action Review
# Run the following only after reviewing the changes, in an Administrator shell:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\setup-virtual-camera.ps1 -Action Install -AcknowledgeSystemDeviceChange
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\setup-virtual-camera.ps1 -Action Uninstall -AcknowledgeSystemDeviceChange
```

Uninstall unregisters only the Rilmo identity. Close camera clients first. If a client holds a DLL open, the script preserves that file and its cleanup receipt; run Uninstall again after closing the client. It validates existing receipts before changing registration and removes only known files and their empty owned folders. It installs no kernel driver or Windows service.

Registration-free tests on Windows 10 build 19045 instantiated both architecture DLLs through their COM class factories, negotiated NV12, checked padded rows, live frames, stopped slate, duplicate writers and cleanup. An engine test sent real libobs scene output to x64 and x86 consumers, each receiving 45 live frames without a slate. Separate Windows PowerShell 5.1 tests cover malformed/escaping receipts and locked-file cleanup. These tests did not perform persistent device registration and do not establish Teams, Zoom, browser, UWP or Windows Camera compatibility. Install/upgrade/uninstall and real-client tests remain release gates.

Build details and the narrow pinned-toolchain constructor workaround are in [native/virtual-camera/README.md](../native/virtual-camera/README.md). Exact sources, modifications, hashes, licenses and imports are in that directory. `tools/build-virtual-camera.ps1 -Architecture both` rebuilds both filters; `tools/virtual-camera-smoke.ps1` verifies real frames without registering a device. The native producer activates the OBS main canvas directly, waits for the first frame, and copies NV12 in native code; there is no managed callback for each frame.

## MSI candidate, 23 October 2026

The MSI candidate owns both architecture DLLs and their class/category keys. It installs them in the same transaction as the app. The portable setup commands above are for portable/NSIS previews; that script refuses to alter an MSI-owned installation. Missing registration is repaired through Windows Installer using the ProductCode in the installed metadata.

The current-PC MSI test passed install, actual DirectShow device enumeration/moniker binding, NV12 frame/slate/cleanup checks, repair, major upgrade, deliberately failed-upgrade rollback and uninstall cleanup. A separate non-elevated process used the public engine camera methods and delivered real libobs frames to both registered clients. Raw evidence is in `artifacts/msi-lifecycle-20261023-025854`. Clean Windows 10/11 and third-party meeting/browser clients remain unqualified. See [MSI candidate](../packaging/msi-candidate.md).

Microsoft's [Media Foundation virtual-camera API](https://learn.microsoft.com/en-us/windows/win32/api/mfvirtualcamera/nf-mfvirtualcamera-mfcreatevirtualcamera) requires Windows build 22000 or later. The DirectShow implementation is the current Windows 10 route. The existing MSIX scaffold does not own this machine COM registration; a full release installer must handle it and pass install, upgrade, rollback, and uninstall tests. Local camera binaries are unsigned review artifacts, not Store-certified components.
