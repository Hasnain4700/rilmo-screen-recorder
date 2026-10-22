using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Rilmo.Engine
{
    public sealed class CaptureWindow
    {
        public IntPtr Handle { get; internal set; }
        public string Title { get; internal set; }
        public string AppName { get; internal set; }
        internal string Selector { get; set; }
        internal string ClassName { get; set; }
        public override string ToString() { return Title + "  (" + AppName + ")"; }
    }

    // Device discovery is independent of OBS, keeping the idle application lightweight.
    internal static class WindowCatalog
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate bool EnumWindowCallback(IntPtr window, IntPtr param);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr param);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int length);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint process);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder text, ref uint length);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int length);

        internal static CaptureWindow[] Enumerate()
        {
            List<CaptureWindow> result = new List<CaptureWindow>();
            EnumWindowCallback callback = delegate(IntPtr handle, IntPtr ignored)
            {
                CaptureWindow window = Describe(handle);
                if (window != null) result.Add(window);
                return true;
            };
            EnumWindows(callback, IntPtr.Zero); GC.KeepAlive(callback);
            result.Sort(delegate(CaptureWindow a, CaptureWindow b) { return StringComparer.CurrentCultureIgnoreCase.Compare(a.Title, b.Title); });
            return result.ToArray();
        }

        internal static CaptureWindow Describe(IntPtr window)
        {
            if (!IsWindow(window) || !IsWindowVisible(window) || IsIconic(window)) return null;
            int cloaked;
            if (DwmGetWindowAttribute(window, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0) return null;
            StringBuilder title = new StringBuilder(4096), className = new StringBuilder(1024);
            if (GetWindowText(window, title, title.Capacity) == 0 || GetClassName(window, className, className.Capacity) == 0) return null;
            // OBS deliberately excludes capture of windows belonging to its own host process.
            uint processId; GetWindowThreadProcessId(window, out processId);
            if (processId == Process.GetCurrentProcess().Id) return null;
            IntPtr process = OpenProcess(0x1000, false, processId);
            if (process == IntPtr.Zero) return null;
            try
            {
                StringBuilder path = new StringBuilder(32768); uint length = (uint)path.Capacity;
                if (!QueryFullProcessImageName(process, 0, path, ref length)) return null;
                string executable = Path.GetFileName(path.ToString());
                return new CaptureWindow { Handle = window, Title = title.ToString(), AppName = executable,
                    ClassName = className.ToString(),
                    Selector = Encode(title.ToString()) + ":" + Encode(className.ToString()) + ":" + Encode(executable) };
            }
            finally { CloseHandle(process); }
        }
        private static string Encode(string value) { return value.Replace("#", "#22").Replace(":", "#3A"); }
    }
}
