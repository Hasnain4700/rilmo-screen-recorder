# First Store submission — 1.0.0.0

Rilmo Screen Recorder was uploaded and submitted to Microsoft Store on
23 October 2026. The observed Partner Center status is **In certification**,
not approval or public availability. It is free and is configured to publish
as soon as certification passes. Store product: 9N8QCFJF0JWQ. This MSIX uses
application source 0.4.2 and omits virtual camera on all Windows versions.

Version-matched source delivery:
https://github.com/Hasnain4700/rilmo-screen-recorder/releases/tag/v0.4.2-source
The 43.1 MB source ZIP plus pinned public HTTPS/Git inputs provides the
source/patch/build mapping; external archives and checkouts are explicitly
identified rather than claimed to be inside the ZIP. See
[SOURCE-RELEASE-0.4.2.md](SOURCE-RELEASE-0.4.2.md).
Source ZIP SHA-256:
d282284c96d307e58d2d1a0406a2a7a7269eca61eb9c1516017300ef95cd185f.

Installed native/browser recording tests passed on a fresh Windows 11
Enterprise client, build 26200, ARM64 host running x64 through emulation,
without OBS installed. NoAudio was selected because the runner has no sound
endpoint. Recording, pause/resume, replay, screenshots, export, scenes,
transitions, custom controls and two browser sessions passed; framework DLL
resolution and test package/certificate cleanup were observed. The fresh
Windows Server x64 integration job also passed. Public CI evidence:
https://github.com/Hasnain4700/rilmo-screen-recorder/actions/runs/39893271028.
Existing Windows 10 build 19045 recording with computer audio and full decode
validation also passed. Clean Windows 10, native ARM64 and a physical
microphone/webcam/all-GPU matrix have not been tested.

Prior WACK production-mode candidate under an independent development
identity returned overall PASS, 23 passes, one optional blocked-executables
result, zero required failures. Its analyzer limitations remain documented in
[msix-wack-0.4.2.md](msix-wack-0.4.2.md). The final Store candidate's 519
executable/assembly payload files differ only in the later Rilmo managed DLL;
WACK is not claimed to have run on this reserved identity/later rebuild.
Partner Center separately accepted the uploaded package's validation. The
uploaded MSIX SHA-256 is:
59d4067f8c53d3727a8d92895cd74baf305bc8338bd8452bbe1eec5d7cc26a59.

The Store uses Microsoft.VCLibs.140.00.UWPDesktop instead of eight app-local
C++ runtime copies. Four legacy OBS camera files are excluded. The scoped
review binds the acquisition inventories, Store transform and source ZIP;
it does not broaden approval of the historical raw runtime cache.

The two English listings include real screenshots, GPL-compatible recipient
terms and developer attribution. Reviewer instructions state the actual test
scope and no external streaming credentials are supplied. Performance varies
with hardware; full OBS parity or universal performance superiority is not
claimed. Microsoft makes the final certification decision.
