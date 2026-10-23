# First Microsoft Store MSIX release: 0.4.2

The publisher selected a free MSIX release and explicitly deferred virtual camera. Physical webcam scene sources remain a separate supported feature. No full OBS parity, superior benchmark result, certification or successful Store upload is claimed.

## Scope and draft

`packaging/store-release-scope.json` records the selected scope. `-p:RilmoStoreEdition=true` removes virtual-camera actions before package identity is available; packaged execution also always excludes them. The engine guard runs before the legacy registry path and before native producer calls. Normal recording, scene editing, streaming, replay and physical camera sources are retained.

Partner Center product `9N8QCFJF0JWQ`, Submission `1152921505702207242`: Free pricing was saved and reopened to verify a retail price of zero; pricing and age ratings both display Complete. The publisher explicitly confirmed legal adulthood and authorized the IARC agreement before it was accepted. The draft's English description and Studio screenshot were revised for the no-virtual-camera scope. This is draft preparation, not a submission for certification.

## Tests performed on 23 October

- `tools/ui-smoke.ps1 -IncludeBrowser -StoreEdition`: passed integrated recording, pause/resume, independent replay, local screenshots, MP4 remux, custom hotkeys, expanded/compact layouts, scene persistence/editing/live transitions, capture exclusion and two fresh isolated browser sessions. The test asserts that neither the main card nor floating menu exposes virtual camera and that an excluded camera action cannot start capture.
- The first browser check failed a fixed 1.7-second startup deadline. The test now waits at most 15 seconds for the actual expected page pixels; it retains the last preview PNG on failure. Both sessions passed. This changes test synchronization, not the production renderer or its performance claims.
- Eight MKV/MP4 outputs from `artifacts/ui-smoke-store/results-20261023-003046-633` fully decoded without decoder errors. `decode-validation.json` records video dimensions, frame counts and durations.
- A private compatibility control replaced the eight app-local C++ runtime copies with Microsoft-signed DLLs from desktop framework 14.0.33728.0 (file version 14.40.33728.0). The same full UI/browser suite passed at `artifacts/ui-smoke-store-framework/results-20261023-004245-079`. The control does **not** prove MSIX package-graph resolution; no framework was installed or changed on this PC.
- Production Store-edition self-contained publish succeeded. WFO0003 remains an unsuppressed warning about the hybrid WPF/WinForms DPI manifest. Four real UI renders were inspected, including the Studio page without a virtual-camera card.

## C++ framework and development MSIX

The MSIX template declares `Microsoft.VCLibs.140.00.UWPDesktop`, minimum 14.0.33728.0. Microsoft recommends this dependency instead of app-local VCRedist DLLs for Desktop Bridge. `tools/configure-msix-framework.ps1` validates and removes only the staged Microsoft-signed C++ copies, updates staging receipts and records the external dependency. It does not alter the original acquisition inventory, install anything, approve source compliance, or remove .NET's distinct vcruntime140_cor3.dll.

The independent unsigned package created at `artifacts/msix-development-20261023-054028` passed MakeAppx validation. SHA-256: `a55fcc4f00df791c087409e53955b2b9c5620c913b6e4df4adfd5649f6346c43`. It has no legacy camera payload and no app-local copies of the eight framework DLLs. It remains a development-only identity, not an uploadable Store candidate, and has not been installed or WACK-qualified on this PC.

`tools/msix-app-ui-probe.ps1` tests the actual production UI under an independent package identity only on disposable GitHub-hosted Windows CI. It checks four renders, installed process identity, package removal and exact temporary-certificate cleanup. Only evidence is retained. It has no OBS payload and cannot qualify recording, final package-graph native loading, clean Windows clients or WACK. Inspect its JSON result before claiming a pass.

## Remaining before upload

Complete and publish exact combined-runtime corresponding-source delivery, native dependency/license mapping and recipient terms. Test the complete native MSIX payload under its framework dependency, clean Windows client recording/update/uninstall and final WACK. Match privacy/storage/network statements and listing screenshots to that candidate. Do not turn an older MSI WACK report, a development MSIX, or an isolated compatibility control into final Store approval.

Source review archive `release/source-review-bundle-0.4.2-20261023-053102.zip` contains 622 files, 641,383,408 bytes, SHA-256 `ba0b1358f3d66a4f7c5f18c7b890d24e53f10bfacac0db51e59ebd4da77c06e5`. It predates subsequent Store framework/CI edits and explicitly retains completeness/distribution approval false; it is not the final corresponding-source release.

## References

- https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/cpp/libraries/c-runtime-packages-desktop-bridge
- https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-com4-extension
- https://www.gnu.org/licenses/gpl-faq.en.html#MustSourceBuildToMatchExactHashOfBinary

GPL corresponding-source completeness is not a requirement for byte-identical reproducible hashes. A rebuild is useful engineering evidence; the unresolved issue is delivering the actual complete version-matched sources, modifications and required build/install instructions.
