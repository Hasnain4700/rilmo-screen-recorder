# One-click installation and Store release checklist

Each item needs evidence for the exact candidate binary. This is not completed certification paperwork.

## Build and source

- [ ] Choose the public product name and publisher; reserve the name in Partner Center. Copy identity name, publisher distinguished name, and display name exactly into the release config.
- [ ] Build and audit a reproducible OBS 32.2.2 runtime with only supported modules. Record the toolchain, source commits, patches, encoder dependencies, build flags, licenses, and hashes. Review all helper and codec distribution terms.
- [x] Implement package-relative engine loading and replace installed-OBS helper relays with genuine version-matched native helpers. The isolated native-load check records app-local DLL paths. No PATH or `RILMO_OBS_ROOT` fallback remains. Clean-machine and installed-package proof is still required below.
- [ ] Publish a self-contained release build and the applicable supported redistributable dependencies. Test native runtime prerequisites on a clean machine. The existing development publish command alone does not satisfy this item.
- [ ] Review the generated runtime inventory, including DLLs, EXEs, shader/data/locale files, and license files. The local cache records selected modules, their static/delay import closure, explicit dynamic libraries, and acquisition origins; complete the transitive component/source/license review before marking it release-approved.
- [ ] Create and independently rebuild the corresponding-source release: our app, native declarations, upstream runtime/plugin/dependency sources where required, modifications, build/installation scripts, and exact versions. Include `release-source-manifest.json` mapping shipped components to corresponding source and build instructions. Hash the archive and establish compliant source delivery to Store recipients.
- [ ] Finalize third-party notices for the actual distributed payload. About must expose the license, source release, privacy policy, support contact, and version.
- [ ] Review the measured FFmpeg GPL-3.0-or-later configuration, camera static runtime sources, and optional CEF/Chromium/Qt inputs. `prepare-release-sources.ps1` and `package-release-source.ps1` create a local review bundle; they do not certify that corresponding source is complete.

## Privacy and product readiness

- [ ] Replace all publisher/date/contact placeholders in the privacy draft; reconcile it with shipped code, network observation, storage, diagnostics, retention, uninstall, and support handling. Publish the final policy at a stable public HTTPS URL.
- [ ] Review `runFullTrust` and microphone declarations against the actual APIs. Confirm no undeclared new camera/borderless/programmatic-capture behavior was introduced.
- [ ] Record user-visible capture initiation, selected sources, active recording indicator, Stop, device/permission denial, and saved-file location. Background/tray behavior must remain understandable.
- [ ] Review log contents and retention. Current development logs can include file paths, device/display metadata, encoder/module details, and errors. Do not describe them as anonymous or automatically purged until verified.
- [ ] Observe and disclose the automatic OBS win-capture compatibility request, browser-page requests/storage, and user-selected streaming traffic. The current build must not be described as making no automatic network requests.
- [ ] Ensure all displayed features work. Do not claim full OBS feature parity, crash-proof files, all-PC support, or performance superiority unless that scope is actually implemented and evidenced.
- [ ] Review the GPL/customer-license/Store delivery interaction and any planned paid distribution. The Store listing and EULA must not contradict GPL rights.

## Clean-machine one-click proof

- [ ] Test Windows 10 build 19041 baseline and the targeted newer Windows versions, including an ordinary nonadministrator user. Confirm GPU/driver requirements and unsupported-device messaging. Set `MaxVersionTested` from actual tests.
- [ ] With OBS absent and the network disconnected after installation, launch from the installed package; record a display plus system audio and optional microphone; pause/resume; stop; play the result; reopen the output folder.
- [ ] Verify install, first launch, update preserving preferences, uninstall, and reinstall. Document which recordings and settings remain. Do not assume unpackaged and MSIX data paths are identical.
- [ ] Explicitly approve and test Rilmo Virtual Camera registration, both architecture clients, upgrade rollback, locked-client uninstall/retry, and real meeting apps. Registration-free frame tests do not establish installed-device compatibility.
- [ ] Exercise low-end integrated graphics, hardware-encoder absence/fallback, multiple monitors and DPI, device removal, privacy denial, low disk, unwritable folder, sleep/resume, and forced termination/recovery.
- [ ] Verify recording output duration, audio/video sync, frame loss, and finalization under long sessions. Observe CPU, GPU, memory, file growth, and energy where relevant, using comparable settings for any OBS benchmark.
- [ ] Store machine/driver/build/hash details and results in the release evidence documents. A “passed” checkbox with no test context is insufficient.

## Package and submission

- [ ] Run `package.ps1 -CheckOnly` against complete inputs, then create the unsigned candidate. Inspect its inventory and unpacked contents for accidental secrets, test media, private recordings, debug/development helpers, and unrelated plugins.
- [ ] Resolve full-featured installer choice: the current MSIX scaffold deliberately rejects the external camera registration. A signed offline EXE/MSI route requires its own complete install/update/uninstall validation and current Store requirements, including signing all PE files. Do not treat an unsigned installer or successful compilation as certification.
- [ ] Validate the manifest and package with Windows App Certification Kit. Perform installed-package testing through the appropriate developer test-signing flow; this scaffold neither signs nor installs.
- [ ] Supply actual screenshots, accurate feature/limitation text, support details, privacy URL, source/license information, age-rating answers, and justified capability/certification notes.
- [ ] Confirm the final package identity/version and source archive match. Recheck Microsoft's current policies and publish only after approval of the concrete candidate.
- [ ] Submit to Partner Center and resolve certification findings. Store re-signing applies after the MSIX is accepted; generating an unsigned package is not publication.

See [MSIX submission requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/create-app-submission), [package signing and validation](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/package-version-numbering?pivots=store-installer-msix), and [the project licensing plan](../docs/store-and-licensing.md).
