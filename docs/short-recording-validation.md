# Short shared-encoder recordings

Starting a file while replay already uses the encoder previously returned success before the file had received its first keyframe. Stopping that file after one second could leave an invalid 716-byte MKV. Both FFprobe and FFmpeg rejected the original failing file in `artifacts/ui-smoke/results-20261022-000859-388/recordings/`.

The engine now waits for at least two video frames delivered to the recording output and nonzero mux bytes before reporting recording started. The wait is limited to five seconds. The UI remains in **Preparing** during this interval; it does not silently extend recording after the user presses Stop. A startup timeout cleans up the new file output, retaining an existing replay encoder and capture session. Native stop/release retains the previously documented driver/helper join limitations; a hard native stall is not proven bounded by this readiness deadline.

OBS 32.2.2 has no generic public force-keyframe API in [obs.h](https://github.com/obsproject/obs-studio/blob/32.2.2/libobs/obs.h). Its [encoder callback implementation](https://github.com/obsproject/obs-studio/blob/32.2.2/libobs/obs-encoder.c) waits for a keyframe for every newly attached output. The [output frame counter](https://github.com/obsproject/obs-studio/blob/32.2.2/libobs/obs-output.c) counts frames delivered to that output, which is the readiness signal used here. Encoder-wide counters would not establish that a newly added file has received video.

Finalization now requires delivered video frames, mux bytes, a nonempty file, and successful native stop/release. It no longer rejects a file solely because it is smaller than an arbitrary 1024-byte threshold. This is runtime completion evidence; release tests independently decode the files.

## Regression evidence

Run `tools/engine-features-short-recordings.ps1 -Decoder <ffmpeg.exe> -Probe <ffprobe.exe>`.

Passing run: `artifacts/short-recordings/results-20261022-001642-473/`.

- AMD `h264_texture_amf` and software `obs_x264`, each with immediate Stop, one-second recording, and a second immediate Stop while replay kept the encoder running.
- All six starts returned with two delivered video frames; all six stops preserved replay. Startup readiness took 572–1904 ms; stops took 120–154 ms on this machine.
- All six recordings and both subsequent replay saves passed FFprobe frame counting and complete FFmpeg `-xerror` decoding. Immediate-stop files contained 5–6 decoded frames; one-second files contained 34–36.
- A full UI smoke regression separately imports a portrait scene during landscape recording, edits it again, verifies native geometry and rendered pixels, stops/restarts recording while replay retains the session, and records for one second after startup returns. Copy/delete/export roundtrips verify editor selection persistence.

These are functional tests on the Ryzen 3 3200G/Vega 8 machine, not performance comparisons or an endurance test. A deliberately hung encoder was not injected; timeout cleanup was reviewed in code.
