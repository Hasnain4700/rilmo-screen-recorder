// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Runtime.InteropServices;
namespace Rilmo.Engine
{
    internal static partial class NativeObs
    {
        [DllImport("rilmo-virtual-camera64.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rilmo_camera_start(IntPtr video, uint width, uint height, uint fpsNumerator, uint fpsDenominator);
        [DllImport("rilmo-virtual-camera64.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int rilmo_camera_stop();
        [DllImport("rilmo-virtual-camera64.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong rilmo_camera_frame_count();
    }
}
