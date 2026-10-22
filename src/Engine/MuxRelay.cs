// Development runtime bridge. libobs looks for helper executables beside its host.
// This is our own executable; it forwards to the user's installed OBS helpers.
// It never copies, patches or launches obs64.exe. Distribution should build/package
// the GPL runtime and helpers together, replacing this development bridge.
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Rilmo.Engine
{
    internal static class MuxRelay
    {
        public static int Main(string[] args)
        {
            try
            {
                string name = Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);
                if (name != "obs-ffmpeg-mux.exe" && name != "obs-amf-test.exe" && name != "obs-qsv-test.exe" && name != "obs-nvenc-test.exe")
                    throw new InvalidOperationException("Unrecognized runtime helper.");
                string runtime = Environment.GetEnvironmentVariable("RILMO_OBS_ROOT");
                if (String.IsNullOrWhiteSpace(runtime)) runtime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "obs-studio");
                string exe = Path.Combine(runtime, "bin", "64bit", name);
                if (!File.Exists(exe)) throw new FileNotFoundException("OBS runtime helper is missing.", exe);
                StringBuilder arguments = new StringBuilder();
                foreach (string arg in args) { if (arguments.Length > 0) arguments.Append(' '); arguments.Append(Quote(arg)); }
                bool mux = name == "obs-ffmpeg-mux.exe";
                ProcessStartInfo start = new ProcessStartInfo(exe, arguments.ToString());
                start.UseShellExecute = false; start.CreateNoWindow = true;
                start.WorkingDirectory = Path.GetDirectoryName(exe);
                start.RedirectStandardInput = mux; start.RedirectStandardOutput = true; start.RedirectStandardError = true;
                using (Process process = Process.Start(start))
                {
                    Thread output = new Thread(delegate() { Pump(process.StandardOutput.BaseStream, Console.OpenStandardOutput(), false); }); output.IsBackground = true; output.Start();
                    Thread error = new Thread(delegate() { Pump(process.StandardError.BaseStream, Console.OpenStandardError(), false); }); error.IsBackground = true; error.Start();
                    if (mux) Pump(Console.OpenStandardInput(), process.StandardInput.BaseStream, true);
                    process.WaitForExit(); output.Join(); error.Join();
                    return process.ExitCode;
                }
            }
            catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
        }
        private static void Pump(Stream input, Stream output, bool close)
        {
            try { input.CopyTo(output, 65536); output.Flush(); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            finally { if (close) output.Dispose(); }
        }
        // CommandLineToArgvW escaping, including trailing slashes and embedded quotes.
        private static string Quote(string value)
        {
            StringBuilder result = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { result.Append('\\', slashes * 2 + 1); result.Append(c); slashes = 0; continue; }
                result.Append('\\', slashes); slashes = 0; result.Append(c);
            }
            result.Append('\\', slashes * 2); result.Append('"'); return result.ToString();
        }
    }
}
