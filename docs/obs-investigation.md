# OBS investigation ledger

## Selected artifact

- User-supplied installation: `C:\Program Files\obs-studio\bin\64bit`.
- REA target: `obs64.exe`, PE x86-64, SHA-256 `e79066670cd8e8b95662f4cd417c4fa6f5b0c77e856a7b41c7de0e00059ba6f2`.
- Windows file-version observation: 32.2.2. This is an ordinary filesystem observation, not a decompilation result.
- REA evidence export: `../obs-rea-evidence.json`.

## REA observations

`open_binary` established the target identity. `inspect_pe_resources` produced Evidence ID `ev_2c0a40b0282ef10857e1175ca0dcaebddebfbcfb8f1d8f3839bbeba6b9826f7f`: 36 directory entries examined, 16 resources, complete coverage of this bounded PE resource tree. Icon group references, file offsets and payload hashes were retained. The resource parser does not establish UI behavior, capture algorithms, runtime performance or original source.

`binary_overview` returned `provider_unavailable`: Ghidra was not configured (`GHIDRA_INSTALL_DIR` absent), IDA was not configured, Hopper was unsupported on Windows, and Kuna was not configured/admitted for this PE route. No decompiled functions or call paths were obtained. Registration itself worked; changing MCP setup would not repair missing analysis engines. The active target was closed after evidence export.

## Source and runtime cross-check

OBS is open source. Implementation work uses its official C API and source for the installed release instead of inventing recovered source. The original interface was built independently. Useful primary references:

- [OBS 32.2.2 source](https://github.com/obsproject/obs-studio/tree/32.2.2)
- [libobs public API header](https://github.com/obsproject/obs-studio/blob/32.2.2/libobs/obs.h)
- [Windows capture module](https://github.com/obsproject/obs-studio/tree/32.2.2/plugins/win-capture)
- [WASAPI audio module](https://github.com/obsproject/obs-studio/tree/32.2.2/plugins/win-wasapi)
- [FFmpeg outputs and AMF](https://github.com/obsproject/obs-studio/tree/32.2.2/plugins/obs-ffmpeg)

Ordinary filesystem inventory observed separate Qt frontend DLLs, native libobs/D3D11 modules, and a CEF browser component. The CEF DLL's on-disk size is not a RAM or CPU measurement. Choosing to omit browser/Qt from the default recorder is a design decision; its measured effect needs matched benchmarks.

Actual local engine tests demonstrate recording, hardware encoder use, fallback capture, pause/resume, stop/restart and container decoding. Those are controlled development test observations, **not REA execution evidence**. See `engine-validation.md` for their precise scope and limitations.

## Open questions

| Question | Status |
|---|---|
| Which artifact/version is installed? | Answered within the file-version and SHA identity bounds above. |
| Does REA native decompilation work on this machine? | Unavailable with current providers. |
| Can an original minimal frontend use the engine directly? | Demonstrated in bounded local recording tests. |
| Does the recorder outperform OBS under matched workloads? | Unresolved; no matched benchmark completed. |
| Is all OBS functionality implemented? | No; feature matrix records remaining milestones. |
| Is the app one-click Store distributable? | No; runtime/source/package/certification gates remain. |
