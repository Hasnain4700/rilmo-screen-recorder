# Microsoft Store and licensing release requirements

Research checked 22 October 2026 (Asia/Karachi). This is an engineering release plan, not a legal opinion or a claim of Store certification. Recheck the applicable agreement, policies, dependency inventory, and package immediately before submission.

## Decision

The owner has approved an **open-source product**. Use a custom WPF interface and a deliberately small libobs runtime, initially matching installed OBS Studio **32.2.2**. License the application under **GPL-2.0-or-later**. Do not represent the new product as an official OBS release.

This is the practical route to OBS-compatible recording, scene composition, audio, and encoder capabilities. The new interface can simplify setup, but real performance gains must come from measured loading, capture, encoding, and rendering choices. Reusing libobs does not itself prove the new application is faster than OBS.

OBS's version-pinned [libobs header](https://github.com/obsproject/obs-studio/blob/32.2.2/libobs/obs.h) grants GPL version 2 or later. An OBS project developer also confirms that libobs and obs-frontend-api have no linking exception in the [OBS license clarification](https://obsproject.com/forum/threads/obs-license-inquiry.58557/). Treat a custom frontend linked through P/Invoke or a native bridge as a GPL-compatible combined application. Loading a DLL dynamically is not a proprietary-license escape.

The independently implemented alternative would use Windows capture APIs and Media Foundation, possibly with the MIT-licensed [ScreenRecorderLib](https://github.com/sskodje/ScreenRecorderLib). Its [MIT license](https://github.com/sskodje/ScreenRecorderLib/blob/master/LICENSE) requires retention of its notice. This alternative was evaluated, but is not the selected engine. It would require independent implementation and validation of the advanced features supplied by OBS.

## Source and dependency release gate

Under [OBS 32.2.2's GPL text](https://github.com/obsproject/obs-studio/blob/32.2.2/COPYING), redistribution requires preservation of notices, the license, and complete corresponding source through an applicable section 3 method. Modified files need change notices and dates. Source includes compilation/installation scripts and interface definitions, not only application code. Recipients must retain GPL freedoms. Charging for distribution is possible. An upstream homepage alone is not a complete source-delivery plan for our particular build.

The actual shipped FFmpeg library reports **GPL version 3 or later**, with GPL and version3 enabled in its build configuration. The combined release must therefore use the later-version election allowed by the application's and OBS's GPL-2.0-or-later terms and satisfy the applicable [GPL version 3](../third_party/supplemental/GPL-3.0.txt) source-conveyance provisions (section 6 for object code). Do not apply GPLv2 section 3 offer wording to this combined payload without reviewing GPLv3's corresponding requirements. The direct binary query is saved in `artifacts/ffmpeg-license-evidence.json`.

For this project, implement these release deliverables:

- Archive every shipped dependency's exact version, source revision, patches, build options, applicable licenses, and SHA-256 hashes in a release manifest.
- Publish the application's source tag and reproducible build instructions, including the native interop layer, packaging scripts, and corresponding libobs/plugin/dependency source required for that binary.
- Deliver the applicable GPL texts and third-party notices in the package and expose them, plus the matching source-download location, through About. Establish a source-delivery method satisfying the selected GPL version for Store recipients; merely saying “source available” is insufficient.
- Review the Store customer-license terms and any DRM or purchase restrictions against the chosen GPL version. Do not impose an EULA that takes away rights granted by the GPL. The final agreement and delivery mechanism are a release dependency.
- Audit each DLL, helper EXE, shader, font, icon, locale file, and optional plugin. “OBS is GPL” is not a substitute for inspecting its dependency licenses. Preserve upstream copyright and attribution files.

FFmpeg's [upstream license inventory](https://github.com/FFmpeg/FFmpeg/blob/master/LICENSE.md) explains that its configuration and external libraries change the applicable license. GPL-enabled builds, LGPL builds, and nonredistributable `--enable-nonfree` builds are materially different. In particular, do not assume any convenient FFmpeg binary may be redistributed. Codec patent and hardware SDK terms require their own market/component review; the GPL is not a patent-clearance certificate.

The standalone local preview now bundles its selected native runtime and has no installed-OBS lookup or helper relay. Runtime preparation can use an explicit, hash-inventoried local fallback; the resulting binary records that origin. A public release still needs an audited redistributable runtime, complete component notices and corresponding-source delivery, and clean-machine/installed-package validation. Local file acquisition is not a completed release process. Avoid loading user-installed third-party plugins into the default application process. See [the standalone runtime plan](standalone-runtime.md).

## Store policy baseline

The current [Microsoft Store Policies](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies) page reports version **7.20**, effective **22 October 2026**. Relevant requirements include accurate features and dependencies (10.1/10.2.4), security and clean uninstall (10.2), testability (10.3), responsive behavior (10.4), privacy (10.5), and justified capabilities (10.6).

Win32 products need a privacy policy, including local-only tools. Explain data access, storage, sharing, security, and controls. External publication of personal information requires the relevant consent. Do not bypass OS privacy controls. Dependencies on non-Microsoft drivers or NT services generally need special consideration; disclose them in certification notes. Online sharing/community features add obligations beyond local recording. Use original branding and accurate screenshots. Only advertise features and performance that shipped builds demonstrate. A minimal recorder must not be listed as having every OBS feature.

These are implementation gates, not a claim that Microsoft's approval is automatic.

## Packaging path

**Re-evaluate the package route for the full Windows 10 virtual-camera feature set.** MSIX remains suitable for the recorder core and provides Store-managed distribution, but the current MSIX scaffold does not expose Rilmo's external DirectShow device or own its machine-registration/uninstall lifecycle. Do not label that scaffold a complete one-click virtual-camera installation. Verify native DLL discovery, helper processes, plugins, writable folders, and runtime dependencies under the chosen installation model. [Distribution choices](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-distribution-path)

MSIX Store submissions do not need a purchased CA-trusted signing certificate; the Store re-signs accepted packages. Separate sideload distribution has its own signing requirements. Package identity must match Partner Center. Run the Windows App Certification Kit and hardware checks before upload. [MSIX package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/package-version-numbering?pivots=store-installer-msix)

If MSIX proves incompatible with a necessary feature, the **EXE/MSI Store route** exists. It requires an immutable versioned HTTPS installer URL, a standalone offline installer, silent installation, and trusted signing of the installer and included PE files. The publisher operates hosting and updates. Do not use a download stub. [EXE/MSI package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-package-requirements)

## Capture, camera, microphone, and permissions

Manifest declarations depend on the actual API and trust model. A full-trust packaged WPF app does not use the same permission model as an AppContainer app. Declare `runFullTrust` when required and justify it in submission. Microsoft recommends declaring privacy-sensitive capabilities for medium-integrity apps too. Relevant names include `microphone`, `webcam`, and, when used by the applicable APIs, `graphicsCapture`, `graphicsCaptureProgrammatic`, and `graphicsCaptureWithoutBorder`. The latter two are not interchangeable with ordinary capture. Declare only capabilities actually required. [Capability declarations](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations)

For Windows Graphics Capture, the documented flow uses the system picker, a visible capture border, and explicit selection. Check `GraphicsCaptureSession.IsSupported`; handle cancellation and unsupported devices. A libobs backend may instead use a Win32/DXGI capture path, so inspect the selected module rather than adding manifest declarations by guesswork. [Windows screen capture](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)

Product behavior we should enforce and test:

- First run explains which display/window and audio sources will be recorded. No automatic recording at installation or sign-in.
- The Record action, countdown, live elapsed time, persistent recording indicator, and reliable Stop action make capture understandable. Restoring a saved preset must not silently start recording.
- Microphone and webcam are separately visible choices; show denial or device-disconnect errors without losing an already valid recording unnecessarily.
- Replay buffering, if implemented, is clearly marked as active capture even before a clip is saved. Put a visible memory/disk budget on it.
- Keep recording content local by default. Upload, streaming, cloud backups, crash attachments, and diagnostics need separately specified data flows and UI. Logs should not accidentally contain stream keys, screen contents, account tokens, or sensitive window titles.
- Use a user-chosen recording destination; make its location obvious. Explain whether uninstall retains recordings. Do not promise encrypted local video unless implemented and verified.

The privacy policy must describe the actual shipped behavior. Those product choices are an implementation proposal; they do not substitute for jurisdiction-specific consent requirements when recording other people.

## Advanced OBS features need individual review

| Feature | Release treatment |
| --- | --- |
| Game capture | Package the exact required hook/helper files, audit their licenses and signatures, test standard-user behavior and protected-game rejection. Do not promise anti-cheat bypass. |
| Browser sources | Include their browser runtime only when supported; establish its security-update and license-notice process. Lazy loading saves idle resources but still needs a functional packaged feature. |
| Third-party plugins/scripts | Define supported versions, provenance, load paths, failure isolation, and install/update behavior before exposing a marketplace or arbitrary-code extension path. |
| Streaming | Keep stream keys out of logs/source/settings exports; define secure storage and consent. Review each service's integration terms. |
| Virtual camera | Treat as a separate packaging/compatibility milestone, not a default mandatory driver install. |

For a possible Windows-native virtual-camera implementation, [MFCreateVirtualCamera](https://learn.microsoft.com/en-us/windows/win32/api/mfvirtualcamera/nf-mfvirtualcamera-mfcreatevirtualcamera) supports Windows build 22000 and later. It offers current-user scope; all-user scope needs administrator rights. Packaged apps need `webcam`, must respect its privacy control, and should not invoke creation on the UI thread. This is an alternative implementation candidate, not evidence that OBS's existing virtual camera automatically works under MSIX.

## Submission evidence to prepare

1. Reserve the chosen name under the verified Partner Center publisher identity. Set architecture, supported Windows builds, category, age rating, and minimum hardware to tested values.
2. Supply the actual package, screenshots, description, support contact, public privacy policy, licenses, and source delivery. Explain full-trust/native recording in certification notes; supply test credentials only if a feature needs an account. [MSIX submission checklist](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/create-app-submission)
3. Keep reports for install/update/uninstall, standard-user launch, offline recording, cancellation, low disk, microphone denial, disconnect, GPU reset, sleep/resume, multi-monitor DPI, and final-file recovery. This project's quality target is a usable recording, not merely a successful process exit.
4. Publish benchmark methodology with any comparative claim: same source, codec, visual quality, resolution, frame rate, machine, driver, and capture duration. Record CPU, GPU, working set, frame loss, sync drift, and finalization behavior. Until those results exist, “most optimized ever” is an aspiration, not release metadata.
5. Revalidate the final binary inventory and source archive, and recheck policy changes. Microsoft certification and source-compliance review remain pending until the actual release artifacts exist.

## Present status

This document establishes release requirements. A bundled local runtime now exists, with provenance and hashes; it does not establish a Store account, public privacy/source site, complete corresponding-source archive, signed production package, capability approval, performance qualification, or certification. See the repository's `THIRD-PARTY-NOTICES.md` for unfinished distribution obligations.
