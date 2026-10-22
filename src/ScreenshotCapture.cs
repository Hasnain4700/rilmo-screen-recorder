// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;

namespace Rilmo
{
    internal static class ScreenshotCapture
    {
        // One screen copy, no ongoing capture, no native recording runtime or preview loop.
        public static Task<string> SaveAsync(Rectangle bounds, string directory)
        {
            if (bounds.Width < 2 || bounds.Height < 2 || bounds.Width > 16384 || bounds.Height > 16384)
                throw new ArgumentException("Choose a valid screenshot area.");
            return Task.Run(() =>
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "Screenshot " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + "-" + Guid.NewGuid().ToString("N").Substring(0,6) + ".png");
                using (var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                    using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) bitmap.Save(stream, ImageFormat.Png);
                }
                return path;
            });
        }
    }
}
