// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Rilmo.Engine
{
    public sealed partial class RecorderEngine
    {
        private static IntPtr browserSearchDirectory;
        private static bool browserModuleEverInitialized;
        private static string BrowserRoot => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", "browser");

        // Discovery is file-only: it never starts Chromium or initializes libobs.
        public static bool BrowserRuntimeAvailable =>
            File.Exists(Path.Combine(BrowserRoot, "browser-runtime-inventory.json")) &&
            File.Exists(Path.Combine(BrowserRoot, "bin", "obs-browser.dll")) &&
            File.Exists(Path.Combine(BrowserRoot, "bin", "obs-browser-page.exe")) &&
            File.Exists(Path.Combine(BrowserRoot, "bin", "libcef.dll"));

        // Called by the scene factory; the caller owns the returned native reference.
        private IntPtr CreateAdvancedSource(SceneSource model, string uniqueName)
        {
            CheckThread();
            if (!initialized) throw new InvalidOperationException("Initialize a capture session before creating sources.");
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (model.Kind == SceneSourceKind.Game) return CreateGameSceneSource(model, uniqueName);
            if (model.Kind == SceneSourceKind.Browser) return CreateBrowserSceneSource(model, uniqueName);
            throw new ArgumentException("Unsupported advanced scene source.");
        }

        private IntPtr CreateGameSceneSource(SceneSource model, string uniqueName)
        {
            if (!String.Equals(model.GameCaptureMode, "window", StringComparison.Ordinal))
                throw new ArgumentException("Game capture requires an explicitly selected game window.");
            CaptureWindow window = WindowCatalog.Describe(model.WindowHandle);
            if (window == null) throw new ArgumentException("Open and restore the selected game, then select its window again.");
            int matches = 0;
            foreach (CaptureWindow candidate in WindowCatalog.Enumerate())
                if (String.Equals(candidate.Selector, window.Selector, StringComparison.OrdinalIgnoreCase)) matches++;
            if (matches != 1 || NativeObs.ms_find_window(1, 1, window.ClassName, window.Title, window.AppName) != window.Handle)
                throw new InvalidOperationException("The game window cannot be matched uniquely. Give it a unique title or choose window capture.");

            IntPtr settings = NativeObs.obs_data_create();
            try
            {
                NativeObs.obs_data_set_string(settings, "capture_mode", "window");
                NativeObs.obs_data_set_string(settings, "window", window.Selector);
                NativeObs.obs_data_set_int(settings, "priority", 1); // Require title matching; the selected native target was validated above.
                NativeObs.obs_data_set_bool(settings, "capture_cursor", model.CaptureCursor);
                NativeObs.obs_data_set_bool(settings, "limit_framerate", true);
                NativeObs.obs_data_set_bool(settings, "capture_overlays", false);
                NativeObs.obs_data_set_bool(settings, "capture_audio", false);
                NativeObs.obs_data_set_bool(settings, "allow_transparency", false);
                NativeObs.obs_data_set_bool(settings, "sli_compatibility", false);
                // Use the standard upstream injection path for the selected game.
                NativeObs.obs_data_set_bool(settings, "anti_cheat_hook", false);
                NativeObs.obs_data_set_int(settings, "hook_rate", 1);
                IntPtr source = NativeObs.obs_source_create("game_capture", uniqueName, settings, IntPtr.Zero);
                if (source == IntPtr.Zero) throw new InvalidOperationException("Game capture is unavailable in this runtime.");
                NativeObs.obs_source_set_audio_mixers(source, 0);
                return source;
            }
            finally { NativeObs.obs_data_release(settings); }
        }

        private IntPtr CreateBrowserSceneSource(SceneSource model, string uniqueName)
        {
            if (model.BrowserWidth < 16 || model.BrowserWidth > 8192 || model.BrowserHeight < 16 || model.BrowserHeight > 8192)
                throw new ArgumentException("Browser dimensions must be between 16 and 8192 pixels.");
            if (model.BrowserFps < 1 || model.BrowserFps > 60)
                throw new ArgumentException("Browser frame rate must be between 1 and 60; 30 is recommended.");
            bool local = !String.IsNullOrWhiteSpace(model.FilePath);
            string address;
            if (local)
            {
                address = Path.GetFullPath(model.FilePath);
                if (!File.Exists(address)) throw new FileNotFoundException("The browser source HTML file is missing.", address);
                string extension = Path.GetExtension(address);
                if (!extension.Equals(".html", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".htm", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Choose an HTML file for a local browser source.");
                if (new FileInfo(address).Length > 32L * 1024 * 1024) throw new ArgumentException("Choose an HTML file smaller than 32 MB.");
            }
            else
            {
                if (!Uri.TryCreate(model.BrowserUrl, UriKind.Absolute, out Uri uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !String.IsNullOrEmpty(uri.UserInfo))
                    throw new ArgumentException("Enter an HTTP or HTTPS browser URL without embedded credentials.");
                address = uri.AbsoluteUri;
            }

            EnsureBrowserModule();
            IntPtr settings = NativeObs.obs_data_create();
            try
            {
                NativeObs.obs_data_set_bool(settings, "is_local_file", local);
                NativeObs.obs_data_set_string(settings, local ? "local_file" : "url", address);
                NativeObs.obs_data_set_int(settings, "width", model.BrowserWidth);
                NativeObs.obs_data_set_int(settings, "height", model.BrowserHeight);
                NativeObs.obs_data_set_bool(settings, "fps_custom", true);
                NativeObs.obs_data_set_int(settings, "fps", model.BrowserFps);
                NativeObs.obs_data_set_bool(settings, "shutdown", true);
                NativeObs.obs_data_set_bool(settings, "restart_when_active", false);
                NativeObs.obs_data_set_int(settings, "webpage_control_level", 0); // No web-page access to recording controls.
                // Route audio away from Windows playback, then mute this visual-only layer.
                NativeObs.obs_data_set_bool(settings, "reroute_audio", true);
                IntPtr source = NativeObs.obs_source_create("browser_source", uniqueName, settings, IntPtr.Zero);
                if (source == IntPtr.Zero) throw new InvalidOperationException("The browser source could not be created.");
                NativeObs.obs_source_set_audio_mixers(source, 0);
                NativeObs.obs_source_set_muted(source, true);
                return source;
            }
            finally { NativeObs.obs_data_release(settings); }
        }

        private void EnsureBrowserModule()
        {
            if (featureModules.Contains("obs-browser")) return;
            if(browserModuleEverInitialized)
                throw new InvalidOperationException("Browser scenes need a fresh isolated capture process. Use RecorderClient for browser sessions.");
            if (!BrowserRuntimeAvailable)
                throw new FileNotFoundException("The optional browser runtime is missing. Install a Rilmo build containing the browser component.");
            if (!NativeObs.Text(NativeObs.obs_get_version_string()).StartsWith("32.2.2", StringComparison.Ordinal))
                throw new InvalidOperationException("The browser component requires the pinned OBS 32.2.2 runtime.");
            string bin = Path.Combine(BrowserRoot, "bin");
            lock (InstanceGate)
            {
                if (browserSearchDirectory == IntPtr.Zero)
                {
                    browserSearchDirectory = NativeObs.AddDllDirectory(bin);
                    if (browserSearchDirectory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Browser dependency search could not be configured.");
                }
            }
            IntPtr privateData = NativeObs.obs_get_private_data();
            try { NativeObs.obs_data_set_bool(privateData, "BrowserHWAccel", true); }
            finally { NativeObs.obs_data_release(privateData); }
            int result = NativeObs.obs_open_module(out IntPtr module, Path.Combine(bin, "obs-browser.dll").Replace('\\', '/'), Path.Combine(BrowserRoot, "data", "obs-browser").Replace('\\', '/'));
            if (result != 0 || !NativeObs.obs_init_module(module))
                throw new InvalidOperationException("The optional browser module could not load (" + result + "). Repair the browser component.");
            browserModuleEverInitialized=true;
            NativeObs.obs_post_load_modules();
            featureModules.Add("obs-browser");
            Log("Optional browser module initialized on request. OBS frontend UI is not started.");
        }
    }
}
