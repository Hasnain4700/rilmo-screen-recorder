// SPDX-License-Identifier: GPL-2.0-or-later
// Native ABI: OBS Studio 32.2.2 libobs/media-io/media-remux.h.
// https://github.com/obsproject/obs-studio/blob/32.2.2/libobs/media-io/media-remux.h
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Rilmo
{
    /// <summary>
    /// Copies the compressed streams from an MKV into MP4 using libobs; no capture
    /// initialization or external ffmpeg process is involved. Unsupported streams fail.
    /// The source is held read-only. The requested output appears only after success.
    /// </summary>
    public static class Remuxer
    {
        private static readonly object LibraryGate = new object();
        private static NativeApi native;

        /// <summary>Runs the blocking remux on a pool thread. Progress is 0..100 on that worker thread.</summary>
        public static Task RemuxAsync(string input, string output, Action<double> progress, CancellationToken cancellationToken)
        {
            return Task.Run(() => Remux(input, output, progress, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// Blocking worker-thread entry point. Never overwrite an existing output.
        /// Cancellation is cooperative at native packet callbacks; it may not interrupt
        /// stream probing or a blocked disk operation immediately. Progress handlers must
        /// marshal UI work themselves; exceptions are captured before leaving native code.
        /// </summary>
        public static void Remux(string input, string output, Action<double> progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = FullFilePath(input, ".mkv", "input");
            string destination = FullFilePath(output, ".mp4", "output");
            if (String.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a different destination; the original recording must be preserved.", "output");
            if (!File.Exists(source)) throw new FileNotFoundException("The original MKV recording was not found.", source);
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new IOException("The destination already exists. Choose a new MP4 filename.");
            string directory = Path.GetDirectoryName(destination);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The destination folder no longer exists.");

            NativeApi api = GetNativeApi();
            string temporary = Path.Combine(directory, ".rilmo-remux-" + Guid.NewGuid().ToString("N") + ".mp4");
            bool ownTemporary = false;
            IntPtr job = IntPtr.Zero;
            IntPtr inputUtf8 = IntPtr.Zero, outputUtf8 = IntPtr.Zero;
            bool created = false, cancellationObserved = false;
            Exception callbackError = null;
            double lastProgress = -1;
            NativeProgress callback = delegate(IntPtr state, float percent)
            {
                if (callbackError != null) return false;
                if (cancellationToken.IsCancellationRequested) { cancellationObserved = true; return false; }
                try
                {
                    // OBS 32.2.2 uses an ANSI stat call for its size estimate. A UTF-8
                    // filename can therefore produce NaN/Infinity despite valid remuxing.
                    // Ignore invalid estimates and reserve 100% for the committed output.
                    if (!Single.IsNaN(percent) && !Single.IsInfinity(percent))
                    {
                        double value = Math.Max(0, Math.Min(99.9, percent));
                        if (value > lastProgress) { if (progress != null) progress(value); lastProgress = value; }
                    }
                }
                catch (Exception error) { callbackError = error; return false; }
                if (cancellationToken.IsCancellationRequested) { cancellationObserved = true; return false; }
                return true;
            };

            try
            {
                // Deny writes/deletion while reading, so an active recording or another
                // writer is rejected and the input cannot be altered by this operation.
                using (FileStream inputGuard = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (inputGuard.Length == 0) throw new InvalidDataException("The original recording is empty.");
                    cancellationToken.ThrowIfCancellationRequested();
                    // CreateNew claims only our unique temporary path. Sharing permits
                    // libobs to open it for writing, but denies deletion/path replacement.
                    using (FileStream reservation = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite))
                    {
                        ownTemporary = true;
                        inputUtf8 = AllocateUtf8(source);
                        outputUtf8 = AllocateUtf8(temporary);
                        created = api.Create(out job, inputUtf8, outputUtf8);
                        if (!created)
                        {
                            // In OBS 32.2.2, a failed create destroys its allocation but
                            // can leave a nonzero out pointer. It must not be freed again.
                            job = IntPtr.Zero;
                            cancellationToken.ThrowIfCancellationRequested();
                            throw new InvalidDataException("The recording could not be opened for MP4 export. The original MKV was preserved.");
                        }
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            bool succeeded = api.Process(job, callback, IntPtr.Zero);
                            // Native process can return true after callback cancellation.
                            // Our cancellation/error state is authoritative in that case.
                            if (callbackError != null) throw new InvalidOperationException("MP4 export progress handling failed.", callbackError);
                            if (cancellationObserved) throw new OperationCanceledException(cancellationToken);
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!succeeded) throw new InvalidDataException("MP4 export failed. This recording may contain a stream that MP4 cannot store, or the destination may be full. The original MKV was preserved.");
                        }
                        finally
                        {
                            api.Destroy(job); job = IntPtr.Zero; created = false;
                            GC.KeepAlive(callback);
                        }
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    using (FileStream completed = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        byte[] header = new byte[12];
                        if (completed.Length < 32 || completed.Read(header, 0, header.Length) != header.Length ||
                            header[4] != 'f' || header[5] != 't' || header[6] != 'y' || header[7] != 'p')
                            throw new InvalidDataException("MP4 export did not produce a recognized output file. The original MKV was preserved.");
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    // Same-directory move is the commit point. File.Move without overwrite
                    // also protects a destination created by another process during export.
                    File.Move(temporary, destination);
                    ownTemporary = false;
                }
            }
            finally
            {
                if (created && job != IntPtr.Zero) api.Destroy(job);
                if (inputUtf8 != IntPtr.Zero) Marshal.FreeHGlobal(inputUtf8);
                if (outputUtf8 != IntPtr.Zero) Marshal.FreeHGlobal(outputUtf8);
                GC.KeepAlive(callback);
                if (ownTemporary)
                {
                    try { File.Delete(temporary); }
                    catch (IOException) { /* No other path is deleted; a locked partial can be removed later. */ }
                    catch (UnauthorizedAccessException) { /* Preserve the primary failure/cancellation. */ }
                }
            }
            // Output is already committed. A UI callback failure cannot turn a completed
            // export into a false failure, and cancellation after commit cannot delete it.
            if (progress != null) { try { progress(100); } catch (Exception) { } }
        }

        private static string FullFilePath(string path, string extension, string parameter)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose a file path.", parameter);
            string full = Path.GetFullPath(path);
            if (!String.Equals(Path.GetExtension(full), extension, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a " + extension + " file.", parameter);
            // Only ordinary filesystem paths, never alternate data streams or FFmpeg URLs.
            if (full.IndexOf(':', 2) >= 0 || full.IndexOf('\0') >= 0)
                throw new ArgumentException("Choose an ordinary file path.", parameter);
            return full;
        }

        private static IntPtr AllocateUtf8(string value)
        {
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(value + "\0");
            IntPtr memory = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, memory, bytes.Length);
            return memory;
        }

        private static NativeApi GetNativeApi()
        {
            if (IntPtr.Size != 8) throw new PlatformNotSupportedException("MP4 export requires the 64-bit runtime.");
            string library = Path.GetFullPath(Path.Combine(Engine.RuntimeLocator.ResolveBinDirectory(), "obs.dll"));
            lock (LibraryGate)
            {
                if (native != null)
                {
                    if (!String.Equals(native.Path, library, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The recording runtime changed. Restart Rilmo before exporting.");
                    return native;
                }
                if (!File.Exists(library)) throw new FileNotFoundException("The bundled MP4 export runtime is missing. Reinstall Rilmo Recorder.", library);
                // Dependencies resolve beside the exact selected OBS DLL. Do not modify
                // the current directory, PATH, or initialize a capture/audio/GPU session.
                IntPtr module = LoadLibraryEx(library, IntPtr.Zero, 0x00001100);
                if (module == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "The MP4 export runtime could not be loaded.");
                try
                {
                    NativeApi loaded = new NativeApi(module, library);
                    string version = Marshal.PtrToStringAnsi(loaded.Version());
                    if (version == null || !version.StartsWith("32.2.", StringComparison.Ordinal))
                        throw new NotSupportedException("This build supports the OBS 32.2.x remux API; found " + version + ".");
                    native = loaded; // Retain the module for process lifetime, like the capture loader.
                }
                catch { FreeLibrary(module); throw; }
                return native;
            }
        }

        private sealed class NativeApi
        {
            public readonly string Path;
            public readonly CreateJob Create;
            public readonly ProcessJob Process;
            public readonly DestroyJob Destroy;
            public readonly GetVersion Version;
            public NativeApi(IntPtr module, string path)
            {
                Path = path;
                Create = Export<CreateJob>(module, "media_remux_job_create");
                Process = Export<ProcessJob>(module, "media_remux_job_process");
                Destroy = Export<DestroyJob>(module, "media_remux_job_destroy");
                Version = Export<GetVersion>(module, "obs_get_version_string");
            }
            private static T Export<T>(IntPtr module, string name) where T : class
            {
                IntPtr address = GetProcAddress(module, name);
                if (address == IntPtr.Zero) throw new EntryPointNotFoundException("The recording runtime does not export " + name + ".");
                return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool NativeProgress(IntPtr state, float percent);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool CreateJob(out IntPtr job, IntPtr inputUtf8, IntPtr outputUtf8);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool ProcessJob(IntPtr job, NativeProgress progress, IntPtr state);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DestroyJob(IntPtr job);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetVersion();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr module);
    }
}
