# Rilmo Recorder: black and red design direction

Updated 22 October 2026. This supersedes the charcoal/mint and Rilmo-player visual references in earlier planning notes. The product remains Rilmo Recorder; its interface is now organized around recording tasks and uses an original set of native XAML shapes and control templates.

## Reference research

The following are official product or support pages, reviewed as interaction references. No screenshots, logos, icons, or other product assets were imported into the app. Their marketing performance claims are not evidence of this recorder's performance.

| Reference | Observed pattern | Application here |
| --- | --- | --- |
| [Bandicam recording window](https://www.bandicam.com/support/configuration/rectangle_window/) | Full-screen and rectangular capture have a compact control bar, sound indicators, recording and screenshot actions. Its documentation also covers moving/hiding controls and keeping recording status available in the tray. | A separate compact toolbar keeps the essential controls beside the user's work. The full window offers display, window, and area modes. The recording timer and stop action stay outside scrolling content. |
| [Mirillis Action overview](https://mirillis.com/en/products/action.html) and [official interface gallery](https://mirillis.com/action-screenshots) | Its compact desktop recorder combines capture modes, recordings management, export, and settings in one product. | Clear task navigation and a separate Recordings view provide a home for completed files without turning the capture page into a media library. |
| [Camtasia recorder setup](https://support.techsmith.com/hc/en-us/articles/39968968889101-How-to-Record-Audio-and-Video-with-Camtasia-Editor) | Screen, camera, microphone, and system audio are explicit choices. Device selection is separate from enabling a source, and microphone gain is adjustable. | The Record page exposes computer audio and microphone switches. Preferences selects devices. Studio handles camera composition, gains, and filters. |
| [Screen Studio](https://screen.studio/) | Its product presentation emphasizes the finished recording, framing, motion, and sound with restrained product language. | Use short task-oriented copy and a clean visual hierarchy. This is an inference for our own interface; it does not imply that automatic zooms, motion editing, or Screen Studio's other features exist here. |
| [Tella features](https://www.tella.com/features) | Recording, editing, sharing, layouts, and overlays are presented as distinct capabilities. | Reveal layout and overlay controls in Studio after the basic capture choices. Advanced editing and cloud sharing are outside this UI pass. |

The resulting design is original: top task navigation, a source console, a compact setup column, and a fixed bottom transport. The references inform workflow choices rather than establish a layout to duplicate.

## Main window

The default window is 1040 × 720 device-independent pixels, with a 760 × 500 minimum. Each content view scrolls vertically while the title, navigation, recording controls, and status remain in place. This provides a compact starting point and accommodates smaller screens without placing Stop inside a scroll region.

- **Record:** source mode, display/window/area choice, an explicit one-time preview, quality preset, computer audio and microphone switches. A screenshot action opens area selection. The idle preview is deliberately empty and labeled; it never displays sample footage as if it were the user's desktop.
- **Live:** server URL, obscured stream key, and video bitrate. Go live is an explicit action. Opening the page is not permission to transmit.
- **Studio:** live gain sliders and real audio peak meters, then expandable camera/layout, voice cleanup, title/logo, and replay settings. Replay explains that starting the buffer begins capture even without a recording file.
- **Recordings:** local recording search and existing open/reveal/export actions.
- **Preferences:** output folder, pointer/countdown/minimize behavior, compact toolbar preference, audio devices, and encoder choice.

Keep action text literal. A red record button means start or stop recording; the UI must not imply that a live stream, replay buffer, or screenshot has started until the engine confirms it. Status and elapsed time come from the session state. No invented CPU, FPS, dropped-frame, or latency statistics belong in the design.

## Visual system

| Resource | Value | Use |
| --- | --- | --- |
| BackgroundBrush | `#09090B` | Window background |
| PanelBrush | `#121215` | Content panels |
| RaisedBrush | `#1C1C20` | Raised surfaces |
| LineBrush | `#2C2C31` | Quiet boundaries |
| TextBrush | `#F5F5F7` | Main labels |
| MutedBrush | `#A8A8B2` | Supporting text |
| AccentBrush | `#D62C3C` | Primary actions and enabled toggles |
| AccentTextBrush | `#FF7A87` | Selected labels and audio indicators |
| FocusBrush | `#FF99A3` | Keyboard focus |

White text on the primary red has a calculated contrast ratio of 4.90:1. Accent text on the panel background is 7.47:1; muted text is 7.93:1. These measurements cover these color pairs, not a claim of complete accessibility certification. Selected controls also use text/state changes; color alone does not carry the state.

Use Segoe UI with 12–13 px body/control text, 26 px page titles, and compact 10–11 px supporting labels. Corners are 5–8 px. Surfaces are solid; no blurred glass, looping background animation, drop-shadow stacks, or live decorative preview is required. The transport timer uses Consolas. Icons are simple XAML paths and shapes with labels or automation names.

Control templates include visible keyboard focus for buttons, selectors, toggles, text fields, sliders, and expander headers. The red meter is a native ProgressBar with no indeterminate animation. When capture is inactive its value should reset to zero; selecting Studio must not turn on audio capture for a decorative meter.

## Compact toolbar

The separate toolbar implementation shares the black/red resources and keeps record/stop, pause, timer, screenshot, and the main-window/menu affordance compact. Its appearance must reflect actual session state. Additional controls can be reached through a menu instead of permanently expanding the bar. It should remain on a connected monitor, preserve its location, and let the user hide or reopen it. Record/stop and keyboard shortcuts must remain reachable when the main window minimizes.

The main title bar provides **Compact bar** to reopen it. Preferences exposes **Show the compact recording toolbar**. The Bandicam reference supports the compact-control pattern, while this app uses its own arrangement and assets.

## Integration contract

`MainWindow.xaml` retains the existing recording, library, preferences, source-selection, and transport control names. `App.xaml` retains the shared resource keys used by code-created controls. New controls and handlers are connected by the main-window partial class; the XAML design alone is not proof of engine support.

| Area | Names and behavior |
| --- | --- |
| Navigation | `NavLive` and `NavStudio` use `Navigate_Click` with `live` and `studio` tags; corresponding `LivePage` and `StudioPage` start collapsed. |
| Live | `LiveControls` contains `LiveServerBox`, `StreamKeyBox` (PasswordBox), and `StreamBitrateBox`. `LiveButton` / `Live_Click` is outside `LiveControls`, so disabling configuration cannot disable Stop live. |
| Audio | `SystemVolumeSlider`, `MicrophoneVolumeSlider`: 0–100, initial 100, `AudioLevel_Changed`. `SystemMeter`, `MicrophoneMeter`: 0–1, initial 0. These controls sit outside `StudioControls` so gain changes remain available during capture. |
| Camera | `CameraToggle`, `CameraPicker`, `ScenePresetPicker`, and `CameraPositionPicker` use `Options_Changed`. Camera selection displays `Name`. |
| Layout indices | `ScenePresetPicker`: screen with overlays, side by side, camera focus. `CameraPositionPicker`: bottom right, bottom left, top right, top left. `OverlayPositionPicker`: bottom left, bottom right, top left, top right. Indices must retain this meaning in settings and composition. |
| Filters | `NoiseSuppressionToggle`, `NoiseGateToggle`, `LimiterToggle`: `Options_Changed`. |
| Overlays | `TitleOverlayBox`: `OverlayText_Changed`; `LogoPathBox` read-only; `ChooseLogo_Click`, `ClearLogo_Click`; `OverlayPositionPicker`: `Options_Changed`. |
| Replay | `ReplaySecondsBox` defaults to 30; `ReplayToggle` uses `ReplayToggle_Changed` and is labeled Enable replay with recording. `ReplayButton` / `Replay_Click` and `SaveReplayButton` / `SaveReplay_Click` sit after, outside, `StudioControls` so Stop/Save remain usable while configuration is locked. |
| Quick actions | `FloatingToggle`: `FloatingToggle_Changed`; `ToolbarButton`: `ShowQuick_Click`; `ScreenshotButton`: `Screenshot_Click`. |

`CaptureBadge.Child` remains a TextBlock because existing code updates its text directly. `RecordGlyph` and `RecordLabel` remain separate named TextBlocks within `RecordButton`. Existing quality-preset and encoder-selector index orders are unchanged.

## Validation status and next checks

At handoff, both XAML documents parse as XML, their 76 main-window names contain no duplicates, every explicit StaticResource key resolves in App.xaml, and all 31 referenced event handlers exist in the current main-window source files. Build and rendered UI verification are performed by the integrating task; no concurrent build was run for this design change.

Required visual and behavior checks: default and 760 × 500 sizes; 125% and 150% display scaling; keyboard-only navigation through collapsed panels and selectors; long device/file names; no attached microphone/camera; recording/paused/stopping states; live and replay stop controls while configuration is disabled; audio meters reset at session end; and no hidden active capture when returning to Record. Streaming, replay, filters, overlays, screenshot capture, and camera composition need their own runtime checks before release. Full OBS feature parity and comparative low-end-PC performance remain separate acceptance work.

## 0.3 scenes and motion

Scene Studio adds an original black/red composition workspace: named scenes on the left, a layout canvas, front-to-back layer list, and explicit Apply/Preview controls. The layout map uses static vector text and bounded decoded image previews; it does not run browser pages or cameras just to draw the editor. A captured frame is labeled as a snapshot. Numeric transform/crop fields provide a keyboard alternative to drag/resize.

The Studio scene card uses a quiet static red gradient and an explicit editor action. Short button press/release, page reveal and quick-menu reveal animations last 90–170 ms with cubic easing and a 30 fps scheduling hint. Windows animation preferences, high contrast, low rendering tier and the app toggle suppress motion; active capture suppresses it as well. No perpetual animation timer or blur/shadow effect runs while idle.

Reference research was refreshed against [Screen Studio's product interface](https://screen.studio/) and [quick-share controls](https://preview.screen.studio/guide/quick-share-widget-), alongside the earlier recorder references. The implementation uses its own XAML/drawing assets. Animation behavior follows the Windows [ClientAreaAnimation preference](https://learn.microsoft.com/en-us/dotnet/api/system.windows.systemparameters.clientareaanimation) and WPF's [DesiredFrameRate hint](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.animation.timeline.desiredframerate), which is not a hard rendering-rate guarantee.
