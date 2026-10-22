// SPDX-License-Identifier: GPL-2.0-or-later
// P010's standard DirectShow FOURCC subtype is absent in Zig 0.14.1's
// MinGW header snapshot. This adds the standard GUID, not a new media format.
#include <windows.h>
static const GUID RILMO_MEDIASUBTYPE_P010 = {0x30313050,0x0000,0x0010,{0x80,0x00,0x00,0xaa,0x00,0x38,0x9b,0x71}};
#define MEDIASUBTYPE_P010 RILMO_MEDIASUBTYPE_P010
