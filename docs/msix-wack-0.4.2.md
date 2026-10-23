# Local MSIX WACK evidence — 23 October 2026

The approved independent development package completed the full applicable Centennial workflow in Windows App Certification Kit 10.0.28000.2957 on Windows 10 build 19045. The XML records `OVERALL_RESULT=PASS` and `PARTIAL_RUN=FALSE`: 23 tests PASS, one optional test FAIL, zero required-test failures. This is not a claim that every test passed or that Microsoft certified the reserved Store product.

Unsigned input SHA-256: `a55fcc4f00df791c087409e53955b2b9c5620c913b6e4df4adfd5649f6346c43`. Private signed copy: `58ad25085d0f2acb1857aed2f4b1d8f094505fd48f1738e5ad9afaa663f9e124`. XML report SHA-256: `2f3c9a70619900654a4420335fe10119ed1d9d539eacd40801ba9f5ace8947b7`. Private evidence: `artifacts/wack-msix-0.4.2-20261023-062558/{report.xml,result.json,summary.json}`.

## Optional blocked-executable finding

The optional `Blocked executables` test flags process-launch API imports in .NET, libobs/CEF and the Rilmo engine host, plus text matches such as `reg`, `cmd` and `dNX` in compressed Chromium resources/translations. A flagged API name alone does not prove that the application launches a prohibited system tool. Rilmo's browser engine starts its own executable with a private authenticated pipe; mux/encoder helpers are bundled application executables. The legacy development-only `MuxRelay.cs` is excluded from the production project.

The scan also found actual OBS virtual-camera registration scripts and camera DLLs under `runtime/obs/data/obs-plugins/win-dshow`. Earlier content checks covered the Rilmo camera paths and missed these OBS filenames. The package was therefore not completely camera-free, despite the hidden UI. `tools/configure-msix-payload.ps1` now removes the four exact hash-inventoried files from a new staging directory, records omissions in `runtime-staging.json` and rejects any surviving camera executable/script. It preserves `win-dshow.dll` for physical webcams and the original acquisition inventory as provenance. Both development and production packaging use the transformation. The earlier package/report is preserved as historical evidence; it does not qualify the corrected bytes.

Microsoft documents Desktop Bridge optional tests as informational for onboarding, and permits investigating bundled application executables separately from external process calls. The finding remains visible; no WACK test was deselected and no report result was rewritten. See https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-desktop-bridge-app-tests.

## Inspection limits and cleanup

The debug-configuration test reports PASS but includes 432 `FailedToLoad` metadata-inspection records, including the old analyzer's inability to inspect .NET 10 `System.Private.CoreLib`. These are retained in the XML. The pass does not independently prove that every managed assembly was successfully inspected. Release self-contained publish, actual application activation and native integration tests supply separate engineering evidence.

The command used `-appxpackagepath`, which performs package analysis; it is not evidence of installed recording. The helper removed the independently named test package if present and removed the exact non-exportable two-hour certificate/private key from CurrentUser My and LocalMachine Trusted People. Both stores were independently checked afterward. Trusted Root and Developer Mode were not changed. Exit code zero was not used as a substitute for reading the XML.

The development identity is `Rilmo.Development.ScreenRecorder`, not `MuhammadHasnain.RilmoScreenRecorder`. A final Store candidate still needs complete corresponding-source delivery, applicable package testing, accurate recipient terms/listing and submission review.
