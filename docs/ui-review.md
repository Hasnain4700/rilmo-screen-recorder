# UI and lifecycle review

## Version 0.2 verification

The black/red layout supersedes the original mint screenshots below. Current rendered evidence is in `artifacts/ui-smoke/results-20261021-224720-079`: Record/Live/Studio/Captures/Preferences at 1040×720, compact Record/Studio at 760×500, and the floating menu. Scrollable content preserves the recording transport and navigation at the minimum size. These are 96-DPI render checks, not mixed-DPI certification.

The real-engine integration test passed recording, pause/resume, native app-local loading, MP4 export, replay-only, recording with replay, independent stopping, invalid stream rejection, nonserialized keys, screenshot files/library, missing-display guards and lazy camera discovery. The floating button exposes Button/Invoke automation semantics; invoking that peer opens the menu and focuses its first action. Capture exclusion succeeds for the ordinary-HWND floating control and frozen-background region picker on Windows 10 build 19045.

Review fixes include preserving cleanup retry state, serializing cleanup before a new operation, revision-checking output snapshots, guarding screenshot shutdown/restoration, preventing recursive menu Close on deactivation, showing actual camera names and matching the pending/busy dock state. The test now fails on any unhandled dispatcher exception, even if the ordinary application error handler would otherwise display a dialog and continue.

Computer Use could read the running main window's accessibility tree, but screenshot capture failed with `FrameArrived timed out` / `window capture timed out`; indexed input then reported `coordinate input geometry is unavailable`. The failed UI session was stopped. Mouse dragging, end-to-end keyboard shortcuts, manual screenshot selection and mixed-DPI interaction are therefore **not claimed as verified**. No alternate direct Windows UI-automation API was used.

The final integrated WPF process sampled 115,310,592 bytes idle and 173,633,536 bytes during recording (about 110 and 166 MiB). These are point samples of the app process; native helper, driver and DWM costs require separate accounting. They are not an OBS comparison or an endurance benchmark. The latest test created only test-local media; no external streaming service was contacted.

## Earlier review record

Static review of the implementation snapshot on 22 October 2026. Application files were not changed and no new capture was started. Line references describe the reviewed snapshot; concurrent fixes may move them.

## Current disposition

The six actionable code findings below have since been addressed: finalizing state retains Retry Stop; unexpected engine termination ends the visible recording state; display matching uses the monitor interface identity and rechecks after countdown; startup bounds adapt to the work area; library searches run asynchronously with debounce/cancellation; and ComboBoxes have a visible focus border. One-shot preview processing also moved off the UI dispatcher, empty display inventories receive an actionable error, and native audio startup validates the selected endpoint.

These are code fixes and bounded test observations, not a claim that every failure was fault-injected. Physical monitor hotplug, every keyboard/DPI combination, blocked disks/native helper hangs and full microphone-quality tests remain explicit release validation work. The original findings remain below as the review record.

A second review found modal-operation reentrancy and stale source previews. Export, area selection and folder selection now reserve the operation before entering the modal message loop; the close confirmation also blocks recording commands while awaiting a choice. One-shot preview completion checks its source identity/revision and clears when the source changes. A missing saved display remains unselected after app restart, and changing another preference does not replace its stored identity with display zero. The instrumented UI test exercises missing-selection rejection and preview invalidation; physical unplug/reorder remains a separate hardware test.

The final self-contained .NET 10 preview rendered successfully at 1140×780, 911×512 and the minimum 820×500 logical window sizes. The source labels reserve space for adjacent controls, content scrolls, and the Start/Stop bar stays visible. Images are `artifacts/rilmo-final.png`, `rilmo-compact.png` and `rilmo-minimum.png`. This layout check uses 96-DPI rendering and does not certify mixed-DPI monitor behavior. The public preview assembly has no `RunAutomatedSmoke` method; unattended recording is restricted to the explicit test build.

## Actionable findings

1. **P1 — A stop timeout removes the only safe retry path.** `src/MainWindow.xaml.cs:257–264` unconditionally clears `recording`, restores Start, and turns the tray icon idle after any stop failure. `src/Engine/RecorderEngine.cs:404` can explicitly throw while the output is still active and asks the user to retry Stop. After that exception the UI cannot retry Stop. Closing then enters `MainWindow.xaml.cs:139–141`, catches a failing `Dispose`, and closes anyway, potentially terminating an output that is still finalizing. Keep a stopping/retry state when the engine remains active; only mark idle after confirmed shutdown. Do not close on failed engine disposal while capture/output remains active.

2. **P1 — An unexpectedly stopped output still appears to be recording.** `src/Engine/RecorderEngine.cs:368–373` sets its recording flag false and throws when the native output stops. `src/MainWindow.xaml.cs:291–296` only changes the footer to “statistics unavailable.” The UI recording flag, timer, red state, and tray indicator continue, so a user can keep talking while no video is being saved. Distinguish fatal output termination from a metrics failure, stop the visible elapsed time, surface the interruption, and safely finalize/release before allowing another recording.

3. **P1 — Display changes can silently switch the captured monitor.** `src/MainWindow.xaml.cs:198–204` refreshes `Screen.AllScreens` but preserves the previous numeric index and picker contents. If the selected display at index 0 disappears, another connected display can take index 0 and pass the bounds check. The UI then starts recording the replacement screen under a stale selection. Persist and match a display identity; rebuild the picker and require explicit selection when the prior display is gone. Recheck identity after the countdown as well.

4. **P2 — Minimum window dimensions exclude common scaled laptop screens.** `src/MainWindow.xaml:1` fixes a 980×700 minimum and starts at 1140×780. A 1366×768 laptop at 125% scaling has roughly 1093×614 logical pixels before taskbar subtraction, so the minimum height exceeds the work area and the bottom recording controls can fall offscreen. Clamp startup bounds to the current work area and allow a smaller responsive layout; test at 1366×768 with 100%, 125%, and 150% scaling.

5. **P2 — Library filtering can freeze the dispatcher during a recording.** `src/MainWindow.xaml.cs:336–355` synchronously enumerates and sorts the whole output directory on every search keystroke and builds up to 100 WPF cards. Sorting occurs before `Take(100)`, so the limit does not bound filesystem work. A large or slow/network folder blocks navigation, the stop button, and dispatched global shortcuts even though recording continues on the worker. Debounce search; gather metadata on a background task with cancellation, then update the UI from the resulting bounded list.

6. **P2 — ComboBox keyboard focus styling has no visual effect.** `src/App.xaml:20` changes the outer ComboBox `Background` on keyboard focus, but the visible inner Border hardcodes its background to `#121716`. The intended focus indicator never renders. Bind the visible surface to the template background or add a visible focus-border trigger to the actual rendered element. Verify the display, quality, and encoder pickers using Tab/Shift+Tab and keyboard selection.

## Follow-up validation gaps

- Requested audio is represented by toggles, but `RecorderEngine.CreateAudioSource` only checks whether a source object was created (`RecorderEngine.cs:253–264`). That does not establish that a usable default audio device is producing samples. Exercise disabled/unplugged/default-missing input and output devices; ensure the UI cannot silently promise microphone capture when initialization failed. This needs runtime evidence before labeling it a confirmed failure.
- `MainWindow.xaml.cs:303–310` performs full-screen GDI capture and PNG compression on the UI thread. Measure the “Preview once” action on a 4K display and the low-end target; move expensive processing away from the dispatcher if it stalls interaction.
- Empty display inventory is not handled by `BuildOptions` (`MainWindow.xaml.cs:179`) or Preview (`:303`). Confirm the supported headless/remote-session behavior and show an actionable unavailable state rather than calling `Math.Clamp` with a maximum of -1.

## Suggested verification

Use a deterministic fake engine for stop timeout and unexpected termination so these states can be exercised without risking a real recording. Test monitor selection across remove/reorder and countdown changes. Run keyboard-only capture setup at laptop DPI settings. Use a temporary large directory or controlled slow filesystem to check that Stop stays responsive while searching recordings.

## 0.3 review

The new scene editor, browser-source dialog and compact scene editor were rendered and inspected. Review corrected black-on-black editor heading text, double-applied text-field padding (which clipped narrow duration fields), and missing large WPF text in scaled layout renders. The layout now uses static vector text drawings; the actual native snapshot remains a distinct user action. Scene map, source fields, main Studio and floating menu renders are retained with the integrated UI results. These are rendered/in-app checks; the earlier manual pointer/DPI and screen-reader qualification limits remain.
