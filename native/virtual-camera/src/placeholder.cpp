// SPDX-License-Identifier: GPL-2.0-or-later
// Original neutral NV12 slate. No OBS branding or external image dependency.
#include <stdint.h>
#include <vector>
#include <mutex>
static std::vector<uint8_t> slate;
static std::once_flag initialized;
bool initialize_placeholder()
{
    std::call_once(initialized, [] {
        slate.resize(640 * 360 * 3 / 2, 128);
        for (int y = 0; y < 360; y++) for (int x = 0; x < 640; x++)
            slate[y * 640 + x] = (x > 270 && x < 370 && y > 140 && y < 220) ? 68 : 24;
    });
    return true;
}
const uint8_t *get_placeholder_ptr() { return slate.data(); }
const bool get_placeholder_size(int *x, int *y) { *x = 640; *y = 360; return true; }
