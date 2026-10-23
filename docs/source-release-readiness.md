# Source release preparation

The local source-review bundle is a concrete collection of project code, native camera modifications, build/install scripts, licenses and exact dependency inputs. It is deliberately marked **`correspondingSourceComplete: false`** and **`publicDistributionApproved: false`**. It has not been published and is not a substitute for the remaining release audit.

Run these commands with PowerShell 7. Python with the standard `tarfile`/`lzma` modules is used to validate XZ source archives on Windows:

```powershell
.\tools\prepare-release-sources.ps1
.\tools\fetch-browser-release-inputs.ps1
.\tools\package-release-source.ps1 -Version 0.4.2
```

For cached verification, use `-NoDownload` on the two acquisition commands. `prepare-release-sources.ps1 -Components FFmpeg,freetype` retries selected inputs while retaining the full manifest. Downloads never execute upstream build scripts. Versioned release archives are checked against upstream checksums; Git archives use exact pinned commits and record local SHA-256 values for subsequent verification. Errors and absent sources remain visible in the manifest. The bundler verifies all included source input hashes, preserves source-root paths, detects changes while reading files, and excludes generated app/native output, recordings, logs, and credentials. It creates a new `release/source-review-bundle-0.4.2-<timestamp>.zip` plus a SHA-256 sidecar, preserving earlier bundles.

The acquired inputs include the official OBS 32.2.2 complete source release (including its submodules), exact obs-deps build recipes and patches, selected core dependency source archives, the Zig camera toolchain archive with its static-runtime sources, exact QtBase 6.11.1 source, CEF source and patch series, the matching CEF distribution with wrapper source and credits, and all modified Rilmo camera source. The current success/failure list is `release/source-inputs.manifest.json`; browser inputs are in `release/browser-source-inputs/manifest.json`. These manifests and `release/source-inputs.lock.json` are now included with the public application source so acquisition commands have their required inputs. Recorded local acquisition status is evidence from this machine, not a claim that archives are already present in a fresh checkout. Browser source acquisition can run without a prepared binary runtime; it records a null comparison until such a runtime exists and still rejects any present mismatch. An upstream CEF binary archive is identified as such, not mislabeled as complete Chromium source.

The official OBS portable binary archive now matches the tested core payload byte-for-byte. This improves provenance but does not prove the complete dependency source/build correspondence. The shipped FFmpeg DLL reports GPL-3.0-or-later; its exact compile configuration is captured in `artifacts/ffmpeg-license-evidence.json`. The full GPL3 text is under `third_party/supplemental`.

On 23 October the offline source acquisition recheck verified all 34 core inputs and 137 browser source/notice inputs. A separate fresh source-acquisition directory, containing the pinned download cache but no `vendor` runtime, also completed the browser command with 137 inputs and a null runtime comparison. No completeness/distribution approval flag changed. Test evidence: `artifacts/browser-source-without-runtime-result.json` and the source acquisition revalidation logs.

The exact Chromium root source archive was subsequently acquired: commit `c51f2916e4a14157c276fd33c8bfb24155aac1ad`, 1,401,959,446 bytes, SHA-256 `916b55c8af15b48f4efe6a54dc325691f1393227b2ef3f9c433ecaa7ca5eec40`. Its archived DEPS bytes match the previously pinned SHA-256 `bd35b66a22eb81109589ee5093b7c492fd7ed32485574446a107e3f274affc55`. Acquire it explicitly with `tools/fetch-browser-release-inputs.ps1 -IncludeChromiumRoot`; a verified cached copy is also inventoried automatically. The root Git archive does not include external Git/CIPD dependencies from DEPS; their completeness and CEF build configuration remain separate work, not silently approved by this download.

`tools/index-chromium-deps.py` reads the pinned DEPS through a restricted AST interpreter, without executing Python from that file. The derived `Chromium-DEPS-index.json` lists 247 Git inputs, 275 other inputs and nine recursive dependency roots, preserving conditions without claiming they were evaluated for Windows. It is an audit index, not proof that every source was acquired. Regenerate it with `python tools/index-chromium-deps.py --deps release/browser-source-inputs/Chromium-DEPS --output release/browser-source-inputs/Chromium-DEPS-index.json`. The collector inventories the derived index when present.

Before public binary delivery, finish these items:

- All 34 selected core inputs are verified. Finish the transitive/static-input mapping and exact source, patch and build-instruction coverage for the shipped binary set. A rebuild is useful engineering evidence; GPL completeness does not require reproducing a byte-identical binary hash.
- Complete the pinned Chromium source/DEPS and build correspondence for CEF. The full credits and CEF source/patches are present, but a credits page alone does not satisfy all component source obligations.
- Review the actual enabled Qt features, applicable third-party terms, and replacement/relink requirements for QtCore, QtGui and QtWidgets. The exact QtBase source and broader attribution inventory are present.
- Virtual camera is excluded from the first Store MSIX by the publisher. Its static-runtime review and registered-client tests apply only to future camera/legacy standalone distributions; do not block the camera-free MSIX on them.
- The Store MSIX now omits app-local Microsoft C++ runtime copies and declares the Microsoft-maintained desktop framework instead. Finish native package-graph qualification. Original System32 acquisition provenance is retained for historical standalone builds, whose redistribution is a separate scope.
- Finalize GPLv3-compatible recipient terms and an actual source-delivery method alongside the exact binary release. Publish only after the final source/binary artifact review; a public upstream homepage is insufficient.
- Finish the MSIX/clean-machine matrix and privacy/network observation. Publisher identity and public support/privacy/source pages exist. Microsoft Store supplies production MSIX signing; no EXE/MSI signing certificate is required for the selected first-release route.

`packaging/package.ps1` rejects an incomplete review bundle even if its structural files and hash are present. The publisher selected a free MSIX without virtual camera in its first release. A separate development identity is used for validation; historical EXE/MSI tests do not qualify the selected MSIX. See `store-msix-0.4.2.md` for current evidence, and `msix-camera-qualification.md` for the deferred camera investigation.

`runtime-build-inputs-0.4.2.md` records inspected upstream build entry points, the actual Windows preset, selected codec/Qt options and remaining CEF correspondence work. The installed native integration tests and WACK findings are functional evidence and do not change corresponding-source approval by themselves.
