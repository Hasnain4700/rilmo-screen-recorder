using System;
using System.Globalization;
using System.Windows.Data;

namespace Rilmo
{
    // Keep command strings and accessible names intact; render their decorative
    // symbols as consistent outline paths instead of font-dependent Unicode glyphs.
    public sealed class QuickActionText : IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
        {string text=value as string??"";int separator=text.IndexOf("   ",StringComparison.Ordinal);return separator<0?text:text.Substring(separator+3);}
        public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
    public sealed class QuickActionIcon : IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
        {
            string text=value as string??"";
            return (text.Length==0?' ':text[0]) switch
            {
                '●'=>"M 12,4 A 8,8 0 1 1 11.99,4 Z",
                '■'=>"M 5,5 H 19 V 19 H 5 Z",
                'Ⅱ'=>"M 7,4 V 20 M 17,4 V 20",
                '▶'=>"M 6,3 L 21,12 L 6,21 Z",
                '↗'=>"M 5,19 L 19,5 M 7,5 H 19 V 17",
                '▣'=>"M 3,7 H 7 L 9,4 H 15 L 17,7 H 21 V 20 H 3 Z M 16,13 A 4,4 0 1 1 8,13 A 4,4 0 1 1 16,13",
                '↶'=>"M 4,10 A 8,8 0 1 1 5,18 M 4,3 V 10 H 11",
                '◎'=>"M 3,6 H 15 V 18 H 3 Z M 15,10 L 22,6 V 18 L 15,14",
                '▧'=>"M 3,3 H 16 V 16 H 3 Z M 8,8 H 21 V 21 H 8 Z",
                '▤'=>"M 2,4 H 22 V 18 H 2 Z M 8,22 H 16 M 12,18 V 22",
                '⚙'=>"M 4,6 H 20 M 4,12 H 20 M 4,18 H 20 M 8,3 V 9 M 16,9 V 15 M 10,15 V 21",
                _=>"M 5,5 L 19,19 M 19,5 L 5,19"
            };
        }
        public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
}
