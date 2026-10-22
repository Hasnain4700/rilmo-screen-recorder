// libobs 32.2.2 C ABI. Definitions verified against upstream libobs/obs.h,
// obs-data.h and obs-properties.h. Only the required capture API is imported.
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Rilmo.Engine
{
    internal static partial class NativeObs
    {
        private const string Dll = "obs.dll";
        [StructLayout(LayoutKind.Sequential)] internal struct VideoInfo
        {
            [MarshalAs(UnmanagedType.LPUTF8Str)] internal string GraphicsModule;
            internal uint FpsNum, FpsDen, BaseWidth, BaseHeight, OutputWidth, OutputHeight;
            internal int Format;
            internal uint Adapter;
            [MarshalAs(UnmanagedType.I1)] internal bool GpuConversion;
            internal int ColorSpace, Range, ScaleType;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct AudioInfo { internal uint SampleRate; internal int Speakers; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void LogHandler(int level, IntPtr format, IntPtr args, IntPtr param);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void base_set_log_handler(LogHandler handler, IntPtr param);
        [DllImport("ucrtbase.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern int __stdio_common_vsprintf(ulong options, IntPtr buffer, UIntPtr size, IntPtr format, IntPtr locale, IntPtr args);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern IntPtr AddDllDirectory(string path);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool SetDefaultDllDirectories(uint flags);
        [DllImport("ole32.dll")] internal static extern int CoInitializeEx(IntPtr reserved, uint flags);
        [DllImport("ole32.dll")] internal static extern void CoUninitialize();
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice data, uint flags);
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] internal struct DisplayDevice
        {
            internal uint Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] internal string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] internal string DeviceString;
            internal uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] internal string DeviceId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] internal string DeviceKey;
        }
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool obs_startup([MarshalAs(UnmanagedType.LPUTF8Str)] string locale, [MarshalAs(UnmanagedType.LPUTF8Str)] string configPath, IntPtr profiler);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_shutdown();
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_get_version_string();
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_add_data_path([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int obs_reset_video(ref VideoInfo info);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool obs_reset_audio(ref AudioInfo info);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int obs_open_module(out IntPtr module, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string dataPath);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool obs_init_module(IntPtr module);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_post_load_modules();
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_data_create();
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_data_release(IntPtr data);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_data_set_string(IntPtr data, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_data_set_int(IntPtr data, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, long value);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_data_set_bool(IntPtr data, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.I1)] bool value);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_source_create([MarshalAs(UnmanagedType.LPUTF8Str)] string id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr settings, IntPtr hotkeys);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr ms_find_window(int searchMode, int priority, [MarshalAs(UnmanagedType.LPUTF8Str)] string className, [MarshalAs(UnmanagedType.LPUTF8Str)] string title, [MarshalAs(UnmanagedType.LPUTF8Str)] string exe);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_source_release(IntPtr source);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_set_output_source(uint channel, IntPtr source);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern uint obs_source_get_width(IntPtr source);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern uint obs_source_get_height(IntPtr source);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_source_set_audio_mixers(IntPtr source, uint mixers);
        [StructLayout(LayoutKind.Sequential)] internal struct Vec2 { internal float X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct SceneCrop { internal int Left, Top, Right, Bottom; }
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_scene_create_private([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_scene_release(IntPtr scene);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_scene_get_source(IntPtr scene);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_scene_add(IntPtr scene, IntPtr source);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_sceneitem_set_crop(IntPtr item, ref SceneCrop crop);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_sceneitem_set_bounds_type(IntPtr item, int type);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_sceneitem_set_bounds(IntPtr item, ref Vec2 bounds);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_sceneitem_set_alignment(IntPtr item, uint alignment);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_sceneitem_set_bounds_alignment(IntPtr item, uint alignment);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_sceneitem_set_scale_filter(IntPtr item, int filter);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_get_source_properties([MarshalAs(UnmanagedType.LPUTF8Str)] string id);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_properties_destroy(IntPtr props);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_properties_get(IntPtr props, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern UIntPtr obs_property_list_item_count(IntPtr property);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_property_list_item_string(IntPtr property, UIntPtr index);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_property_list_item_name(IntPtr property, UIntPtr index);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool obs_enum_encoder_types(UIntPtr index, out IntPtr id);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_video_encoder_create([MarshalAs(UnmanagedType.LPUTF8Str)] string id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr settings, IntPtr hotkeys);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_audio_encoder_create([MarshalAs(UnmanagedType.LPUTF8Str)] string id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr settings, UIntPtr mixer, IntPtr hotkeys);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_encoder_release(IntPtr encoder);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_encoder_set_video(IntPtr encoder, IntPtr video);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_encoder_set_audio(IntPtr encoder, IntPtr audio);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_get_video();
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern uint video_output_get_skipped_frames(IntPtr video);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_get_audio();
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_output_create([MarshalAs(UnmanagedType.LPUTF8Str)] string id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr settings, IntPtr hotkeys);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_output_release(IntPtr output);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_output_set_video_encoder(IntPtr output, IntPtr encoder);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_output_set_audio_encoder(IntPtr output, IntPtr encoder, UIntPtr index);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool obs_output_start(IntPtr output);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_output_stop(IntPtr output);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_output_force_stop(IntPtr output);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool obs_output_active(IntPtr output);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool obs_output_can_pause(IntPtr output);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool obs_output_pause(IntPtr output, [MarshalAs(UnmanagedType.I1)] bool pause);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool obs_output_paused(IntPtr output);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_output_get_last_error(IntPtr output);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern ulong obs_output_get_total_bytes(IntPtr output);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int obs_output_get_frames_dropped(IntPtr output);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int obs_output_get_total_frames(IntPtr output);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern uint obs_get_lagged_frames();
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern double obs_get_active_fps();
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern ulong obs_get_average_frame_time_ns();
        internal static string Text(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return String.Empty;
            int length = 0; while (Marshal.ReadByte(ptr, length) != 0) length++;
            byte[] bytes = new byte[length]; Marshal.Copy(ptr, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
