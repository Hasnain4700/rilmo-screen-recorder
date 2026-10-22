using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Rilmo
{
    public partial class App : System.Windows.Application
    {
        private Mutex instanceLock;
#if RILMO_TEST
        internal static Exception UnhandledTestException;
#endif
        protected override void OnStartup(StartupEventArgs e)
        {
            if(Rilmo.Engine.RecorderHost.IsHostRequest(e.Args)){int code=Rilmo.Engine.RecorderHost.Run(e.Args);Shutdown(code);return;}
            base.OnStartup(e);
            bool render = e.Args.Length > 0 && e.Args[0] == "--render-ui";
            bool automatedTest = false;
#if RILMO_TEST
            automatedTest = e.Args.Length > 1 && e.Args[0] == "--smoke-ui";
            automatedTest |= e.Args.Length > 1 && e.Args[0] == "--benchmark-ui";
#endif
            if (!render && !automatedTest)
            {
                bool created;
                instanceLock = new Mutex(true, "Local\\RilmoRecorder.Desktop.v2", out created);
                if (!created) { MessageBox.Show("Rilmo Recorder is already running. Open it from the system tray.", "Rilmo Recorder"); Shutdown(); return; }
            }
            DispatcherUnhandledException += (s, args) =>
            {
                WriteError(args.Exception);
#if RILMO_TEST
                if (automatedTest) { UnhandledTestException=args.Exception; args.Handled=true; return; }
#endif
                MessageBox.Show(args.Exception.Message, "Rilmo Recorder", MessageBoxButton.OK, MessageBoxImage.Error); args.Handled = true;
            };
            var window = new MainWindow(render, (render || automatedTest) && e.Args.Length > 1 ? e.Args[1] : null, automatedTest);
#if RILMO_TEST
            if(e.Args.Length>1&&e.Args[0]=="--benchmark-ui")window.BenchmarkRequest=e.Args[1];
#endif
            if(render&&e.Args.Length>=5)window.RenderPage=e.Args[4];
            if (render && e.Args.Length >= 4 && int.TryParse(e.Args[2], out int renderWidth) && int.TryParse(e.Args[3], out int renderHeight))
            {
                window.Width = Math.Clamp(renderWidth, window.MinWidth, SystemParameters.WorkArea.Width);
                window.Height = Math.Clamp(renderHeight, window.MinHeight, SystemParameters.WorkArea.Height);
            }
            MainWindow = window;
            window.Show();
        }
        internal static void WriteError(Exception exception)
        {
            try { Directory.CreateDirectory(Settings.DataDirectory); File.AppendAllText(Path.Combine(Settings.DataDirectory, "app-errors.log"), DateTimeOffset.Now + " " + exception + Environment.NewLine); } catch { }
        }
        protected override void OnExit(ExitEventArgs e) { if (instanceLock != null) instanceLock.Dispose(); base.OnExit(e); }
    }
}
