using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rilmo.Engine;
using Forms = System.Windows.Forms;

namespace Rilmo
{
    public partial class MainWindow : Window
    {
        private readonly Settings settings;
        private readonly EngineWorker worker = new EngineWorker();
        private RecorderClient recorder;
        private readonly Stopwatch recordingTime = new Stopwatch();
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        private CancellationTokenSource libraryCancellation;
        private CancellationTokenSource exportCancellation;
        private Forms.NotifyIcon tray;
        private System.Drawing.Icon idleIcon, activeIcon;
        private HwndSource hwndSource;
        private bool ready, busy, recording, paused, statsPending, closeAllowed, closeAfterStop, finalizing;
        private readonly bool renderOnly;
        private readonly bool automatedTest;
        private readonly string renderPath;
        internal string RenderPage {get;set;}
        private string selectedPage = "record";
        private string lastOutput;
        private int countdown;
        private bool cancelCountdown;
        private string captureMode = "display";
        private System.Drawing.Rectangle selectedArea;
        private string areaDisplay;
        private bool audioLoaded, loadingAudio;
        private bool exporting, closeAfterExport;
        private Forms.Screen[] monitors = Forms.Screen.AllScreens;
        private string[] monitorIdentities;
        private string previewSelectionKey;
        private long previewRevision;
        private sealed class DialogOwner : Forms.IWin32Window
        {
            public IntPtr Handle { get; private set; }
            public DialogOwner(IntPtr handle) { Handle = handle; }
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr handle, out NativeRect rectangle);
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

        public MainWindow(bool renderOnly = false, string renderPath = null, bool automatedTest = false)
        {
            this.renderOnly = renderOnly;
            this.automatedTest = automatedTest;
            this.renderPath = renderPath;
            settings = automatedTest ? new Settings() : Settings.Load();
            monitorIdentities = monitors.Select(GetDisplayIdentity).ToArray();
            InitializeComponent();
            Width = Math.Min(Width, SystemParameters.WorkArea.Width);
            Height = Math.Min(Height, SystemParameters.WorkArea.Height);
            MinWidth = Math.Min(760, Width);
            MinHeight = Math.Min(500, Height);
            for (int i = 0; i < monitors.Length; i++) MonitorPicker.Items.Add("Display " + (i + 1) + (monitors[i].Primary ? " · Main display" : " · Extended"));
            int storedMonitor = Array.FindIndex(monitorIdentities, identity => identity == settings.MonitorIdentity);
            if (storedMonitor < 0 && string.IsNullOrEmpty(settings.MonitorIdentity)) storedMonitor = Array.FindIndex(monitors, screen => screen.DeviceName == settings.MonitorDeviceName);
            bool hadStoredDisplay = !string.IsNullOrEmpty(settings.MonitorIdentity) || !string.IsNullOrEmpty(settings.MonitorDeviceName);
            MonitorPicker.SelectedIndex = storedMonitor >= 0 ? storedMonitor : hadStoredDisplay || monitors.Length == 0 ? -1 : Math.Clamp(settings.MonitorIndex, 0, monitors.Length - 1);
            QualityPicker.SelectedIndex = settings.QualityIndex;
            SystemAudioToggle.IsChecked = settings.SystemAudio;
            MicrophoneToggle.IsChecked = settings.Microphone;
            CursorToggle.IsChecked = settings.CaptureCursor;
            CountdownToggle.IsChecked = settings.Countdown;
            MinimizeToggle.IsChecked = settings.MinimizeOnRecord;
            EncoderPicker.SelectedIndex = settings.SoftwareEncoder ? 1 : 0;
            FolderPath.Text = settings.OutputDirectory;
            InitializeFeatures();
            InitializeHotkeyEditors();
            ready = true;
            UpdateOptions();
            UpdateCaptureMode();
            Navigate("record");
            timer.Tick += Timer_Tick;
            searchTimer.Tick += (s, e) => { searchTimer.Stop(); RefreshLibrary(); };
            Closing += Window_Closing;
            SourceInitialized += Window_SourceInitialized;
            ContentRendered += Window_ContentRendered;
            Closed += (s, e) => { timer.Stop(); searchTimer.Stop(); if (libraryCancellation != null) libraryCancellation.Cancel(); if (quickDock != null) quickDock.Close(); if (tray != null) tray.Dispose(); if (idleIcon != null) idleIcon.Dispose(); if (activeIcon != null) activeIcon.Dispose(); if (hwndSource != null) { for (int id=1;id<=5;id++) UnregisterHotKey(hwndSource.Handle,id); hwndSource.RemoveHook(WindowHook); } worker.Dispose(); };
        }
        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            if (renderOnly) return;
            hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            hwndSource.AddHook(WindowHook);
            RegisterConfiguredHotkeys();
            idleIcon = BrandAssets.CreateIcon();
            activeIcon = MakeIcon(System.Drawing.Color.FromArgb(255, 110, 125));
            Icon = Imaging.CreateBitmapSourceFromHIcon(idleIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            tray = new Forms.NotifyIcon { Icon = idleIcon, Visible = true, Text = "Rilmo Recorder · Ready" };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open Rilmo Recorder", null, (s, args) => Dispatcher.Invoke(ShowWindow));
            menu.Items.Add("Start / stop recording", null, (s, args) => Dispatcher.InvokeAsync(ToggleRecording));
            menu.Items.Add("Pause / resume", null, (s, args) => Dispatcher.InvokeAsync(TogglePause));
            menu.Items.Add("Screenshot", null, (s,args)=>Dispatcher.InvokeAsync(()=>QuickAction("screenshot")));
            menu.Items.Add("Quick controls", null, (s,args)=>Dispatcher.InvokeAsync(()=>ShowQuick_Click(this,new RoutedEventArgs())));
            menu.Items.Add("Exit", null, (s, args) => Dispatcher.Invoke(()=>{ exitRequested=true; Close(); }));
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, args) => Dispatcher.Invoke(ShowWindow);
        }
        private static System.Drawing.Icon MakeIcon(System.Drawing.Color color)
        {
            using (var bitmap = new System.Drawing.Bitmap(32, 32))
            {
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    graphics.Clear(System.Drawing.Color.Transparent);
                    using (var logo = BrandAssets.CreateIcon()) graphics.DrawIcon(logo, new System.Drawing.Rectangle(0, 0, 32, 32));
                    using (var border = new System.Drawing.SolidBrush(System.Drawing.Color.White)) graphics.FillEllipse(border, 20, 20, 12, 12);
                    using (var brush = new System.Drawing.SolidBrush(color)) graphics.FillEllipse(brush, 22, 22, 8, 8);
                }
                IntPtr handle = bitmap.GetHicon();
                try { return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(handle).Clone(); } finally { DestroyIcon(handle); }
            }
        }
        private async void Window_ContentRendered(object sender, EventArgs e)
        {
            ResourceLabel.Text = "Memory  " + (Process.GetCurrentProcess().WorkingSet64 / 1048576) + " MB";
#if RILMO_TEST
            if(BenchmarkRequest!=null){await RunMatchedBenchmark();return;}
            if (automatedTest) { await RunAutomatedSmoke(); return; }
#endif
            if (!renderOnly) { EnsureQuickDock(); return; }
            Window renderTarget=this;
            if(RenderPage=="scene")
            {
                sceneLibrary.SelectedSceneId=sceneLibrary.Scenes.Last().Id;
                EditScenes_Click(this,new RoutedEventArgs());renderTarget=sceneEditor;
            }
            else if(RenderPage=="camera"){Navigate("studio");UpdateLayout();VirtualCameraCard.BringIntoView();}
            else if(!string.IsNullOrEmpty(RenderPage))Navigate(RenderPage);
            Motion.Enabled=false;
            await Task.Delay(250);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if(RenderPage=="record")RecordPage.ScrollToTop();
            if(RenderPage=="settings")SettingsPage.ScrollToTop();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            string output = renderPath ?? Path.Combine(AppContext.BaseDirectory, "rilmo-preview.png");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            var bitmap = new RenderTargetBitmap((int)renderTarget.ActualWidth, (int)renderTarget.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(renderTarget);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(output)) encoder.Save(stream);
            if(renderTarget!=this)renderTarget.Close();
            closeAllowed = true;
            Close();
        }
        private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x0312) { handled = true; int id=wParam.ToInt32(); if (id == 1) ToggleRecording(); else if (id == 2) TogglePause(); else if(id==3) ShowQuick_Click(this,new RoutedEventArgs()); else if(id==4) QuickAction("screenshot"); else if(id==5) QuickAction("replay"); }
            if (message == 0x007E && quickDock != null) Dispatcher.BeginInvoke(()=>quickDock.KeepOnScreen(false));
            return IntPtr.Zero;
        }
        private void ShowWindow() { if (closeAllowed || screenshotBusy) return; Show(); WindowState = WindowState.Normal; Activate(); }
        private void Title_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton != MouseButton.Left) return; if (e.ClickCount == 2) Maximize_Click(sender, e); else DragMove(); }
        private void Minimize_Click(object sender, RoutedEventArgs e) { WindowState = WindowState.Minimized; }
        private void Maximize_Click(object sender, RoutedEventArgs e) { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }
        private async void Window_Closing(object sender, CancelEventArgs e)
        {
            if (closeAllowed || renderOnly) return;
            if (screenshotBusy) { e.Cancel=true; StatusLabel.Text="Finish or cancel the screenshot before exiting."; return; }
            if (settings.FloatingControls && !exitRequested && !automatedTest) { e.Cancel=true; Hide(); EnsureQuickDock(); return; }
            if (exporting) { e.Cancel = true; closeAfterExport = true; exportCancellation.Cancel(); StatusLabel.Text = "Cancelling export safely before closing…"; return; }
            if (busy) { cancelCountdown = true; e.Cancel = true; StatusLabel.Text = "Please wait while the recording operation finishes."; return; }
            if (HasActiveSession)
            {
                e.Cancel = true;
                bool stopAndClose;
                busy = true;
                try { stopAndClose = MessageBox.Show(this, "Stop recording, streaming, virtual camera and replay capture before exiting? Unsaved replay history will be discarded.", "Rilmo Recorder", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes; }
                finally { busy = false; }
                if (stopAndClose)
                {
                    busy=true;
                    try { await worker.Run(()=>recorder.Stop()); recording=streaming=replaying=virtualCamera=false; closeAllowed=true; Close(); }
                    catch(Exception ex) { StatusLabel.Text="Could not finish capture: " + SafeStreamMessage(ex.Message); ShowWindow(); }
                    finally { busy=false; UpdateQuickState(); }
                }
                return;
            }
            e.Cancel = true;
            busy = true;
            try { if (recorder != null) await worker.Run(() => { recorder.Dispose(); recorder = null; }); if (!automatedTest) settings.Save(); }
            catch (Exception ex) { App.WriteError(ex); }
            closeAllowed = true; Close();
        }
        private void Navigate_Click(object sender, RoutedEventArgs e) { Navigate((string)((Button)sender).Tag); }
        private void Preferences_Click(object sender, RoutedEventArgs e) { Navigate("settings"); }
        private void GoRecord_Click(object sender, RoutedEventArgs e) { Navigate("record"); }
        private void Navigate(string page)
        {
            selectedPage = page;
            RecordPage.Visibility = page == "record" ? Visibility.Visible : Visibility.Collapsed;
            LibraryPage.Visibility = page == "library" ? Visibility.Visible : Visibility.Collapsed;
            SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
            LivePage.Visibility = page == "live" ? Visibility.Visible : Visibility.Collapsed;
            StudioPage.Visibility = page == "studio" ? Visibility.Visible : Visibility.Collapsed;
            foreach (var button in new[] { NavRecord, NavLive, NavStudio, NavLibrary, NavSettings }) { bool current = (string)button.Tag == page; button.Background = current ? (Brush)FindResource("RaisedBrush") : Brushes.Transparent; button.Foreground = (Brush)FindResource(current ? "AccentTextBrush" : "MutedBrush"); }
            if (page == "library") RefreshLibrary();
            if (page == "settings" && !audioLoaded) LoadAudioDevices();
            if (page == "studio") {LoadCameraDevices();RefreshVirtualCamera();}
            Motion.Reveal(page=="record"?RecordPage:page=="live"?LivePage:page=="studio"?StudioPage:page=="library"?LibraryPage:SettingsPage,HasActiveSession);
        }
        private void Options_Changed(object sender, RoutedEventArgs e) { if (ready) UpdateOptions(); }
        private void UpdateOptions()
        {
            SaveFeatureSettings();
            RefreshPreviewSelection();
            // Preserve a missing selection until the user explicitly chooses a display.
            // An audio/quality change must never silently select index zero instead.
            int selectedMonitor = MonitorPicker.SelectedIndex;
            if (selectedMonitor >= 0 && selectedMonitor < monitors.Length)
            {
                settings.MonitorIndex = selectedMonitor;
                settings.MonitorDeviceName = monitors[selectedMonitor].DeviceName;
                settings.MonitorIdentity = monitorIdentities[selectedMonitor];
            }
            settings.QualityIndex = Math.Max(0, QualityPicker.SelectedIndex);
            settings.SystemAudio = SystemAudioToggle.IsChecked == true;
            settings.Microphone = MicrophoneToggle.IsChecked == true;
            settings.CaptureCursor = CursorToggle.IsChecked == true;
            settings.Countdown = CountdownToggle.IsChecked == true;
            settings.MinimizeOnRecord = MinimizeToggle.IsChecked == true;
            settings.SoftwareEncoder = EncoderPicker.SelectedIndex == 1;
            settings.UseSceneCollection=captureMode=="scene";
            if (selectedMonitor >= 0 && selectedMonitor < monitors.Length)
            {
                var screen = monitors[Math.Min(settings.MonitorIndex, monitors.Length - 1)];
                SourceDetail.Text = screen.Bounds.Width + " × " + screen.Bounds.Height + " · entire display";
                if (captureMode == "window") { var selectedWindow = WindowPicker.SelectedItem as CaptureWindow; SourceDetail.Text = selectedWindow == null ? "Choose an open window" : selectedWindow.AppName + " · selected window"; }
                else if (captureMode == "area") SourceDetail.Text = selectedArea.Width > 0 ? selectedArea.Width + " × " + selectedArea.Height + " · selected area" : "Select an area on this display";
            }
            else SourceDetail.Text = monitors.Length == 0 ? "No active display available" : "Choose a connected display";
            if(captureMode=="scene"&&sceneLibrary!=null)SourceDetail.Text=(sceneLibrary.Scenes.FirstOrDefault(s=>s.Id==sceneLibrary.SelectedSceneId)?.Name??"Scene")+" · composed scene";
            string[] hints = { "A comfortable starting point for everyday PCs.", "Clearer text and detail for tutorials.", "Smoother motion. More GPU and CPU work.", "Preserves your display resolution. Larger files." };
            QualityHint.Text = hints[settings.QualityIndex];
            var options = BuildOptions();
            if (!HasActiveSession) RecordingDetail.Text = options.Width + " × " + options.Height + " · " + options.Fps + " fps · " + (settings.SoftwareEncoder ? "software encoder" : "hardware preferred");
            if (!renderOnly && !automatedTest) { try { settings.Save(); } catch (Exception ex) { StatusLabel.Text = "Preferences could not be saved: " + ex.Message; } }
        }
        private RecorderOptions BuildOptions()
        {
            if (monitors.Length == 0) return new RecorderOptions { Width = 1280, Height = 720, Fps = 30, OutputDirectory = settings.OutputDirectory };
            var screen = monitors[Math.Clamp(settings.MonitorIndex, 0, monitors.Length - 1)].Bounds;
            int sourceWidth = screen.Width, sourceHeight = screen.Height;
            if(captureMode=="scene"&&sceneLibrary!=null){sourceWidth=sceneLibrary.CanvasWidth;sourceHeight=sceneLibrary.CanvasHeight;}
            var selectedWindow = WindowPicker.SelectedItem as CaptureWindow;
            if (captureMode == "area" && selectedArea.Width > 0) { sourceWidth = selectedArea.Width; sourceHeight = selectedArea.Height; }
            else if (captureMode == "window" && selectedWindow != null)
            {
                NativeRect bounds;
                if (GetClientRect(selectedWindow.Handle, out bounds) && bounds.Right > 0 && bounds.Bottom > 0) { sourceWidth = bounds.Right; sourceHeight = bounds.Bottom; }
            }
            int maxHeight = settings.QualityIndex == 0 ? 720 : settings.QualityIndex == 3 ? sourceHeight : 1080;
            double factor = Math.Min(1.0, (double)maxHeight / sourceHeight);
            int width = Math.Max(320, (int)(sourceWidth * factor) / 4 * 4), height = Math.Max(180, (int)(sourceHeight * factor) / 2 * 2);
            var options = new RecorderOptions { MonitorIndex = settings.MonitorIndex, Width = width, Height = height, Fps = settings.QualityIndex == 2 ? 60 : 30, SystemAudio = settings.SystemAudio, Microphone = settings.Microphone, CaptureCursor = settings.CaptureCursor, OutputDirectory = settings.OutputDirectory, EncoderPreference = settings.SoftwareEncoder ? "software" : "auto" };
            if(recorder!=null&&recorder.IsSessionActive)
            {
                if(sessionWidth<=0||sessionHeight<=0||sessionFps<=0)throw new InvalidOperationException("The active capture dimensions are unavailable. Finish capture before starting another output.");
                options.Width=sessionWidth;options.Height=sessionHeight;options.Fps=sessionFps;
            }
            options.SeparateAudioTracks=settings.SeparateAudioTracks;
            options.MicrophoneDeviceId = settings.MicrophoneDeviceId ?? "default";
            options.SystemAudioDeviceId = settings.SystemAudioDeviceId ?? "default";
            if (captureMode == "window" && selectedWindow != null) options.WindowHandle = selectedWindow.Handle;
            if (captureMode == "area") { options.CropX = selectedArea.X; options.CropY = selectedArea.Y; options.CropWidth = selectedArea.Width; options.CropHeight = selectedArea.Height; }
            return options;
        }
        private void CaptureMode_Click(object sender, RoutedEventArgs e) { if (busy || HasActiveSession) return; captureMode = (string)((Button)sender).Tag; UpdateCaptureMode(); UpdateOptions(); }
        private void UpdateCaptureMode()
        {
            CaptureModeLabel.Text = char.ToUpperInvariant(captureMode[0])+captureMode.Substring(1);
            MonitorPicker.Visibility = captureMode == "window" || captureMode=="scene" ? Visibility.Collapsed : Visibility.Visible;
            RecordSceneControls.Visibility=captureMode=="scene"?Visibility.Visible:Visibility.Collapsed;
            WindowPicker.Visibility = captureMode == "window" ? Visibility.Visible : Visibility.Collapsed;
            AreaControls.Visibility = captureMode == "area" ? Visibility.Visible : Visibility.Collapsed;
            foreach (var button in new[] { DisplayModeButton, WindowModeButton, AreaModeButton, SceneModeButton }) { bool current = (string)button.Tag == captureMode; button.Background = (Brush)FindResource(current ? "AccentBrush" : "PanelBrush"); button.Foreground = (Brush)FindResource(current ? "BackgroundBrush" : "TextBrush"); }
            if (captureMode == "window") { WindowPicker.ItemsSource = RecorderEngine.GetCaptureWindows(); WindowPicker.SelectedIndex = 0; }
            PreviewButton.IsEnabled = captureMode != "window";
            PreviewButton.ToolTip = captureMode == "window" ? "Still preview is available for display and area capture." : "Capture one still preview";
            InvalidatePreview();
        }
        private void WindowSelection_Changed(object sender, SelectionChangedEventArgs e) { if (ready) { UpdateOptions(); var window = WindowPicker.SelectedItem as CaptureWindow; SourceDetail.Text = window == null ? "No capturable window selected" : window.AppName + " · selected window"; } }
        private async void SelectArea_Click(object sender, RoutedEventArgs e)
        {
            if (HasActiveSession || busy || exporting || finalizing || MonitorPicker.SelectedIndex < 0 || MonitorPicker.SelectedIndex >= monitors.Length) return;
            int monitorIndex = MonitorPicker.SelectedIndex;
            var screen = monitors[monitorIndex];
            string displayIdentity = monitorIdentities[monitorIndex];
            // Reserve the operation before the delay and the dialog's nested message loop.
            // Global hotkeys and tray commands must not start a take with the old area.
            busy = true; RecordButton.IsEnabled = false; SetControlsEnabled(false);
            try
            {
                WindowState = WindowState.Minimized;
                await Task.Delay(200);
                var picker = new RegionPicker(screen.Bounds);
                if (picker.ShowDialog() == true)
                {
                    selectedArea = picker.Selection; areaDisplay = displayIdentity;
                    AreaLabel.Text = selectedArea.Width + " × " + selectedArea.Height + " px";
                    InvalidatePreview(); UpdateOptions();
                }
            }
            catch (Exception ex) { App.WriteError(ex); StatusLabel.Text = "Couldn't select an area: " + ex.Message; }
            finally { busy = false; RecordButton.IsEnabled = true; SetControlsEnabled(!recording && !finalizing && !exporting); ShowWindow(); }
        }
        private string GetPreviewSelectionKey()
        {
            int index = MonitorPicker.SelectedIndex;
            string display = index >= 0 && index < monitorIdentities.Length ? monitorIdentities[index] : "";
            string bounds = index >= 0 && index < monitors.Length ? monitors[index].Bounds.ToString() : "";
            var window = WindowPicker.SelectedItem as CaptureWindow;
            return string.Join("|", captureMode, display, bounds, window == null ? "" : window.Handle.ToInt64().ToString(), selectedArea.ToString(), areaDisplay ?? "");
        }
        private void RefreshPreviewSelection()
        {
            if (!string.Equals(previewSelectionKey, GetPreviewSelectionKey(), StringComparison.Ordinal)) InvalidatePreview();
        }
        private void InvalidatePreview()
        {
            previewSelectionKey = GetPreviewSelectionKey(); previewRevision++;
            PreviewImage.Source = null; PreviewImage.Visibility = Visibility.Collapsed; PreviewPlaceholder.Visibility = Visibility.Visible;
            ((TextBlock)CaptureBadge.Child).Text = "Preview off · saving resources";
        }
        private void SetControlsEnabled(bool enabled) { CaptureControls.IsEnabled = enabled; SettingsControls.IsEnabled = enabled; StudioControls.IsEnabled=enabled; LiveControls.IsEnabled=!streaming && !busy; LiveButton.IsEnabled=!busy && !exporting; ReplayButton.IsEnabled=!busy && !exporting; SaveReplayButton.IsEnabled=replaying && !busy; RefreshVirtualCamera(); }
        private static string GetDisplayIdentity(Forms.Screen screen)
        {
            var device = new NativeObs.DisplayDevice(); device.Size = (uint)Marshal.SizeOf(typeof(NativeObs.DisplayDevice));
            return NativeObs.EnumDisplayDevices(screen.DeviceName, 0, ref device, 1) && !string.IsNullOrEmpty(device.DeviceId) ? device.DeviceId : screen.DeviceName;
        }
        private void SyncDisplays(Forms.Screen[] screens, string[] identities, int selected)
        {
            bool wasReady = ready; ready = false; monitors = screens; monitorIdentities = identities;
            MonitorPicker.Items.Clear();
            for (int i = 0; i < monitors.Length; i++) MonitorPicker.Items.Add("Display " + (i + 1) + (monitors[i].Primary ? " · Main display" : " · Extended"));
            MonitorPicker.SelectedIndex = selected; ready = wasReady;
            RefreshPreviewSelection();
        }
        private void Record_Click(object sender, RoutedEventArgs e) { ToggleRecording(); }
        private async void ToggleRecording()
        {
            if (exporting) { exportCancellation.Cancel(); RecordLabel.Text = "Cancelling…"; RecordButton.IsEnabled = false; return; }
            if (countdown > 0) { cancelCountdown = true; return; }
            if (busy || screenshotBusy) return;
            if (cleanupPending) await RetryCaptureCleanup(); else if (recording) await StopRecording(); else await StartRecording();
        }
        private async Task StartRecording()
        {
            busy = true; captureRevision++; cancelCountdown = false; SetControlsEnabled(false); UpdateQuickState(); Navigate("record");
            try
            {
                if (cleanupPending) throw new InvalidOperationException("Press Finish capture before starting a new recording.");
                // Resolve current display inventory at every start, since docking can change it.
                var currentMonitors = Forms.Screen.AllScreens;
                if (currentMonitors.Length == 0) throw new InvalidOperationException("No active display is available. Connect a display or restore your desktop session.");
                var currentIdentities = currentMonitors.Select(GetDisplayIdentity).ToArray();
                int selectedMonitor = Array.FindIndex(currentIdentities, identity => identity == settings.MonitorIdentity);
                if (selectedMonitor < 0 && captureMode != "window" && captureMode != "scene")
                {
                    SyncDisplays(currentMonitors, currentIdentities, -1);
                    throw new InvalidOperationException("The selected display was disconnected. Choose a connected display before recording.");
                }
                if (MonitorPicker.SelectedIndex < 0 && captureMode != "window" && captureMode != "scene") throw new InvalidOperationException("Choose a display before recording.");
                if (captureMode == "window" && WindowPicker.SelectedItem == null) throw new InvalidOperationException("Choose an open window before recording.");
                if (captureMode == "area" && (selectedArea.Width == 0 || areaDisplay != settings.MonitorIdentity)) throw new InvalidOperationException("Select an area on this display before recording.");
                // Preserve selected display identity if Windows reorders the display inventory.
                SyncDisplays(currentMonitors, currentIdentities, Math.Max(0, selectedMonitor)); settings.MonitorIndex = Math.Max(0, selectedMonitor);
                Directory.CreateDirectory(settings.OutputDirectory);
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(settings.OutputDirectory)));
                if (drive.IsReady && drive.AvailableFreeSpace < 512L * 1024 * 1024) throw new IOException("Free up at least 512 MB in the recording folder before starting.");
                var options = BuildOptions();
                AddComposition(options);
                if (settings.Countdown && !HasActiveSession)
                {
                    RecordLabel.Text = "Cancel";
                    for (countdown = 3; countdown > 0; countdown--)
                    {
                        StateLabel.Text = "Starting in " + countdown + "…";
                        await Task.Delay(1000);
                        if (cancelCountdown) { StateLabel.Text = "Ready to record"; StatusLabel.Text = "Countdown cancelled."; return; }
                    }
                }
                // Check again after countdown: docking or unplugging must never silently
                // replace the user's selected display during those three seconds.
                var finalMonitors = Forms.Screen.AllScreens;
                var finalIdentities = finalMonitors.Select(GetDisplayIdentity).ToArray();
                int finalMonitor = Array.FindIndex(finalIdentities, identity => identity == settings.MonitorIdentity);
                if (captureMode != "window" && captureMode != "scene" && finalMonitor < 0) throw new InvalidOperationException("The selected display disconnected during setup. Choose it again before recording.");
                if (finalMonitors.Length == 0) throw new InvalidOperationException("No active display is available.");
                SyncDisplays(finalMonitors, finalIdentities, Math.Max(0, finalMonitor)); settings.MonitorIndex = Math.Max(0, finalMonitor);
                options.MonitorIndex = settings.MonitorIndex;
                if(options.SceneCollection!=null)options.SceneCollection=ResolveScenes(sceneLibrary,options.Width,options.Height);
                RecordButton.IsEnabled = false; StateLabel.Text = "Preparing your recording…";
                StatusLabel.Text = "Preparing capture and waiting for its first video frames…";
                var layout=CaptureLayout(options);
                await worker.Run(() => { StartNativeSession(options,layout);recorder.StartRecording(); });
                recording = true; paused = false; finalizing = false;
                recordingTime.Restart(); lastOutput = recorder.OutputPath;
                StateLabel.Text = "Recording"; StateDot.Fill = (Brush)FindResource("AccentBrush");
                RecordLabel.Text = "Stop & save"; RecordGlyph.Text = "■";
                RecordButton.Background = (Brush)FindResource("AccentBrush");
                PauseButton.Visibility = Visibility.Visible; PauseButton.Content = "Pause";
                RecordingDetail.Text = recorder.EncoderName + " · " + options.Width + " × " + options.Height + " · " + options.Fps + " fps";
                StatusLabel.Text = "Recording locally. Stop: " + settings.Hotkeys[0] + " · Pause: " + settings.Hotkeys[1];
                if (tray != null) { tray.Icon = activeIcon; tray.Text = "Rilmo Recorder · RECORDING"; }
                InvalidatePreview();
                if(settings.ReplayWithRecording && !replaying)
                {
                    try { await worker.Run(()=>recorder.StartReplayBuffer(new ReplayOptions { Seconds=settings.ReplaySeconds,MaximumMegabytes=128,OutputDirectory=settings.OutputDirectory })); replaying=true; }
                    catch(Exception ex) { StatusLabel.Text="Recording started; replay could not start: " + ex.Message; }
                }
                timer.Start();
                if (settings.MinimizeOnRecord) { if(settings.FloatingControls && !automatedTest) { Hide(); EnsureQuickDock(); } else WindowState = WindowState.Minimized; }
            }
            catch (Exception ex)
            {
                App.WriteError(ex); ShowWindow(); StateLabel.Text = "Couldn't start recording"; StatusLabel.Text = ex.Message;
                if(!HasActiveSession) await ReleaseUnusedSession();
                if (automatedTest) throw;
                MessageBox.Show(this, ex.Message, "Recording couldn't start", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                countdown = 0; busy = false; RecordButton.IsEnabled = true;
                if (!recording) { SetControlsEnabled(!HasActiveSession); RecordLabel.Text = cleanupPending ? "Finish capture" : "Start recording"; }
                PauseButton.IsEnabled=!streaming && !replaying && !virtualCamera; UpdateQuickState();
            }
        }
        private async Task StopRecording()
        {
            if (!recording || busy) return;
            busy = true; captureRevision++; timer.Stop(); recordingTime.Stop(); RecordButton.IsEnabled = false; PauseButton.IsEnabled = false; UpdateQuickState();
            StateLabel.Text = "Saving your recording…"; StatusLabel.Text = "Finalizing the recording file. Please keep Rilmo open.";
            bool saved = false, stillActive = false;
            try
            {
                await worker.Run(() => recorder.StopRecording());
                saved = !string.IsNullOrEmpty(lastOutput) && File.Exists(lastOutput) && new FileInfo(lastOutput).Length > 0;
                if (!saved) throw new IOException("The recorder stopped but no completed file was found. Check the engine log.");
                StateLabel.Text = "Recording saved"; StatusLabel.Text = "Saved: " + Path.GetFileName(lastOutput);
                if(!quickOperation) ShowWindow();
                if (selectedPage == "library") RefreshLibrary();
            }
            catch (Exception ex)
            {
                App.WriteError(ex); ShowWindow();
                stillActive = ex is TimeoutException || await worker.Run(() => recorder.IsRecording);
                StateLabel.Text = stillActive ? "Still finalizing · retry Stop" : "Recording needs attention";
                StatusLabel.Text = ex.Message;
                if (automatedTest) throw;
                MessageBox.Show(this, (stillActive ? "Keep Rilmo open and press Stop again.\n\n" : "Couldn't confirm finalization. A partial file may still be recoverable in your recordings folder.\n\n") + ex.Message, "Recording needs attention", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                recording = stillActive; finalizing = stillActive; paused = false; RecordButton.IsEnabled = true; PauseButton.IsEnabled = true; PauseButton.Visibility = Visibility.Collapsed;
                RecordButton.Background = (Brush)FindResource("AccentBrush"); RecordLabel.Text = stillActive ? "Retry stop" : "Record again"; RecordGlyph.Text = stillActive ? "■" : "●"; StateDot.Fill = (Brush)FindResource("AccentBrush");
                if(!stillActive && !streaming && !replaying && !virtualCamera) await ReleaseUnusedSession();
                busy = false;
                SetControlsEnabled(!HasActiveSession);
                if (tray != null) { tray.Icon = stillActive ? activeIcon : idleIcon; tray.Text = stillActive ? "Rilmo Recorder · FINALIZING" : "Rilmo Recorder · Ready"; }
                if (closeAfterStop && saved) { closeAfterStop = false; Close(); } else closeAfterStop = false;
                if(streaming || replaying || virtualCamera) timer.Start(); UpdateSessionIndicatorsAfterRecording();
            }
        }
        private void Pause_Click(object sender, RoutedEventArgs e) { TogglePause(); }
        private async void TogglePause() { await ChangePause(); }
        private async Task ChangePause()
        {
            if (busy || !recording || finalizing || streaming || replaying || virtualCamera) return;
            busy = true; PauseButton.IsEnabled = false; UpdateQuickState();
            try
            {
                bool next = !paused;
                await worker.Run(() => recorder.SetPaused(next));
                paused = next;
                if (paused) recordingTime.Stop(); else recordingTime.Start();
                PauseButton.Content = paused ? "Resume" : "Pause"; StateLabel.Text = paused ? "Paused" : "Recording";
                if (tray != null) tray.Text = paused ? "Rilmo Recorder · PAUSED" : "Rilmo Recorder · RECORDING";
            }
            catch (Exception ex) { StatusLabel.Text = ex.Message; App.WriteError(ex); if (automatedTest) throw; }
            finally { busy = false; PauseButton.IsEnabled = !streaming && !replaying && !virtualCamera; UpdateQuickState(); }
        }
        private async void Timer_Tick(object sender, EventArgs e)
        {
            bool visibleUi=IsVisible&&WindowState!=WindowState.Minimized;
            string elapsed=recordingTime.Elapsed.ToString(recordingTime.Elapsed.TotalHours>=1?@"hh\:mm\:ss":@"mm\:ss");
            if(visibleUi)ElapsedLabel.Text=elapsed;
            if(quickDock?.IsVisible==true)quickDock.UpdateState(recording,paused,streaming,replaying,busy||screenshotBusy||exporting,elapsed,cleanupPending,virtualCamera);
            if (busy || statsPending || !HasActiveSession || cleanupPending) return;
            long revision=captureRevision;
            bool includeLevels=visibleUi&&selectedPage=="studio";
            statsPending = true;
            try
            {
                var snapshot=await worker.Run(()=> (Stats:recorder.GetStats(), Status:recorder.GetOutputStatus(), Camera:recorder.IsVirtualCameraActive, Levels:includeLevels?recorder.GetAudioLevels():null));
                var stats=snapshot.Stats; var status=snapshot.Status;
                if (busy || revision!=captureRevision) return;
                bool wasStreaming=streaming, wasReplaying=replaying, wasCamera=virtualCamera; streaming=status.Streaming; replaying=status.ReplayBufferActive; virtualCamera=snapshot.Camera;
                if ((wasStreaming && !streaming) || (wasReplaying && !replaying) || (wasCamera && !virtualCamera))
                {
                    busy=true;
                    try { await ReleaseUnusedSession(); } finally { busy=false; }
                    UpdateSessionIndicators();
                    StatusLabel.Text=wasStreaming && !streaming ? "Stream ended: " + SafeStreamMessage(status.LastStreamError ?? "Connection closed.") : wasCamera && !virtualCamera ? "Virtual camera ended. Start it again when ready." : "Replay capture ended. Start it again to capture new history.";
                    ShowWindow();
                    return;
                }
                if(selectedPage=="studio" && snapshot.Levels!=null)
                {
                    var levels=snapshot.Levels;
                    SystemMeter.Value=Math.Clamp((levels.SystemPeakDb+60)/60.0,0,1);
                    MicrophoneMeter.Value=Math.Clamp((levels.MicrophonePeakDb+60)/60.0,0,1);
                }
                if(!visibleUi)return;
                using(var metrics=Process.GetCurrentProcess())ResourceLabel.Text = "Memory  " + (metrics.WorkingSet64 / 1048576) + " MB  ·  " + FormatSize(stats.OutputBytes);
                if (stats.EncoderSkippedFrames > 0) StatusLabel.Text = "Encoder skipped " + stats.EncoderSkippedFrames + " frames. Try the Light preset for the next take.";
                else if (stats.DroppedFrames > 0) StatusLabel.Text = "Capture/output timing delays detected. Try the Light preset for the next take.";
            }
            catch (Exception ex)
            {
                var activeOutputs = await worker.Run(() => (File: recorder.IsRecording, Live: recorder.IsStreaming, Replay: recorder.IsReplayBufferActive, Camera:recorder.IsVirtualCameraActive));
                if (busy || revision!=captureRevision) return;
                streaming=activeOutputs.Live; replaying=activeOutputs.Replay; virtualCamera=activeOutputs.Camera;
                bool active = activeOutputs.File;
                if (!active && recording && !busy)
                {
                    timer.Stop(); recordingTime.Stop();
                    await StopRecording();
                    StateLabel.Text = "Recording interrupted";
                    StatusLabel.Text = SafeStreamMessage(ex.Message);
                    ShowWindow();
                }
                else
                {
                    busy=true;
                    try { await ReleaseUnusedSession(); } finally { busy=false; }
                    UpdateSessionIndicators();
                    StatusLabel.Text = (HasActiveSession ? "Capture statistics unavailable: " : "Capture interrupted: ") + SafeStreamMessage(ex.Message);
                    if (!HasActiveSession) ShowWindow();
                }
            }
            finally { statsPending = false; }
        }
        private async void Preview_Click(object sender, RoutedEventArgs e)
        {
            if(captureMode=="scene"){await ShowScenePreview();return;}
            if (HasActiveSession || busy || exporting || captureMode == "window" || monitors.Length == 0 || MonitorPicker.SelectedIndex < 0) return;
            if (captureMode == "area" && (selectedArea.Width == 0 || areaDisplay != monitorIdentities[MonitorPicker.SelectedIndex])) { StatusLabel.Text = "Select an area on this display before previewing."; return; }
            RefreshPreviewSelection();
            long requestRevision = previewRevision;
            string sourceIdentity = GetPreviewSelectionKey();
            busy = true; RecordButton.IsEnabled = false; PreviewButton.IsEnabled = false;
            try
            {
                var bounds = monitors[Math.Clamp(MonitorPicker.SelectedIndex, 0, monitors.Length - 1)].Bounds;
                if (captureMode == "area" && selectedArea.Width > 0) bounds = new System.Drawing.Rectangle(bounds.Left + selectedArea.X, bounds.Top + selectedArea.Y, selectedArea.Width, selectedArea.Height);
                var preview = await Task.Run(() =>
                {
                    using (var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height))
                    {
                        using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
                        using (var stream = new MemoryStream())
                        {
                            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png); stream.Position = 0;
                            var source = new BitmapImage(); source.BeginInit(); source.CacheOption = BitmapCacheOption.OnLoad; source.DecodePixelWidth = 800; source.StreamSource = stream; source.EndInit(); source.Freeze();
                            return source;
                        }
                    }
                });
                // A source can change while screen copying/PNG decoding runs. Its old
                // image must not be displayed under the newly selected source's label.
                if (requestRevision != previewRevision || !string.Equals(sourceIdentity, GetPreviewSelectionKey(), StringComparison.Ordinal) || recording || exporting || closeAllowed) return;
                PreviewImage.Source = preview;
                PreviewImage.Visibility = Visibility.Visible; PreviewPlaceholder.Visibility = Visibility.Collapsed;
                ((TextBlock)CaptureBadge.Child).Text = "Still preview · " + DateTime.Now.ToString("HH:mm:ss");
                StatusLabel.Text = "One still preview captured. Nothing is being recorded.";
            }
            catch (Exception ex) { if (requestRevision == previewRevision) StatusLabel.Text = "Preview unavailable: " + ex.Message; }
            finally { busy = false; RecordButton.IsEnabled = true; PreviewButton.IsEnabled = captureMode != "window"; }
        }
        private void ChooseFolder_Click(object sender, RoutedEventArgs e)
        {
            if (busy || HasActiveSession || exporting || finalizing) return;
            busy = true; RecordButton.IsEnabled = false; SetControlsEnabled(false);
            try
            {
                using (var picker = new Forms.FolderBrowserDialog { Description = "Choose where Rilmo saves your recordings", UseDescriptionForTitle = true, SelectedPath = settings.OutputDirectory })
                {
                    if (picker.ShowDialog(new DialogOwner(new WindowInteropHelper(this).Handle)) != Forms.DialogResult.OK) return;
                    settings.OutputDirectory = picker.SelectedPath; FolderPath.Text = picker.SelectedPath; UpdateOptions();
                }
            }
            catch (Exception ex) { App.WriteError(ex); StatusLabel.Text = "Couldn't choose a recording folder: " + ex.Message; }
            finally { busy = false; RecordButton.IsEnabled = true; SetControlsEnabled(!recording && !finalizing && !exporting); }
        }
        private void RefreshAudio_Click(object sender, RoutedEventArgs e) { LoadAudioDevices(); }
        private async void LoadAudioDevices()
        {
            if (loadingAudio) return;
            loadingAudio = true; InputDevicePicker.IsEnabled = false; OutputDevicePicker.IsEnabled = false;
            try
            {
                var devices = await Task.Run(() => new[] { AudioDeviceCatalog.GetInputs(), AudioDeviceCatalog.GetOutputs() });
                var inputs = devices[0].ToList(); var outputs = devices[1].ToList();
                string inputId = settings.MicrophoneDeviceId ?? "default", outputId = settings.SystemAudioDeviceId ?? "default";
                if (!inputs.Any(d => d.Id == inputId)) inputs.Add(new AudioDevice(inputId, "Unavailable microphone · choose another", false));
                if (!outputs.Any(d => d.Id == outputId)) outputs.Add(new AudioDevice(outputId, "Unavailable audio output · choose another", false));
                InputDevicePicker.ItemsSource = inputs; OutputDevicePicker.ItemsSource = outputs;
                InputDevicePicker.SelectedItem = inputs.First(d => d.Id == inputId); OutputDevicePicker.SelectedItem = outputs.First(d => d.Id == outputId);
                audioLoaded = true;
            }
            catch (Exception ex) { StatusLabel.Text = "Couldn't list audio devices: " + ex.Message; }
            finally { loadingAudio = false; InputDevicePicker.IsEnabled = true; OutputDevicePicker.IsEnabled = true; }
        }
        private void AudioSelection_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!ready || loadingAudio) return;
            if (InputDevicePicker.SelectedItem is AudioDevice input) settings.MicrophoneDeviceId = input.Id;
            if (OutputDevicePicker.SelectedItem is AudioDevice output) settings.SystemAudioDeviceId = output.Id;
            UpdateOptions();
        }
        private void OpenFolder_Click(object sender, RoutedEventArgs e) { try { Directory.CreateDirectory(settings.OutputDirectory); OpenPath(settings.OutputDirectory); } catch (Exception ex) { StatusLabel.Text = ex.Message; } }
        private static void OpenPath(string path) { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        private void ProjectNotes_Click(object sender, RoutedEventArgs e)
        {
            string folder = AppContext.BaseDirectory;
            for (int i = 0; i < 6; i++) { string path = Path.Combine(folder, "README.md"); if (File.Exists(path)) { OpenPath(path); return; } folder = Path.GetDirectoryName(folder.TrimEnd(Path.DirectorySeparatorChar)); if (folder == null) break; }
            MessageBox.Show(this, "Rilmo Recorder 0.2 preview\nOriginal interface with a bundled OBS recording engine.\nSee the included license and project notes for build and distribution details.", "About Rilmo Recorder");
        }
        private void LibrarySearch_Changed(object sender, TextChangedEventArgs e) { if (ready) { searchTimer.Stop(); searchTimer.Start(); } }
        private async void RefreshLibrary()
        {
            if (libraryCancellation != null) libraryCancellation.Cancel();
            libraryCancellation = new CancellationTokenSource();
            var cancellation = libraryCancellation.Token;
            if (!Directory.Exists(settings.OutputDirectory)) { LibraryEmpty.Visibility = Visibility.Visible; return; }
            try
            {
                string query = LibrarySearch.Text.Trim();
                string directory = settings.OutputDirectory;
                var files = await Task.Run(() => new DirectoryInfo(directory).EnumerateFiles().Where(f => { cancellation.ThrowIfCancellationRequested(); return (f.Extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase)) && f.Name.Contains(query, StringComparison.OrdinalIgnoreCase); }).OrderByDescending(f => f.LastWriteTime).Take(100).ToArray(), cancellation);
                if (cancellation.IsCancellationRequested) return;
                RecordingsList.Children.Clear();
                foreach (var file in files)
                {
                    var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,15,0) };
                    labels.Children.Add(new TextBlock { Text = file.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = file.Name });
                    labels.Children.Add(new TextBlock { Text = file.LastWriteTime.ToString("dd MMM yyyy · h:mm tt") + "   ·   " + FormatSize(file.Length), FontSize = 12, Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0,6,0,0) });
                    grid.Children.Add(labels);
                    var play = new Button { Content = file.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ? "View ↗" : "Play ↗", Padding = new Thickness(12,9,12,9), IsEnabled = !recording || file.FullName != lastOutput }; Grid.SetColumn(play, 1); play.Click += (s, e) => { try { OpenPath(file.FullName); } catch (Exception ex) { StatusLabel.Text = "Choose an app that opens this file type. " + ex.Message; } }; grid.Children.Add(play);
                    var reveal = new Button { Content = "Show", Padding = new Thickness(12,9,12,9), Margin = new Thickness(8,0,0,0) }; Grid.SetColumn(reveal, 2); reveal.Click += (s, e) => Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + file.FullName + "\"", UseShellExecute = true }); grid.Children.Add(reveal);
                    if (file.Extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase))
                    {
                        var export = new Button { Content = "MP4", ToolTip = "Export to MP4 without re-encoding", Padding = new Thickness(12,9,12,9), Margin = new Thickness(8,0,0,0), IsEnabled = !HasActiveSession && !exporting };
                        Grid.SetColumn(export, 3); export.Click += async (s, e) => await ExportMp4(file.FullName); grid.Children.Add(export);
                    }
                    var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(18), Margin = new Thickness(0,0,0,10), Child = grid }; RecordingsList.Children.Add(card);
                }
                LibraryEmpty.Visibility = RecordingsList.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { StatusLabel.Text = "Couldn't read recordings: " + ex.Message; }
        }
        private async Task ExportMp4(string input)
        {
            if (HasActiveSession || busy || exporting) { StatusLabel.Text = "Finish recording, streaming and replay capture before exporting MP4."; return; }
            // File dialogs pump messages, including registered hotkeys and tray actions.
            // Guard the choice of destination as part of the export operation.
            busy = true; RecordButton.IsEnabled = false; SetControlsEnabled(false);
            bool exportStarted = false;
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Export MP4 · original recording is kept", Filter = "MP4 video (*.mp4)|*.mp4", DefaultExt = ".mp4", AddExtension = true, InitialDirectory = Path.GetDirectoryName(input), FileName = Path.GetFileNameWithoutExtension(input) + ".mp4", OverwritePrompt = true };
                if (dialog.ShowDialog(this) != true) return;
                if (recording || finalizing || exporting || closeAllowed) return;
                if (File.Exists(dialog.FileName)) { StatusLabel.Text = "Choose a new file name. Existing recordings are never overwritten."; return; }
                exportCancellation = new CancellationTokenSource(); exporting = true; exportStarted = true;
                busy = false; RecordButton.IsEnabled = true; // The record control now cancels this export.
                StateLabel.Text = "Exporting MP4…"; RecordingDetail.Text = "Copying the video and audio without re-encoding"; RecordLabel.Text = "Cancel export"; RecordGlyph.Text = "×";
                var lastUpdate = Stopwatch.StartNew();
                await Remuxer.RemuxAsync(input, dialog.FileName, progress =>
                {
                    if (lastUpdate.ElapsedMilliseconds < 150 && progress < 100) return;
                    lastUpdate.Restart();
                    Dispatcher.BeginInvoke(() => { if (exporting) StateLabel.Text = "Exporting MP4 · " + progress.ToString("0") + "%"; });
                }, exportCancellation.Token);
                StateLabel.Text = "MP4 ready"; StatusLabel.Text = "Exported " + Path.GetFileName(dialog.FileName) + ". Original MKV kept.";
            }
            catch (OperationCanceledException) { StateLabel.Text = "Export cancelled"; StatusLabel.Text = "Your original recording is unchanged."; }
            catch (Exception ex) { App.WriteError(ex); StateLabel.Text = "Couldn't export MP4"; StatusLabel.Text = ex.Message; }
            finally
            {
                exporting = false; busy = false;
                if (exportCancellation != null) { exportCancellation.Dispose(); exportCancellation = null; }
                RecordButton.IsEnabled = true; SetControlsEnabled(!recording && !finalizing);
                if (exportStarted) { RecordLabel.Text = "Start recording"; RecordGlyph.Text = "●"; UpdateOptions(); RefreshLibrary(); }
                if (closeAfterExport) { closeAfterExport = false; Close(); }
            }
        }
        private static string FormatSize(long bytes) { return bytes >= 1073741824 ? (bytes / 1073741824.0).ToString("0.0") + " GB" : (bytes / 1048576.0).ToString("0.0") + " MB"; }
    }
}
