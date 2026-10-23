# MSIX and virtual-camera qualification

## Selected route, 23 October 2026

The maintainer approved MSIX with virtual camera limited to Windows 11. Recording and streaming must remain available on Windows 10. The existing Microsoft Store reservation stays `MuhammadHasnain.RilmoScreenRecorder`; it must not be deleted or moved to EXE/MSI as part of this route.

The installed earlier Rilmo player is `MuhammadHasnain.Rilmo_3.4.2.0_x64__vbxf8yskf76q4`, a Store-signed full-trust AppX/MSIX application. It has no camera device registration. Its packaging confirms why that app did not require a separately purchased signing certificate. It does not establish recorder or camera compatibility.

## Platform boundaries

- The current Windows 10 DirectShow DLL is loaded inside client applications and requires global category/COM registration. The original MSIX scaffold does not own or expose those registrations.
- Microsoft documents `com4:InProcessServer` starting at build 20348, above this PC's Windows 10 build 19045. `com5` starts at Windows 11 build 22000.
- `MFCreateVirtualCamera` starts at Windows 11 build 22000. The current-user/session-lifetime API is the intended camera lifecycle, not an elevated external setup script.
- The API alone does not prove deployment: the camera service must be able to instantiate the packaged media-source DLL. Microsoft's reference sample uses an additional MSI. Packaged source activation must be established independently before claiming an MSIX camera works.
- A Frame Server source runs as Local Service in session 0. The old `Local\\` shared-memory queue is not a suitable cross-session transport. Any new transport must constrain access and carry frames only after the user starts the camera.

## Deployment probe

`native/msix-camera-probe` and `tools/msix-camera-probe.ps1` build a small, independent package identity. The initial stub returned `E_NOTIMPL`. On run 39871209444, both Windows 11 ARM64 and Server 2025 x64 installed/launched the package, created the camera object and cleaned up the package/certificate, but Start returned the stub's `E_NOTIMPL`. Neither service loading nor streaming passed in that run.

The revised probe implements an independent synthetic 640x360 NV12 Media Foundation source. A local Source Reader test received ten changing samples with increasing timestamps and valid duration/buffer length, and all COM objects released. This verifies the source implementation in-process; it does not verify service or packaged streaming. Named events require Local Service/SYSTEM and reject the probe executable itself, distinguishing camera-service loading from a client-side COM call. No screen content is captured by this probe.

Installation is guarded to disposable GitHub-hosted Windows CI. A non-exportable temporary certificate is trusted for this test package and removed in cleanup; no private key or probe binary is uploaded. `-BuildOnly` compiles and tests the synthetic source locally without installing a package, certificate or device. The probe records package install/removal, true packaged activation, API results, service DLL loading, service source activation and cleanup separately. `mediaStreamingTested` becomes true only if the virtual-camera media source delivers ten validated samples. This diagnostic is separate from service loading and from production screen-output qualification.

The investigation runs on Windows Server 2025 x64 and a native Windows 11 ARM64 hosted runner, compiling the latter probe with the image's preinstalled Microsoft C++ tools. ARM64 client activation is useful platform evidence but does not qualify the recorder's x64 capture/encoding path or third-party meeting applications. A failed or skipped probe must remain visible. No flag may turn it into a camera-streaming pass. Standard hosted runners are free for this public repository; no paid machine is provisioned.

Runs 39873569887 and 39874139085 passed the ten-frame synthetic source test on Windows 11 ARM64. Packaged Start timed out after 60 seconds. The latter run's watchdog confirms that the source initialized, and Windows queried its presentation descriptor and source attributes in the client process. No camera-service factory/activation event was observed; no virtual-camera samples passed. The test package and certificate were removed. A diagnostic collection error was retained and its optional collection has been corrected separately. These results do not establish that a package-owned camera is supported or unsupported; further service/deployment evidence is required.

Run 39875504627 controls for source/runner failure: after fixing activator lifetime, the identical DLL registered outside MSIX was loaded by Local Service, Start/GetMediaSource succeeded, and ten validated samples passed through the Windows 11 virtual device. Stop, Remove and Shutdown returned S_OK. The independent MSIX still timed out with no observed service activation. The exact control registration was removed; Frame Server retained a mapped test DLL, preventing test-file removal until runner disposal. No service was stopped to hide this cleanup limitation. Camera consent was Allow and both Frame Server services were running. This narrows the remaining failure to the tested package activation model, without proving all possible MSIX declarations incompatible.

The next probe explicitly sets the documented `com4:Class Virtualization="disabled"`, whose omission was the previous configuration. This controls package virtualization during class loading; it is a declaration owned by this test package, not a registry/security-policy change. Its outcome must be recorded separately.

## Independent application development package

`tools/build-msix-development.ps1` creates only `Rilmo.Development.ScreenRecorder`, with publisher `CN=RilmoDevelopmentOnly`. It does not bypass or change the release preflight, use the reserved Store identity, sign/install anything, or grant source-compliance approval. It strips the legacy camera DLLs/setup and overlays the current self-contained managed build when requested. The packaged UI blocks legacy registration and reports the actual interim camera limitation.

On 23 October the unsigned development MSIX passed Microsoft MakeAppx packaging validation. SHA-256: `f58fb746c06bc361cc087ba76cc9155cdb9132348a3bb5b412177fbab557a15d`; size 244,194,455 bytes. REA inventory found 1,902 package entries and no static integrity contradictions (parent Evidence `ev_e10d1702728559171b5dd93e56c8e9ac4145e549731614427ef8bd07f043bbeb`, graph manifest `agm_f0eaf612ba11671d9fea6a267104045f5ffaaa09be67793bf2f34ccb7b541eca`). The contained managed assembly matches the current publish hash. This is static package structure evidence, not installation, WACK, functional camera or Store certification evidence. The package is deliberately ineligible for the reserved Store product and must not be distributed.

## Remaining release work

Prove package-owned activation; implement the actual Media Foundation source and protected producer transport; integrate start/stop and Windows 10 explanatory UI; verify frames, color/stride/timing, client use, privacy denial, camera removal/crash cleanup and package upgrade/uninstall; qualify a Windows 11 client. Complete combined-runtime corresponding-source delivery and final MSIX/WACK checks before Store upload. The earlier desktop MSI WACK report is not an MSIX certification result.

## Primary references

- https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-com4-inprocessserver
- https://learn.microsoft.com/en-us/windows/win32/api/mfvirtualcamera/nf-mfvirtualcamera-mfcreatevirtualcamera
- https://learn.microsoft.com/en-us/windows-hardware/drivers/stream/frame-server-custom-media-source
- https://github.com/microsoft/Windows-Camera/tree/626f8b19c5f367602f2e89c6b314573d3776c9df/Samples/VirtualCamera
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/publish-first-app
