# Rilmo Recorder privacy policy — draft

**Not ready for publication.** The publisher must fill the placeholders, verify the release data flows, and complete the notes at the end before placing this policy on a public URL. Prepared against the current standalone recording/streaming preview on 22 October 2026; a packaged release may change storage behavior.

Effective date: **[PUBLISHER TO SET]**  
Publisher/data controller: **[LEGAL NAME AND ADDRESS]**  
Privacy and support contact: **[WORKING CONTACT ADDRESS]**

Rilmo Recorder records the screen and audio sources you select and saves recordings on your computer. Screen contents, voices, notifications, and other information visible or audible during recording may contain personal or confidential information. Choose the appropriate sources and obtain any permissions you need before recording or sharing someone else's information.

## Recording content

Capture begins through the app's Record, Replay, Go live, virtual-camera, or scene-preview controls. You can select a display, window, game, or area, and optionally add camera, image, text, and browser sources. System sound and microphone recording are configurable separately. Screenshots save an image locally. Replay buffering retains a recent segment in memory until you save it or stop the buffer; a saved replay becomes a local media file.

The app writes recording files to the output folder you select. Its current default is the `Rilmo Recordings` folder in your Windows Videos location; a custom output location may be selected. Recordings are ordinary local media files and are not automatically encrypted by the app. Windows account permissions, disk encryption, or backup software may provide separate protection.

We do not receive recordings through the app's current local-recording workflow. If you choose a folder synchronized by another service, that service may upload the files under its own settings and privacy policy. If you open or share a recording through another app, that app's data handling applies.

When you explicitly use Go live, the selected picture and audio are sent to the RTMP/RTMPS server you provide. That server or streaming service controls subsequent storage, distribution, audience access, and processing under its own terms. Local recording and live streaming can run independently: stopping one may leave the other active, so use each visible output control. RTMPS encrypts the transport to its server; RTMP does not provide that transport encryption. The app does not operate a hosting service or an online recording community.

When you explicitly start virtual camera, the composed video is available locally to applications that select **Rilmo Virtual Camera**. Rilmo's camera component does not transmit that video over the network or provide a microphone device. A meeting, browser, or other camera consumer may record or transmit it under that application's settings and privacy policy. Stopping recording does not stop virtual camera. Use the camera's Stop control to stop sharing live pictures; connected clients then receive an inactive slate. One-time camera setup is a separate administrator-approved Windows device-registration action, not capture consent for future sessions.

## Browser sources and automatic compatibility requests

An enabled browser source loads the URL or local HTML file you select using an embedded browser. It may contact that site and third parties referenced by the page, execute page scripts, and store cookies or other browser state locally. The destination can receive ordinary request/network information such as the connecting IP address. Site content and data handling are controlled by those providers. Local HTML can also request remote content. **[PUBLISHER: verify the final browser profile location, persistence, cookie controls, and clearing behavior before publication.]**

The bundled OBS Windows-capture module also attempts an automatic compatibility-data check when it initializes, including `https://obsproject.com/obs2_update/win-capture/v1/package.json`. This is separate from browser sources and live streaming, so the current build must not be described as having zero automatic network activity. The remote server can observe connection/request information. Local logs confirm the attempted request; its full headers, payload, retention, and all redirect/update endpoints have not been independently audited. **[PUBLISHER: complete that audit, disclose the data and recipient accurately, and decide whether the release should disable this update path or expose a control.]** The observed check is for capture-compatibility data; we have not observed a recording upload through it.

## Preferences and diagnostic files

The app stores preferences such as the output folder, selected capture/audio choices, quality settings, overlay paths, interface options, and streaming-server address on your device. Stream keys remain in the current session's password field and are not saved in the settings file. The unpackaged preview stores settings and diagnostic data beneath `%LOCALAPPDATA%\Rilmo Recorder`.

Local diagnostic logs may contain recording paths, device/display identifiers and names, hardware/encoder information, loaded-module details, timestamps, and error messages. Paths can contain your Windows account name. Native engine logs may include additional technical details. These logs should be treated as potentially personal information, not assumed to be anonymous.

The current app does not automatically send these logs to the publisher. If you send a log or recording to support, inspect it first and remove information you do not want to share. **[PUBLISHER: describe the support provider, processing purpose, access, retention period, and any international transfers before accepting such submissions.]**

## Access and controls

You can stop recording, replay buffering, streaming, and virtual camera independently, and choose whether to include microphone, system audio, or a camera. Windows privacy controls may block microphone or camera access. The app should explain unavailable devices and permissions rather than bypassing those settings.

You can view, move, or delete saved recordings using the app's folder action or File Explorer. Deleting a library entry or changing an output path must not be described as deleting a media file unless that behavior is explicitly implemented. Preferences and logs can be removed from the app's local data folder after the app is closed; this may reset settings. The preview does not establish an automatic log-retention period.

**[PUBLISHER: confirm packaged data locations and uninstall behavior. State exactly which files are retained and how users can remove them. Do not promise that uninstall deletes recordings.]**

## Accounts, analytics, and third parties

The current preview does not require a user account and has no advertising or analytics integration in the application source. Windows and the Microsoft Store may separately process installation, purchase, update, or operating-system diagnostics under their own policies. The use of an open-source recording engine does not, by itself, mean your recordings are sent to its developers.

**[PUBLISHER: verify the final package and native dependency network behavior before making a definitive no-telemetry statement. Document streaming destinations, any service-directory refreshes, and any added crash reporting, licensing, payment, or update services.]**

## Security, retention, and privacy requests

Keep your Windows account and output location secure. A recording may contain information from any application visible on the selected display. The app does not guarantee that local files are inaccessible to other software or people who can access your account or storage.

You control the retention of recordings on your computer. **[PUBLISHER: supply the final policy for logs, settings, support messages, backups, legal retention, and deletion requests.]**

To ask questions or exercise applicable privacy rights for information held by the publisher, contact **[PRIVACY CONTACT]**. **[PUBLISHER: specify the applicable process and jurisdictional rights based on actual markets and processing, without implying the publisher can remotely delete files it never receives.]**

## Changes

We will update this policy if the app's data practices change. **[PUBLISHER: define where updates appear and how users receive material-change notices.]**

## Internal publication checks — remove from final policy

1. Identify the publisher and actual contact route; provide an effective date and stable public HTTPS policy URL.
2. Check every statement against the release package, especially native logs/network behavior, microphone/camera controls, replay storage, independent live/record outputs, stream-key handling, packaged local-data paths, uninstall, and output-folder defaults.
3. Specify retention and support handling. Do not fill unknowns with unsupported promises such as “no personal data,” “fully anonymous,” “encrypted,” or “deleted automatically.”
4. Make the policy reachable in the app and Partner Center. This draft does not replace Microsoft's required privacy-policy URL or a final legal review.
5. Microsoft's [Store privacy requirements](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies#105-personal-information) apply to Win32 apps even when their main workflow saves locally.
