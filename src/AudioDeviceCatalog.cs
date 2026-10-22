using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Rilmo
{
    public sealed class AudioDevice
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        // A default row remains visible even when Windows has no default endpoint.
        // The UI can explain or disable that choice without storing a fake device ID.
        public bool IsAvailable { get; private set; }

        public AudioDevice(string id, string name, bool isAvailable = true)
        {
            Id = id;
            Name = name;
            IsAvailable = isAvailable;
        }

        public override string ToString() { return Name; }
    }

    /// <summary>
    /// Takes a fresh snapshot of active Windows audio endpoints. Does not load OBS,
    /// activate an audio client, request microphone samples, or start capture.
    /// Call on a background thread if enumeration would delay a UI interaction.
    /// Endpoint IDs are opaque strings suitable for win-wasapi's device_id setting.
    /// </summary>
    public static class AudioDeviceCatalog
    {
        private const uint DeviceStateActive = 1;
        private const int RpcChangedMode = unchecked((int)0x80010106);
        private const int NotFound = unchecked((int)0x80070490);
        private static readonly PropertyKey FriendlyNameKey = new PropertyKey
        {
            FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
            PropertyId = 14
        };

        public static AudioDevice[] GetInputs() { return Enumerate(DataFlow.Capture); }
        public static AudioDevice[] GetOutputs() { return Enumerate(DataFlow.Render); }

        private static AudioDevice[] Enumerate(DataFlow flow)
        {
            var devices = new List<AudioDevice>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection collection = null;
            AudioDevice defaultDevice = null;
            bool uninitialize = false;
            bool enumerationFailed = false;
            string kind = flow == DataFlow.Capture ? "microphone" : "output";
            try
            {
                // Balance both S_OK and S_FALSE. RPC_E_CHANGED_MODE means the caller
                // already owns an STA; use that apartment without changing it.
                int initialized = CoInitializeEx(IntPtr.Zero, 0);
                uninitialize = initialized >= 0;
                if (initialized < 0 && initialized != RpcChangedMode) Marshal.ThrowExceptionForHR(initialized);
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(flow, DeviceStateActive, out collection));
                uint count;
                Marshal.ThrowExceptionForHR(collection.GetCount(out count));
                for (uint index = 0; index < count; index++)
                {
                    IMMDevice endpoint = null;
                    try
                    {
                        Marshal.ThrowExceptionForHR(collection.Item(index, out endpoint));
                        uint state;
                        Marshal.ThrowExceptionForHR(endpoint.GetState(out state));
                        if ((state & DeviceStateActive) == 0) continue; // Device may have unplugged during enumeration.
                        string id = ReadId(endpoint);
                        if (String.IsNullOrWhiteSpace(id) || !seenIds.Add(id)) continue;
                        string fallback = (flow == DataFlow.Capture ? "Microphone " : "Audio output ") + (index + 1);
                        devices.Add(new AudioDevice(id, ReadFriendlyName(endpoint, fallback)));
                    }
                    catch (Exception ex)
                    {
                        // A single disappearing or faulty endpoint must not hide the others.
                        Trace.TraceWarning("Could not read an audio endpoint: {0}", ex.Message);
                    }
                    finally { ReleaseCom(endpoint); }
                }

                // OBS 32.2.x uses communications for default microphone capture,
                // and console for default desktop/system sound capture.
                defaultDevice = ReadDefault(enumerator, flow, kind);
            }
            catch (Exception ex)
            {
                enumerationFailed = true;
                Trace.TraceWarning("Could not enumerate audio {0} devices: {1}", kind, ex.Message);
            }
            finally
            {
                ReleaseCom(collection);
                ReleaseCom(enumerator);
                if (uninitialize) CoUninitialize();
            }

            devices.Sort((left, right) =>
            {
                int byName = StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
                return byName != 0 ? byName : StringComparer.OrdinalIgnoreCase.Compare(left.Id, right.Id);
            });
            if (defaultDevice == null)
            {
                string reason = enumerationFailed ? "device list unavailable" : devices.Count == 0 ? "no active devices" : "not configured";
                defaultDevice = new AudioDevice("default", "Default " + kind + " — " + reason, false);
            }
            devices.Insert(0, defaultDevice);
            return devices.ToArray();
        }

        private static AudioDevice ReadDefault(IMMDeviceEnumerator enumerator, DataFlow flow, string kind)
        {
            IMMDevice endpoint = null;
            try
            {
                int result = enumerator.GetDefaultAudioEndpoint(flow, flow == DataFlow.Capture ? Role.Communications : Role.Console, out endpoint);
                if (result == NotFound) return null;
                Marshal.ThrowExceptionForHR(result);
                uint state;
                Marshal.ThrowExceptionForHR(endpoint.GetState(out state));
                if ((state & DeviceStateActive) == 0) return null;
                string name = ReadFriendlyName(endpoint, "Windows " + kind);
                return new AudioDevice("default", "Default — " + name);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Could not read the default audio {0}: {1}", kind, ex.Message);
                return new AudioDevice("default", "Default " + kind + " — unavailable", false);
            }
            finally { ReleaseCom(endpoint); }
        }

        private static string ReadId(IMMDevice endpoint)
        {
            IntPtr value = IntPtr.Zero;
            try
            {
                Marshal.ThrowExceptionForHR(endpoint.GetId(out value));
                return Marshal.PtrToStringUni(value);
            }
            finally { if (value != IntPtr.Zero) Marshal.FreeCoTaskMem(value); }
        }

        private static string ReadFriendlyName(IMMDevice endpoint, string fallback)
        {
            IPropertyStore properties = null;
            var value = new PropVariant();
            try
            {
                Marshal.ThrowExceptionForHR(endpoint.OpenPropertyStore(0, out properties)); // STGM_READ
                PropertyKey key = FriendlyNameKey;
                Marshal.ThrowExceptionForHR(properties.GetValue(ref key, out value));
                string name = value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : null; // VT_LPWSTR
                return String.IsNullOrWhiteSpace(name) ? fallback : name;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Could not read an audio device name: {0}", ex.Message);
                return fallback;
            }
            finally
            {
                PropVariantClear(ref value);
                ReleaseCom(properties);
            }
        }

        private static void ReleaseCom(object value)
        {
            // Each object stays within this synchronous call and its COM apartment.
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }

        // Interop contracts: Windows SDK mmdeviceapi.h and propsys.h.
        // https://learn.microsoft.com/windows/win32/coreaudio/device-properties
        // https://learn.microsoft.com/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immdevice-getid
        private enum DataFlow { Render, Capture, All }
        private enum Role { Console, Multimedia, Communications }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey { public Guid FormatId; public uint PropertyId; }

        [StructLayout(LayoutKind.Sequential)]
        private struct Blob { public uint Size; public IntPtr Data; }

        [StructLayout(LayoutKind.Explicit)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort Type;
            [FieldOffset(8)] public IntPtr Pointer;
            // The native union is pointer-sized: 16 bytes total on x86, 24 on x64.
            [FieldOffset(8)] public Blob Storage;
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(DataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(DataFlow dataFlow, Role role, out IMMDevice endpoint);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int Item(uint index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid id, uint context, IntPtr activationParameters, out IntPtr instance);
            [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
            [PreserveSig] int GetId(out IntPtr id);
            [PreserveSig] int GetState(out uint state);
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
            [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
            [PreserveSig] int Commit();
        }

        [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoInitializeEx(IntPtr reserved, uint concurrencyModel);
        [DllImport("ole32.dll", ExactSpelling = true)] private static extern void CoUninitialize();
        [DllImport("ole32.dll", ExactSpelling = true)] private static extern int PropVariantClear(ref PropVariant value);
    }
}
