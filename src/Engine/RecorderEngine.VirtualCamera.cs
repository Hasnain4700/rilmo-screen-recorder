// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.IO;
using Microsoft.Win32;
namespace Rilmo.Engine
{
    public sealed class VirtualCameraCapability
    {
        public bool CanStart { get; internal set; }
        public bool NeedsSetup { get; internal set; }
        public string Message { get; internal set; }
        public string DeviceName { get { return "Rilmo Virtual Camera"; } }
    }
    public sealed partial class RecorderEngine
    {
        public const string VirtualCameraClassId = "{9E7FF7BC-AA7A-4B1D-8A9E-DDDFB0692DFD}";
        public bool IsVirtualCameraActive { get; private set; }
        public ulong VirtualCameraFrames { get { return IsVirtualCameraActive ? NativeObs.rilmo_camera_frame_count() : 0; } }
        public static VirtualCameraCapability GetVirtualCameraCapability()
        {
            var result = new VirtualCameraCapability();
            if (!AppEdition.OffersVirtualCamera)
            {
                // A Store package must never fall through to the legacy
                // machine-wide DirectShow registration/repair path.
                result.Message = "Virtual camera is not included in this Microsoft Store release.";
                return result;
            }
            string component = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rilmo-virtual-camera64.dll");
            if (!File.Exists(component)) { result.Message = "The Rilmo camera component is missing. Rebuild or repair this app."; return result; }
            using (RegistryKey classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64))
            using (RegistryKey key = classes.OpenSubKey(@"CLSID\" + VirtualCameraClassId + @"\InprocServer32"))
            {
                string installed = key == null ? null : key.GetValue(null) as string;
                result.CanStart = !String.IsNullOrEmpty(installed) && File.Exists(installed) && String.Equals(Path.GetFileName(installed), "rilmo-virtual-camera64.dll", StringComparison.OrdinalIgnoreCase);
            }
            result.NeedsSetup = !result.CanStart;
            result.Message = result.CanStart ? "Ready. Choose Rilmo Virtual Camera in your meeting app after starting it here." : "One-time camera setup is required. It adds the Rilmo Virtual Camera device to Windows; it does not change OBS.";
            return result;
        }
        public void StartVirtualCamera()
        {
            RequireSession();
            if (IsVirtualCameraActive) throw new InvalidOperationException("Virtual camera is already active.");
            if (IsPaused) throw new InvalidOperationException("Resume the recording before starting virtual camera.");
            VirtualCameraCapability capability = GetVirtualCameraCapability();
            if (!capability.CanStart) throw new InvalidOperationException(capability.Message);
            int result = NativeObs.rilmo_camera_start(NativeObs.obs_get_video(), (uint)sessionOptions.Width, (uint)sessionOptions.Height, (uint)sessionOptions.Fps, 1);
            if (result != 0) throw new InvalidOperationException(result == 4 ? "Another Rilmo virtual camera producer is using the camera. Stop it before starting this session." : "Virtual camera could not start (code " + result + ").");
            IsVirtualCameraActive = true;
            Log("Rilmo Virtual Camera started. Video only; microphone stays under the meeting app's control.");
        }
        public void StopVirtualCamera()
        {
            if (!IsVirtualCameraActive) return;
            if (NativeObs.rilmo_camera_stop() != 0) throw new InvalidOperationException("Virtual camera is still stopping. Keep this session open and try again.");
            IsVirtualCameraActive = false;
            Log("Rilmo Virtual Camera stopped.");
        }
    }
}
