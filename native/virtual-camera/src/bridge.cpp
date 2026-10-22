// SPDX-License-Identifier: GPL-2.0-or-later
// Rilmo native producer. ABI layouts below are from OBS 32.2.2 video-io.h.
// The external camera DLL has no libobs dependency in camera consumer apps.
#include <windows.h>
#include <stdint.h>
#include <stddef.h>
#include "shared-memory-queue.h"

struct video_data { uint8_t *data[8]; uint32_t linesize[8]; uint64_t timestamp; };
struct video_scale_info { int format; uint32_t width, height; int range, colorspace; };
typedef void (*frame_callback)(void *, video_data *);
typedef void (*connect_fn)(const video_scale_info *, frame_callback, void *);
typedef void (*disconnect_fn)(frame_callback, void *);
static_assert(offsetof(video_data, timestamp) == sizeof(void *) * 8 + 32, "OBS video_data ABI");
static_assert(sizeof(video_scale_info) == 20, "OBS video_scale_info ABI");
static SRWLOCK lifecycle = SRWLOCK_INIT;
static video_queue_t *queue = nullptr;
static void *video = nullptr;
static disconnect_fn disconnect_video = nullptr;
static volatile LONG64 frames = 0;
static HANDLE first_frame = nullptr;

static void on_frame(void *, video_data *frame)
{
    // obs_remove_raw_video_callback disconnects under the video callback mutex.
    // No allocation or managed transition occurs on this hot path.
    if (queue && frame && frame->data[0] && frame->data[1]) {
        video_queue_write(queue, frame->data, frame->linesize, frame->timestamp);
        InterlockedIncrement64(&frames);
        if (first_frame) SetEvent(first_frame);
    }
}

extern "C" __declspec(dllexport) int rilmo_camera_start(void *source, uint32_t width, uint32_t height, uint32_t fps_num, uint32_t fps_den)
{
    if (!source || width < 16 || height < 16 || width > 4096 || height > 4096 || (width & 1) || (height & 1) || !fps_num || !fps_den) return 1;
    AcquireSRWLockExclusive(&lifecycle);
    if (queue) { ReleaseSRWLockExclusive(&lifecycle); return 2; }
    HMODULE obs = GetModuleHandleW(L"obs.dll");
    auto connect_video = obs ? (connect_fn)GetProcAddress(obs, "obs_add_raw_video_callback") : nullptr;
    disconnect_video = obs ? (disconnect_fn)GetProcAddress(obs, "obs_remove_raw_video_callback") : nullptr;
    if (!connect_video || !disconnect_video) { ReleaseSRWLockExclusive(&lifecycle); return 3; }
    uint64_t interval = (uint64_t)fps_den * 10000000ULL / fps_num;
    queue = video_queue_create(width, height, interval);
    if (!queue) { ReleaseSRWLockExclusive(&lifecycle); return 4; }
    video_scale_info conversion = {2 /* NV12 */, width, height, 1 /* partial */, 2 /* 709 */};
    InterlockedExchange64(&frames, 0);
    video = source;
    first_frame = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!first_frame) { video_queue_close(queue); queue=nullptr; video=nullptr; ReleaseSRWLockExclusive(&lifecycle); return 5; }
    // The OBS wrapper also activates the canvas raw-video path; connecting
    // video_output directly leaves that canvas idle when no encoder is active.
    connect_video(&conversion, on_frame, nullptr);
    if (WaitForSingleObject(first_frame, 5000) != WAIT_OBJECT_0) {
        disconnect_video(on_frame, nullptr);
        CloseHandle(first_frame); first_frame=nullptr;
        video_queue_close(queue); queue = nullptr; video = nullptr;
        ReleaseSRWLockExclusive(&lifecycle); return 5;
    }
    ReleaseSRWLockExclusive(&lifecycle);
    return 0;
}

extern "C" __declspec(dllexport) int rilmo_camera_stop()
{
    AcquireSRWLockExclusive(&lifecycle);
    if (video && disconnect_video) disconnect_video(on_frame, nullptr);
    video = nullptr;
    if (first_frame) { CloseHandle(first_frame); first_frame=nullptr; }
    video_queue_close(queue); queue = nullptr;
    ReleaseSRWLockExclusive(&lifecycle);
    return 0;
}

extern "C" __declspec(dllexport) uint64_t rilmo_camera_frame_count()
{
    return (uint64_t)InterlockedCompareExchange64(&frames, 0, 0);
}
