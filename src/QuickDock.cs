// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Rilmo
{
    internal sealed class QuickDock : Window
    {
        private readonly Action<string> action;
        private readonly Action<int, int> savePosition;
        private readonly TextBlock glyph;
        private readonly Image brandMark;
        private readonly QuickControl ring;
        private Point pointerDown;
        private bool moving, pressed, active, paused, streaming, replay, busy, cleanupPending, camera;
        private string elapsed = "00:00";
        private QuickMenu menu;
        private IntPtr handle;
        private readonly int? savedX, savedY;
        public bool CaptureExclusionApplied { get; private set; }
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateEllipticRgn(int left,int top,int right,int bottom);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);

        public QuickDock(Action<string> action, Action<int, int> savePosition, int? x, int? y)
        {
            this.action = action; this.savePosition = savePosition; savedX = x; savedY = y;
            Title = "Rilmo quick controls"; Width = Height = 58; MinWidth = MinHeight = 0;
            // A layered WPF window cannot reliably use capture exclusion on Windows 10.
            // An ordinary HWND with a native ellipse keeps the round shape and supports it.
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            Background = (Brush)Application.Current.FindResource("PanelBrush"); ShowInTaskbar = false; Topmost = true; ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            glyph = new TextBlock { Text = "●", FontSize = 24, FontFamily = new FontFamily("Segoe UI"), Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            brandMark = new Image { Source = BrandAssets.Mark, Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(brandMark, BitmapScalingMode.HighQuality);
            var face = new Grid(); face.Children.Add(brandMark); face.Children.Add(glyph);
            glyph.Visibility = Visibility.Collapsed;
            var normalRing = (Brush)Application.Current.FindResource("AccentBrush");
            ring = new QuickControl(OpenMenu) { CornerRadius = new CornerRadius(28), BorderThickness = new Thickness(2), BorderBrush = normalRing, Background = Background, Margin = new Thickness(3), Child = face, Focusable = true, Cursor = Cursors.Hand, ToolTip = "Quick controls · drag to move · Enter to open" };
            AutomationProperties.SetName(ring, "Rilmo recording controls");
            AutomationProperties.SetAutomationId(ring, "QuickControlsButton");
            AutomationProperties.SetHelpText(ring, "Press Enter or Space to open recording controls. Drag to move the floating button.");
            ring.IsKeyboardFocusWithinChanged += (s, e) => ring.BorderBrush = (bool)e.NewValue ? Brushes.White : normalRing;
            Content = ring;
            ring.MouseLeftButtonDown += (s, e) => { ring.Focus(); pointerDown = e.GetPosition(this); pressed = true; moving = false; ring.CaptureMouse(); e.Handled = true; };
            ring.MouseMove += (s, e) =>
            {
                if (!pressed || moving || e.LeftButton != MouseButtonState.Pressed) return;
                Point current = e.GetPosition(this);
                if (Math.Abs(current.X - pointerDown.X) + Math.Abs(current.Y - pointerDown.Y) < 5) return;
                moving = true; pressed = false; ring.ReleaseMouseCapture(); CloseMenu();
                try { DragMove(); } catch (InvalidOperationException) { }
                KeepOnScreen(true);
            };
            ring.MouseLeftButtonUp += (s, e) => { ring.ReleaseMouseCapture(); bool click = pressed && !moving; pressed = false; moving = false; if (click) OpenMenu(); e.Handled = true; };
            ring.KeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Space) { if (!e.IsRepeat) OpenMenu(); e.Handled = true; } else if (e.Key == Key.Escape) { CloseMenu(); e.Handled = true; } };
            SourceInitialized += (s, e) =>
            {
                handle = new WindowInteropHelper(this).Handle;
                // Microsoft's supported recorder-control exclusion. This is not a DRM guarantee.
                CaptureExclusionApplied = SetWindowDisplayAffinity(handle, 0x11);
                var area = Forms.Screen.PrimaryScreen.WorkingArea;
                SetWindowPos(handle, IntPtr.Zero, savedX ?? area.Right - 84, savedY ?? area.Top + area.Height / 3, 0, 0, 0x0015);
            };
            Loaded += (s, e) => { KeepOnScreen(false); CaptureExclusionApplied=SetWindowDisplayAffinity(handle,0x11); };
            IsVisibleChanged += (s, e) => { if (!IsVisible) CloseMenu(); };
            Closed += (s, e) => CloseMenu();
        }
        internal void KeepOnScreen(bool remember)
        {
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out Rect rect)) return;
            var work = Forms.Screen.FromRectangle(new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)).WorkingArea;
            int x = Math.Clamp(rect.Left, work.Left, Math.Max(work.Left, work.Right - (rect.Right - rect.Left)));
            int y = Math.Clamp(rect.Top, work.Top, Math.Max(work.Top, work.Bottom - (rect.Bottom - rect.Top)));
            SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x0015);
            IntPtr shape=CreateEllipticRgn(1,1,rect.Right-rect.Left-1,rect.Bottom-rect.Top-1);
            if(shape!=IntPtr.Zero && SetWindowRgn(handle,shape,true)==0) DeleteObject(shape);
            if (remember) savePosition(x, y);
        }
        public void UpdateState(bool recording, bool isPaused, bool isStreaming, bool isReplay, bool operationBusy, string time, bool needsCleanup=false, bool isCamera=false)
        {
            active = recording; paused = isPaused; streaming = isStreaming; replay = isReplay; busy = operationBusy; elapsed = time; cleanupPending=needsCleanup; camera=isCamera;
            bool showStatus = active || streaming || replay || busy || cleanupPending || camera;
            brandMark.Visibility = showStatus ? Visibility.Collapsed : Visibility.Visible;
            glyph.Visibility = showStatus ? Visibility.Visible : Visibility.Collapsed;
            glyph.Text = busy ? "···" : cleanupPending ? "!" : active ? (paused ? "Ⅱ" : "■") : streaming ? "↗" : replay ? "↶" : camera ? "◎" : "●";
            ring.ToolTip = cleanupPending ? "Capture needs to finish · click to retry" : active ? (paused ? "Paused · " : "Recording · ") + elapsed + " · click for controls" : streaming ? "Streaming live · click for controls" : replay ? "Replay buffer active · click to save" : camera ? "Virtual camera active · click for controls" : "Quick controls · drag to move · Enter to open";
            // Expose state without announcing a new elapsed-time value every second.
            AutomationProperties.SetItemStatus(ring, busy ? "Working" : cleanupPending ? "Capture needs to finish" : active ? (paused ? "Recording paused" : "Recording") : streaming ? "Streaming live" : replay ? "Replay buffer active" : camera ? "Virtual camera active" : "Ready to capture");
            if (menu != null) menu.UpdateState(active, paused, streaming, replay, busy, elapsed,cleanupPending,camera);
        }
        public void OpenMenu()
        {
            if (!IsVisible) return;
            if (menu != null) { CloseMenu(); return; }
            var popupWindow = new QuickMenu(command => { CloseMenu(); action(command); }, () => CloseMenu(true)) { Owner = this };
            menu = popupWindow;
            popupWindow.UpdateState(active, paused, streaming, replay, busy, elapsed,cleanupPending,camera);
            popupWindow.SourceInitialized += (s, e) => SetWindowDisplayAffinity(new WindowInteropHelper(popupWindow).Handle, 0x11);
            popupWindow.Closed += (s, e) => { if (ReferenceEquals(menu, popupWindow)) menu = null; };
            popupWindow.Show(); popupWindow.UpdateLayout();
            Motion.Reveal((FrameworkElement)popupWindow.Content,active||streaming||replay||camera);
            if (!popupWindow.IsVisible) return;
            if (GetWindowRect(handle, out Rect anchor))
            {
                IntPtr menuHandle = new WindowInteropHelper(popupWindow).Handle;
                if (GetWindowRect(menuHandle, out Rect popup))
                {
                    var work = Forms.Screen.FromHandle(handle).WorkingArea;
                    int width = popup.Right - popup.Left, height = popup.Bottom - popup.Top;
                    int x = anchor.Right + 8 + width <= work.Right ? anchor.Right + 8 : anchor.Left - width - 8;
                    x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
                    int y = Math.Clamp(anchor.Top, work.Top, Math.Max(work.Top, work.Bottom - height));
                    SetWindowPos(menuHandle, IntPtr.Zero, x, y, 0, 0, 0x0015);
                }
            }
        }
        public void CloseMenu(bool restoreFocus = false)
        {
            if (menu != null) { var previous = menu; menu = null; previous.Close(); }
            // Only an explicit Escape returns focus. Dismissal by clicking another
            // application must not take focus back from that application.
            if (restoreFocus && IsVisible) { Activate(); ring.Focus(); Keyboard.Focus(ring); }
        }

        private sealed class QuickControl : Border
        {
            private readonly Action invoke;
            public QuickControl(Action invoke) { this.invoke = invoke; }
            protected override AutomationPeer OnCreateAutomationPeer() => new QuickControlPeer(this);

            private sealed class QuickControlPeer : FrameworkElementAutomationPeer, IInvokeProvider
            {
                public QuickControlPeer(QuickControl owner) : base(owner) { }
                protected override string GetClassNameCore() => "RilmoQuickControl";
                protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
                protected override bool IsControlElementCore() => true;
                protected override bool IsContentElementCore() => true;
                public override object GetPattern(PatternInterface pattern) => pattern == PatternInterface.Invoke ? this : base.GetPattern(pattern);
                public void Invoke()
                {
                    if (!IsEnabled()) throw new ElementNotEnabledException();
                    var control = (QuickControl)Owner;
                    control.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                    {
                        if (control.IsEnabled && control.IsVisible) control.invoke();
                    }));
                }
            }
        }

        private sealed class QuickMenu : Window
        {
            private bool closing;
            private readonly TextBlock status;
            private readonly Button record, pause, live, replayButton, screenshot, cameraButton;
            public QuickMenu(Action<string> action, Action dismissWithFocus)
            {
                Title = "Quick controls"; Width = 278; SizeToContent = SizeToContent.Height;
                FontFamily=(FontFamily)Application.Current.FindResource("UiFont");FontSize=14;FontWeight=FontWeights.SemiBold;
                WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
                Background = (Brush)Application.Current.FindResource("PanelBrush"); Foreground = (Brush)Application.Current.FindResource("TextBrush");
                var panel = new StackPanel { Margin = new Thickness(12) };
                KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Cycle);
                KeyboardNavigation.SetDirectionalNavigation(panel, KeyboardNavigationMode.Cycle);
                panel.Children.Add(new TextBlock { Text = "Quick capture", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = (Brush)Application.Current.FindResource("MutedBrush"), Margin = new Thickness(8,4,0,8) });
                status = new TextBlock { Text = "Ready", FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(9,0,0,10) }; panel.Children.Add(status);
                record = Add(panel, "●   Start recording", "record", action);
                pause = Add(panel, "Ⅱ   Pause", "pause", action);
                live = Add(panel, "↗   Stream", "live", action);
                screenshot = Add(panel, "▣   Screenshot", "screenshot", action);
                replayButton = Add(panel, "↶   Replay buffer", "replay", action); cameraButton=Add(panel,"◎   Virtual camera","camera",action); Add(panel,"▧   Scene studio","studio",action);
                cameraButton.Visibility=Rilmo.Engine.AppEdition.OffersVirtualCamera?Visibility.Visible:Visibility.Collapsed;
                panel.Children.Add(new Border { Height = 1, Background = (Brush)Application.Current.FindResource("LineBrush"), Margin = new Thickness(8,8,8,8) });
                Add(panel, "▤   Open recorder", "open", action);
                Add(panel, "⚙   Settings", "settings", action);
                Add(panel, "×   Hide floating button", "hide", action);
                Content = new Border { BorderBrush = (Brush)Application.Current.FindResource("LineBrush"), BorderThickness = new Thickness(1), CornerRadius=new CornerRadius(14), Child = panel };
                KeyDown += (s,e) => { if (e.Key == Key.Escape) { e.Handled = true; dismissWithFocus(); } };
                Closing += (s,e)=>closing=true;
                Deactivated += (s,e) => { if(!closing) Close(); };
                Loaded += (s,e) => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (!IsVisible || !IsActive) return;
                    foreach (UIElement child in panel.Children)
                    {
                        if (child is Button button && button.IsEnabled && button.Visibility == Visibility.Visible)
                        {
                            button.Focus(); Keyboard.Focus(button); break;
                        }
                    }
                }));
            }
            private static Button Add(Panel panel, string text, string command, Action<string> action)
            {
                var button = new Button { Content = text, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12,9,12,9), Margin = new Thickness(0,2,0,2), Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 14 };
                button.ContentTemplate=(DataTemplate)Application.Current.FindResource("QuickActionContent");
                AutomationProperties.SetAutomationId(button, "QuickAction_" + command);
                SetActionName(button);
                button.Click += (s,e) => action(command); panel.Children.Add(button); return button;
            }
            private static void SetActionName(Button button)
            {
                string text = button.Content as string ?? "";
                int separator = text.IndexOf("   ", StringComparison.Ordinal);
                AutomationProperties.SetName(button, separator < 0 ? text : text.Substring(separator + 3));
            }
            public void UpdateState(bool recording, bool paused, bool streaming, bool replay, bool busy, string time,bool cleanupPending,bool camera)
            {
                status.Text = busy ? "Working…" : cleanupPending ? "Capture needs to finish" : recording ? (paused ? "Paused · " : "Recording · ") + time : streaming ? "Streaming live" : replay ? "Replay buffer is running" : camera ? "Virtual camera active" : "Ready to capture";
                record.Content = cleanupPending ? "■   Finish capture" : recording ? "■   Stop & save" : "●   Start recording";
                record.IsEnabled = !busy; record.Background = (Brush)Application.Current.FindResource("AccentBrush"); record.Foreground=(Brush)Application.Current.FindResource("BackgroundBrush");
                pause.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
                pause.Content = paused ? "▶   Resume" : "Ⅱ   Pause"; pause.IsEnabled = !busy && !streaming && !replay && !camera;
                live.Content = streaming ? "■   Stop streaming" : "↗   Stream"; live.IsEnabled = !busy;
                replayButton.Content = replay ? "↶   Save replay" : "↶   Replay buffer"; replayButton.IsEnabled = !busy;
                screenshot.IsEnabled = !busy; cameraButton.Content=camera?"■   Stop virtual camera":"◎   Virtual camera";cameraButton.IsEnabled=!busy;SetActionName(cameraButton);
                SetActionName(record); SetActionName(pause); SetActionName(live); SetActionName(replayButton);
            }
        }
    }
}
