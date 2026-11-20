using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>Fixed dark "workbench" palette shared by the optical drawing controls.</summary>
    internal static class DofPalette
    {
        public static readonly Brush Canvas = Solid("#141420");
        public static readonly Brush Base = Solid("#1E1E2E");
        public static readonly Brush Surface = Solid("#313244");
        public static readonly Brush Fov = Solid("#89DCEB");
        public static readonly Brush Dof = Solid("#A6E3A1");
        public static readonly Brush Ray = Solid("#F9E2AF");
        public static readonly Brush Warn = Solid("#F38BA8");
        public static readonly Brush Aperture = Solid("#CBA6F7");
        public static readonly Brush Accent = Solid("#89B4FA");
        public static readonly Brush Muted = Solid("#A6ADC8");
        public static readonly Brush TextMain = Solid("#CDD6F4");
        public static readonly Brush Subtle = Solid("#6C7086");
        public static readonly Brush White = Solid("#FFFFFF");

        public static Brush Solid(string hex, double opacity = 1)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color) { Opacity = opacity };
            brush.Freeze();
            return brush;
        }

        public static Brush WithOpacity(Brush brush, double opacity)
        {
            var clone = brush.Clone();
            clone.Opacity = opacity;
            clone.Freeze();
            return clone;
        }

        public static Pen Pen(Brush brush, double thickness, double dash = 0, double gap = 0)
        {
            var pen = new Pen(brush, thickness);
            if (dash > 0)
            {
                // WPF dash lengths are multiples of the pen thickness.
                pen.DashStyle = new DashStyle([dash / thickness, gap / thickness], 0);
            }

            pen.Freeze();
            return pen;
        }
    }

    internal enum TextAlign { Left, Center, Right }

    internal static class DofText
    {
        private static readonly Typeface Face = new(new FontFamily("Consolas, Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface BoldFace = new(new FontFamily("Consolas, Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        public static FormattedText Format(Visual owner, string text, double size, Brush brush, bool bold = false) =>
            new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold ? BoldFace : Face, size, brush,
                VisualTreeHelper.GetDpi(owner).PixelsPerDip);

        /// <summary>Draws text with <paramref name="y"/> as the baseline, like canvas fillText.</summary>
        public static double Draw(DrawingContext dc, Visual owner, string text, double size, Brush brush, double x, double y,
            TextAlign align = TextAlign.Left, bool bold = false)
        {
            var ft = Format(owner, text, size, brush, bold);
            var left = align switch
            {
                TextAlign.Center => x - ft.WidthIncludingTrailingWhitespace / 2,
                TextAlign.Right => x - ft.WidthIncludingTrailingWhitespace,
                _ => x,
            };
            dc.DrawText(ft, new Point(left, y - ft.Baseline));
            return ft.WidthIncludingTrailingWhitespace;
        }

        public static double Measure(Visual owner, string text, double size, bool bold = false) =>
            Format(owner, text, size, Brushes.Black, bold).WidthIncludingTrailingWhitespace;
    }
}
