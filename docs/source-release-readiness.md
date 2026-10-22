# Source release preparation

The local source-review bundle is a concrete collection of project code, native camera modifications, build/install scripts, licenses and exact dependency inputs. It is deliberately marked **`correspondingSourceComplete: false`** and **`publicDistributionApproved: false`**. It has not been published and is not a substitute for the remaining release audit.

Run these commands with PowerShell 7. Python with the standard `tarfile`/`lzma` modules is used to validate XZ source archives on Windows:

```powershell
.\tools\prepare-release-sources.ps1
.\tools\fetch-browser-release-inputs.ps1
.\tools\package-release-source.ps1 -Version 0.3.0
```

For cached verification, use `-NoDownload` on the two acquisition commands. `prepare-release-sources.ps1 -Components FFmpeg,freetype` retries selected inputs while retaining the full manifest. Downloads never execute upstream build scripts. Versioned release archives are checked against upstream checksums; Git archives use exact pinned commits and record local SHA-256 values for subsequent verification. Errors and absent sources remain visible in the manifest. The bundler verifies all included source input hashes, preserves source-root paths, detects changes while reading files, and excludes generated app/native output, recordings, logs, and credentials. It creates a new `release/source-review-bundle-0.3.0-<timestamp>.zip` plus a SHA-256 sidecar, preserving earlier bundles.

The acquired inputs include the official OBS 32.2.2 complete source release (including its submodules), exact obs-deps build recipes and patches, selected core dependency source archives, the Zig camera toolchain archive with its static-runtime sources, exact QtBase 6.11.1 source, CEF source and patch series, the matching CEF distribution with wrapper source and credits, and all modified Rilmo camera source. The current success/failure list is `release/source-inputs.manifest.json`; browser inputs are in `release/browser-source-inputs/manifest.json`. An upstream CEF binary archive is identified as such, not mislabeled as complete Chromium source.

The official OBS portable binary archive now matches the tested core payload byte-for-byte. This improves provenance but does not prove the complete dependency source/build correspondence. The shipped FFmpeg DLL reports GPL-3.0-or-later; its exact compile configuration is captured in `artifacts/ffmpeg-license-evidence.json`. The full GPL3 text is under `third_party/supplemental`.

Before public binary delivery, finish these items:

- Acquire any failed core source input and all required transitive submodules/static inputs. Map exact shipped binaries and configuration to the selected source, patches and rebuild instructions; perform the rebuild.
- Complete the pinned Chromium source/DEPS and build correspondence for CEF. The full credits and CEF source/patches are present, but a credits page alone does not satisfy all component source obligations.
- Review the actual enabled Qt features, applicable third-party terms, and replacement/relink requirements for QtCore, QtGui and QtWidgets. The exact QtBase source and broader attribution inventory are present.
- Complete camera static compiler-runtime/source review and retain repeated x86/x64 activation tests when changing the toolchain.
- Establish Microsoft VC++ redistribution entitlement and required notices. Current signed System32 files are inventoried, not automatically approved for redistribution.
- Finalize GPLv3-compatible recipient terms and an actual source-delivery method alongside the exact binary release. Publish only after the final source/binary artifact review; a public upstream homepage is insufficient.
- Finish the signed installer and clean-machine matrix, camera registration lifecycle/real-client tests, privacy/network observation, publisher identity/support/privacy URLs, and Store submission review.

`packaging/package.ps1` rejects an incomplete review bundle even if its structural files and hash are present. The current MSIX scaffold also rejects the full camera-equipped build because it does not own machine device registration. The offline EXE preview installer is a separate local test candidate, with signing, machine tests and certification still pending.
