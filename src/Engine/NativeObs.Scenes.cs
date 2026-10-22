// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Runtime.InteropServices;

namespace Rilmo.Engine
{
    internal static partial class NativeObs
    {
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_source_get_ref(IntPtr source);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_source_update(IntPtr source,IntPtr settings);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_source_inc_showing(IntPtr source);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_source_dec_showing(IntPtr source);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_sceneitem_set_order_position(IntPtr item,int position);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_transition_set(IntPtr transition,IntPtr source);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] internal static extern bool obs_transition_start(IntPtr transition,int mode,uint duration,IntPtr destination);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_transition_set_size(IntPtr transition,uint width,uint height);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_transition_force_stop(IntPtr transition);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] internal static extern bool obs_transition_is_active(IntPtr transition);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_transition_clear(IntPtr transition);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr obs_get_output_source(uint channel);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_source_video_render(IntPtr source);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_enter_graphics();
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void obs_leave_graphics();
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern ulong obs_get_video_frame_time();
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr gs_texrender_create(int format,int depthStencil);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void gs_texrender_destroy(IntPtr texrender);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] internal static extern bool gs_texrender_begin(IntPtr texrender,uint width,uint height);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void gs_texrender_end(IntPtr texrender);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr gs_texrender_get_texture(IntPtr texrender);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr gs_stagesurface_create(uint width,uint height,int format);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void gs_stagesurface_destroy(IntPtr surface);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void gs_stage_texture(IntPtr surface,IntPtr texture);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] internal static extern bool gs_stagesurface_map(IntPtr surface,out IntPtr data,out uint stride);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void gs_stagesurface_unmap(IntPtr surface);
        [StructLayout(LayoutKind.Sequential)] internal struct Vec4 {internal float X,Y,Z,W;}
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void gs_clear(uint flags,ref Vec4 color,float depth,byte stencil);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void gs_ortho(float left,float right,float top,float bottom,float near,float far);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void gs_blend_state_push();
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void gs_blend_state_pop();
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void gs_blend_function(int source,int destination);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void gs_projection_push();
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void gs_projection_pop();
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void gs_viewport_push();
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void gs_viewport_pop();
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void gs_set_viewport(int x,int y,int width,int height);
    }
}
