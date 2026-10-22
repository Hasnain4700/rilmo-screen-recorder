using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shapes = System.Windows.Shapes;

namespace Rilmo
{
    // Uses actual screen pixels for the selected rectangle, including mixed-DPI desktops.
    internal sealed class RegionPicker : Window
    {
        private readonly System.Drawing.Rectangle display;
        private readonly Canvas canvas = new Canvas();
        private readonly Shapes.Rectangle outline = new Shapes.Rectangle { Stroke = Brushes.White, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(35, 239, 73, 101)) };
        private readonly int minimumWidth, minimumHeight;
        private Point origin;
        private bool dragging;
        public System.Drawing.Rectangle Selection { get; private set; }
        public bool CaptureExclusionApplied { get; private set; }
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd,uint affinity);

        public RegionPicker(System.Drawing.Rectangle display, int minimumWidth = 320, int minimumHeight = 180)
        {
            this.display = display; this.minimumWidth = minimumWidth; this.minimumHeight = minimumHeight;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            Background = Brushes.Black; Topmost = true; ShowInTaskbar = false;
            // A frozen desktop image allows an ordinary, capture-excluded HWND instead
            // of a layered transparency window that leaks into ongoing recordings.
            var layers=new Grid();
            using(var bitmap=new System.Drawing.Bitmap(display.Width,display.Height))
            using(var graphics=System.Drawing.Graphics.FromImage(bitmap))
            using(var stream=new MemoryStream())
            {
                graphics.CopyFromScreen(display.Location,System.Drawing.Point.Empty,display.Size);
                bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Png); stream.Position=0;
                var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();
                layers.Children.Add(new Image { Source=image,Stretch=Stretch.Fill });
            }
            layers.Children.Add(new Border { Background=new SolidColorBrush(Color.FromArgb(130,8,8,11)) });
            layers.Children.Add(canvas);
            WindowStartupLocation = WindowStartupLocation.Manual; Cursor = Cursors.Cross; Content = layers;
            var tip = new Border { Background = (Brush)Application.Current.FindResource("RaisedBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(20, 14, 20, 14), Child = new TextBlock { Text = "Drag an area · " + minimumWidth + " × " + minimumHeight + " px minimum · Esc to cancel", Foreground = Brushes.White, FontSize = 14, FontFamily=(FontFamily)Application.Current.FindResource("UiFont"), FontWeight=FontWeights.SemiBold } };
            Canvas.SetLeft(tip, 30); Canvas.SetTop(tip, 30); canvas.Children.Add(tip);
            outline.Visibility = Visibility.Collapsed; canvas.Children.Add(outline);
            SourceInitialized += (s, e) => { IntPtr handle=new WindowInteropHelper(this).Handle;CaptureExclusionApplied=SetWindowDisplayAffinity(handle,0x11);SetWindowPos(handle, new IntPtr(-1), display.Left, display.Top, display.Width, display.Height, 0x0040); };
            Loaded += (s,e)=>CaptureExclusionApplied=SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle,0x11);
            KeyDown += (s, e) => { if (e.Key == Key.Escape) { DialogResult = false; Close(); } };
            MouseLeftButtonDown += (s, e) => { origin = e.GetPosition(canvas); dragging = true; CaptureMouse(); outline.Visibility = Visibility.Visible; UpdateOutline(origin); };
            MouseMove += (s, e) => { if (dragging) UpdateOutline(e.GetPosition(canvas)); };
            MouseLeftButtonUp += (s, e) =>
            {
                if (!dragging) return;
                dragging = false; ReleaseMouseCapture();
                Point end = e.GetPosition(canvas), first = PointToScreen(origin), second = PointToScreen(end);
                int x = Math.Clamp((int)Math.Min(first.X, second.X) - display.Left, 0, display.Width - 2);
                int y = Math.Clamp((int)Math.Min(first.Y, second.Y) - display.Top, 0, display.Height - 2);
                int width = Math.Min(display.Width - x, (int)Math.Abs(first.X - second.X));
                int height = Math.Min(display.Height - y, (int)Math.Abs(first.Y - second.Y));
                width = width / 2 * 2; height = height / 2 * 2;
                if (width < minimumWidth || height < minimumHeight) { outline.Visibility = Visibility.Collapsed; return; }
                Selection = new System.Drawing.Rectangle(x, y, width, height); DialogResult = true; Close();
            };
        }
        private void UpdateOutline(Point end)
        {
            Canvas.SetLeft(outline, Math.Min(origin.X, end.X)); Canvas.SetTop(outline, Math.Min(origin.Y, end.Y));
            outline.Width = Math.Abs(end.X - origin.X); outline.Height = Math.Abs(end.Y - origin.Y);
        }
    }
}
