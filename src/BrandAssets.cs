// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Rilmo
{
    public static class BrandAssets
    {
        // One small frozen decode shared by title bar and floating controls.
        public static BitmapSource Mark { get; } = LoadMark();
        private static BitmapSource LoadMark()
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri("pack://application:,,,/Assets/rilmo-logo.png");
            bitmap.DecodePixelWidth = 96;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        internal static System.Drawing.Icon CreateIcon()
        {
            using (var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/recorder.ico")).Stream)
            using (var icon = new System.Drawing.Icon(stream, 32, 32))
                return (System.Drawing.Icon)icon.Clone();
        }
    }
}
