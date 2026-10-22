# Code signing policy

Rilmo Screen Recorder is maintained by [Muhammad Hasnain / Hasnain4700](https://github.com/Hasnain4700). On 23 October 2026 the maintainer selected the Microsoft Store **MSIX** route, with recording/streaming on Windows 10 and a Windows 11 virtual-camera target. Microsoft signs the Store package after certification; a separately purchased Authenticode certificate is not a requirement for that submission route. Camera packaging and final release qualification are still in development.

An earlier SignPath Foundation application was attempted for the alternative EXE/MSI route, but receipt and approval were not confirmed. **No SignPath production signing is active. Local development installers and Rilmo binaries remain unsigned.**

Maintainer and conditional EXE/MSI signing roles:

- Author, reviewer and release approver: Muhammad Hasnain (Hasnain4700).
- The human maintainer reviews source and build-script changes before approving a production signing request. Automated development assistance is not an independent human review.
- Production signing requires the maintainer's MFA on GitHub and SignPath and a deliberate approval of each release. The maintainer confirmed GitHub 2FA is enabled on 23 October 2026; SignPath account/MFA setup remains pending.

If the Foundation approves and signing is enabled, the required provider attribution will be: **Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org/)**. That statement is conditional, not a claim that current files are signed.

The repository's pinned GitHub Actions workflow builds the managed app, camera components and installer guard from checked-in source, validates native metadata, and exercises registration-free DirectShow consumers. It uploads build evidence only. It does not publish a complete installer, provide clean Windows 10/11 qualification, or enable production signing. Runtime components retain their upstream signatures and attribution.

The MSIX camera deployment probe may create a short-lived, non-exportable test certificate on a disposable GitHub-hosted Windows runner, trust it for that test package, and remove both package and certificate afterward. That certificate is not a trusted production signature, is not uploaded, and is not installed on the maintainer's PC. This independent probe has no recording frames or OBS runtime and cannot establish working camera streaming or Store approval.

Before a signing request can become a release, the combined runtime's source/license obligations must be complete, the full installer must pass its release tests, and modified upstream camera code must meet the Foundation's visible-fork and review conditions. Signing configuration must restrict product names, versions, sources and artifacts to this project. No private key or signing token belongs in source control.

[Privacy policy](https://rilmo-screen-recorder.wigglymaple8.chatgpt.site/privacy) · [Support](https://rilmo-screen-recorder.wigglymaple8.chatgpt.site/support) · [License](LICENSE)

Recordings stay in the user's selected local folder. User-selected streaming destinations and browser layers use the network; the upstream capture module can retrieve compatibility data. These behaviors and support-contact handling are described in the privacy policy. The project does not claim that all features are offline.
