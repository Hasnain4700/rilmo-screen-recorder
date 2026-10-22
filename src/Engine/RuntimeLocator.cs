// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.IO;

namespace Rilmo.Engine
{
    /// <summary>One package-relative native runtime for capture and export.</summary>
    internal static class RuntimeLocator
    {
        internal static string ResolveRoot()
        {
            // Never consult PATH, Program Files, the current directory, or a user
            // environment override. Relocating the application relocates its runtime.
            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", "obs"));
        }

        internal static string ResolveBinDirectory()
        {
            // libobs starts native helpers beside the host EXE. Keeping their DLL
            // dependency closure here avoids wrappers and duplicate native libraries.
            return Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        }

        internal static bool IsAvailable
        {
            get
            {
                string root = ResolveRoot();
                string bin = ResolveBinDirectory();
                return File.Exists(Path.Combine(bin, "obs.dll")) &&
                    File.Exists(Path.Combine(bin, "libobs-d3d11.dll")) &&
                    Directory.Exists(Path.Combine(root, "data", "libobs")) &&
                    File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "obs-ffmpeg-mux.exe"));
            }
        }
    }
}
