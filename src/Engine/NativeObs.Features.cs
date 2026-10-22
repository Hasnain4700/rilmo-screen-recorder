using System;
using System.Runtime.InteropServices;

namespace Rilmo.Engine
{
    // libobs 32.2.2 obs.h, obs-audio-controls.h, callback/calldata.h.
    internal static partial class NativeObs
    {
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_data_create_from_json([MarshalAs(UnmanagedType.LPUTF8Str)] string json);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_source_inc_active(IntPtr source);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_source_dec_active(IntPtr source);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_source_set_sync_offset(IntPtr source,long nanoseconds);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_source_set_monitoring_type(IntPtr source,int type);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] internal static extern bool obs_set_audio_monitoring_device([MarshalAs(UnmanagedType.LPUTF8Str)] string name,[MarshalAs(UnmanagedType.LPUTF8Str)] string id);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_data_set_obj(IntPtr data, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr value);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_data_set_double(IntPtr data, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, double value);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_source_set_volume(IntPtr source, float volume);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_source_set_muted(IntPtr source, [MarshalAs(UnmanagedType.I1)] bool muted);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_source_filter_add(IntPtr source, IntPtr filter);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_source_filter_remove(IntPtr source, IntPtr filter);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_sceneitem_remove(IntPtr item);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_sceneitem_set_pos(IntPtr item, ref Vec2 pos);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_sceneitem_set_rot(IntPtr item, float degrees);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] internal static extern bool obs_sceneitem_set_visible(IntPtr item, [MarshalAs(UnmanagedType.I1)] bool visible);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_volmeter_create(int type);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_volmeter_destroy(IntPtr meter);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] internal static extern bool obs_volmeter_attach_source(IntPtr meter, IntPtr source);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void MeterCallback(IntPtr param, IntPtr magnitude, IntPtr peak, IntPtr inputPeak);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_volmeter_add_callback(IntPtr meter, MeterCallback callback, IntPtr param);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_volmeter_remove_callback(IntPtr meter, MeterCallback callback, IntPtr param);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_service_create([MarshalAs(UnmanagedType.LPUTF8Str)] string id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr settings, IntPtr hotkeys);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_service_release(IntPtr service);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_output_set_service(IntPtr output, IntPtr service);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_output_set_reconnect_settings(IntPtr output, int retries, int seconds);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_output_get_signal_handler(IntPtr output);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_output_get_proc_handler(IntPtr output);
        [StructLayout(LayoutKind.Sequential)] internal struct CallData { internal IntPtr Stack; internal UIntPtr Size, Capacity; [MarshalAs(UnmanagedType.I1)] internal bool Fixed; }
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] internal static extern bool proc_handler_call(IntPtr handler, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref CallData data);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] internal static extern bool calldata_get_string(ref CallData data, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, out IntPtr value);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void bfree(IntPtr memory);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void SignalCallback(IntPtr param, IntPtr data);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void signal_handler_connect(IntPtr handler, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, SignalCallback callback, IntPtr param);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void signal_handler_disconnect(IntPtr handler, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, SignalCallback callback, IntPtr param);
    }
}
