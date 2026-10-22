// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Runtime.InteropServices;

namespace Rilmo.Engine
{
    internal static class WindowsPackageIdentity
    {
        private static readonly Lazy<bool> packaged = new Lazy<bool>(() =>
        {
            uint length = 0;
            // ERROR_INSUFFICIENT_BUFFER means a package name exists. An
            // unpackaged process returns APPMODEL_ERROR_NO_PACKAGE instead.
            return GetCurrentPackageFullName(ref length, IntPtr.Zero) == 122;
        });
        internal static bool IsPackaged => packaged.Value;

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);
    }
}
