# Rilmo Recorder 0.4 validation

Local development evidence, 22 October 2026. This release adds Doppio One typography, a vertically expanding aspect-preserving preview, taller navigation/source buttons, custom shortcuts and the developer credit Muhammad Hasnain. Advanced features remain inside source dialogs and Preferences to keep the recording page simple.

## Functional evidence

`tools/ui-smoke.ps1 -IncludeBrowser` passed at `artifacts/ui-smoke/results-20261022-021800-234/result.json`. This exercises the actual WPF frontend and bundled native engine, with test-only settings and owned recording outputs. The final default Window font resource change is additionally checked by production UI rendering.

- Doppio One resolves to the embedded font, rather than a system substitute. The font and its unchanged SIL OFL notice are bundled.
- Large preview height, minimum control heights and compact layouts pass; actual recorded images retain `Uniform` aspect fit.
- Five custom shortcut actions: persistence, disabled bindings, duplicate and plain-letter rejection, invalid-save rollback and reset. An owned Ctrl+Alt+Shift+F24 native registration/conflict/release test passed without taking the user's shortcuts.
- Source dialog switches through media/slideshow/nested scene/text without reparenting errors; screenshots include About and shortcut controls.
- Actual record/pause/resume/stop, independent replay, screenshot, MKV-to-MP4 remux, live scene edits, fade and scene persistence pass.
- Two browser sessions through the real UI, helper cleanup, app-local runtime, missing-display/window guards, secret-free settings, reduced motion and floating controls pass. See 0.3 validation for the broader original game/browser/camera tests and their limits.

`tools/feature-expansion-smoke.ps1` passed at `artifacts/feature-expansion/results-20261022-071553/result.json`. Checks use actual rendered pixels for nested scenes, local media, timed slideshow, green-screen transparency and saturation correction. Swipe, Slide and Fade-to-black reach the correct scenes while recording. Recursive scenes are rejected. The graph builder validates longest nested paths independent of scene ordering and limits them to eight levels.

The expanded audio path offers three recording tracks: mixed, computer and microphone. Both native feature recordings contain three AAC streams and decode completely. This initially verifies stream creation and muxing with silent inputs; it does not itself prove microphone routing or monitoring quality. `tools/audio-routing-smoke.ps1` provides generated 440/880 Hz source substitution to test channel isolation, gain, compressor attachment and positive/negative native sync offsets without opening physical inputs. Its separate result and frequency-analysis evidence are kept in `artifacts/audio-routing`.

That synthetic audio test passed: `artifacts/audio-routing/results-20261022-072054/verified.json`. The mixed track contains both tones; computer-only and mic-only tracks suppress the other tone by over 100:1 amplitude. All three streams decode. Positive 120 ms and negative 40 ms native offsets, disabled monitoring state, compressor attachment and invalid-offset rejection also passed. This is one additional fully decoded file beyond the 16-file suite.

`tools/verify-current-recordings.py` decodes every stream in six benchmark recordings, two feature recordings and eight integrated UI files, including replay/browser/MP4. The 16-file report is `artifacts/recording-validation-0.4.json`. Every benchmark video also contains nonblank moving content. No external stream destination is used.

## Performance and distribution

The matched frontend comparison is documented in [performance-comparison-0.4.md](performance-comparison-0.4.md). Rilmo's hidden UI updates and repeated native module initialization were reduced, but **OBS still uses less measured recording working set and slightly less GPU-mode CPU** on this PC. Both maintained 30 fps with zero native drops/skips. These results do not establish superiority, game performance or behavior on other machines.

Production publishing uses `RilmoTestMode=false`, bundles .NET, core OBS libraries, optional browser runtime and independent virtual-camera components. `tools/validate-published.ps1` verifies native hashes, version and read-only UI renders with external .NET disabled. The local preview installer is unsigned, and does not automatically register a camera or change OBS settings. Build, package and local install receipts are retained in `artifacts/published-validation`, `packaging/out` and `artifacts/local-install-0.4.0` when those steps complete.

Full OBS parity, physical audio/camera qualification, demanding game/GPU matrix, long recordings, clean-machine installation/uninstallation, exact public corresponding-source delivery, signing and Store certification remain open. Partner Center identity and public support/privacy URLs are still needed for Store submission. See [feature coverage](feature-coverage.md) for precise implemented and remaining scope.
