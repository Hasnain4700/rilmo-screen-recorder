// SPDX-License-Identifier: GPL-2.0-or-later
// OBS 32.2.2 libobs/obs.h. obs_get_private_data returns an owned data reference.
using System;
using System.Runtime.InteropServices;

namespace Rilmo.Engine
{
    internal static partial class NativeObs
    {
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr obs_get_private_data();
    }
}
