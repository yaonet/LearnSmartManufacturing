using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Vision;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Visualizes the optical complementary color wheel (highlighting same-color reflection vs complementary absorption)
    /// alongside 6 real-world industrial color patches rendered as human color vs 8-bit monochrome camera grayscale.
    /// </summary>
    public class ColorSpectrumCanvas : FrameworkElement
    {
        public static readonly DependencyProperty WavelengthProperty = DependencyProperty.Register(
            nameof(Wavelength), typeof(LightWavelength), typeof(ColorSpectrumCanvas),
            new FrameworkPropertyMetadata(LightWavelength.Red630, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty PatchesProperty = DependencyProperty.Register(
            nameof(Patches), typeof(IReadOnlyList<ColorPatchReading>), typeof(ColorSpectrumCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly (string Label, string Hex, double AngleDeg)[] WheelNodes =
        [
            ("红 630nm", "#E05263", -90),
            ("黄 580nm", "#EAB308", -30),
            ("绿 525nm", "#2E9E62", 30),
            ("青 495nm", "#06B6D4", 90),
            ("蓝 470nm", "#3B82F6", 150),
            ("紫/UV 365", "#A855F7", 210),
        ];

        private static readonly Brush[] WheelBrushes = [.. WheelNodes.Select(n => DofPalette.Solid(n.Hex, 0.88))];
        private static readonly Pen RingPen = DofPalette.Pen(DofPalette.Surface, 1.5);
        private static readonly Pen ActivePen = DofPalette.Pen(DofPalette.White, 2.2);
        private static readonly Pen CompArrowPen = DofPalette.Pen(DofPalette.Warn, 1.6, dash: 4, gap: 3);
        private static readonly Pen BarBgPen = DofPalette.Pen(DofPalette.Surface, 1.0);
        private static readonly Brush BarBgFill = DofPalette.Solid("#1E1E2E");

        public ColorSpectrumCanvas()
        {
            ClipToBounds = true;
        }

        public LightWavelength Wavelength
        {
            get => (LightWavelength)GetValue(WavelengthProperty);
            set => SetValue(WavelengthProperty, value);
        }

        public IReadOnlyList<ColorPatchReading>? Patches
        {
            get => (IReadOnlyList<ColorPatchReading>?)GetValue(PatchesProperty);
            set => SetValue(PatchesProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 300 || h < 180)
            {
                return;
            }

            DofText.Draw(dc, this, "光谱互补色色轮 · 同色变亮 / 互补色吸收变黑", 10.5, DofPalette.White, 12, 16, bold: true);
            DofText.Draw(dc, this, $"当前光源：{LightingEngine.FormatWavelengthShort(Wavelength)}", 10, DofPalette.Fov, w - 12, 16, TextAlign.Right, bold: true);

            var leftW = Math.Min(w * 0.44, 240);
            DrawColorWheel(dc, leftW * 0.5 + 8, h * 0.54, Math.Min(leftW * 0.34, h * 0.32));
            DrawPatchComparison(dc, leftW + 18, 30, w - leftW - 30, h - 40);
        }

        private void DrawColorWheel(DrawingContext dc, double cx, double cy, double radius)
        {
            dc.DrawEllipse(null, RingPen, new Point(cx, cy), radius, radius);

            var activeIdx = Wavelength switch
            {
                LightWavelength.Red630 => 0,
                LightWavelength.Green525 => 2,
                LightWavelength.Blue470 => 4,
                LightWavelength.Uv365 => 5,
                _ => -1,
            };

            if (activeIdx >= 0)
            {
                var compIdx = (activeIdx + 3) % 6;
                var p1 = Polar(cx, cy, radius * 0.72, WheelNodes[activeIdx].AngleDeg);
                var p2 = Polar(cx, cy, radius * 0.72, WheelNodes[compIdx].AngleDeg);
                dc.DrawLine(CompArrowPen, p1, p2);
                DofText.Draw(dc, this, "互补吸收变黑", 8.5, DofPalette.Warn, cx, cy + 3, TextAlign.Center, bold: true);
            }
            else
            {
                var centerNote = Wavelength == LightWavelength.Ir850 ? "IR 850nm\n穿透油墨/薄塑" : "白光全谱\n均衡反射";
                var lines = centerNote.Split('\n');
                DofText.Draw(dc, this, lines[0], 9, DofPalette.White, cx, cy - 3, TextAlign.Center, bold: true);
                DofText.Draw(dc, this, lines[1], 8, DofPalette.Muted, cx, cy + 10, TextAlign.Center);
            }

            for (var i = 0; i < WheelNodes.Length; i++)
            {
                var pt = Polar(cx, cy, radius, WheelNodes[i].AngleDeg);
                var isActive = i == activeIdx;
                var isComp = activeIdx >= 0 && i == (activeIdx + 3) % 6;
                var r = isActive ? 15.0 : 12.0;

                dc.DrawEllipse(WheelBrushes[i], isActive ? ActivePen : isComp ? CompArrowPen : null, pt, r, r);

                var labelPt = Polar(cx, cy, radius + 24, WheelNodes[i].AngleDeg);
                var tag = isActive ? "★" + WheelNodes[i].Label : WheelNodes[i].Label;
                DofText.Draw(dc, this, tag, 8.2, isActive ? DofPalette.White : isComp ? DofPalette.Warn : DofPalette.Muted,
                    labelPt.X, labelPt.Y + 3, TextAlign.Center, bold: isActive || isComp);
            }
        }

        private void DrawPatchComparison(DrawingContext dc, double x, double y, double width, double height)
        {
            var patches = Patches;
            if (patches is null || patches.Count == 0 || width < 140)
            {
                return;
            }

            DofText.Draw(dc, this, "色卡", 8.5, DofPalette.Muted, x + 12, y + 8, TextAlign.Center);
            DofText.Draw(dc, this, "工业目标色", 8.5, DofPalette.Muted, x + 32, y + 8);
            DofText.Draw(dc, this, "黑白相机灰度响应 (0~255)", 8.5, DofPalette.Muted, x + width, y + 8, TextAlign.Right);

            var rowTop = y + 16;
            var rowH = Math.Max(22, (height - 16) / patches.Count);

            for (var i = 0; i < patches.Count; i++)
            {
                var item = patches[i];
                var ry = rowTop + i * rowH;
                var midY = ry + rowH * 0.5;

                // True color patch swatch
                var swatch = DofPalette.Solid(item.HexColor);
                dc.DrawRoundedRectangle(swatch, null, new Rect(x + 2, midY - 7, 20, 14), 3, 3);

                // Label
                DofText.Draw(dc, this, item.Name, 9, DofPalette.TextMain, x + 28, midY + 3.5);

                // Monochrome simulated swatch + 0..255 bar
                var barX = x + Math.Min(width * 0.48, 126);
                var barW = Math.Max(40, width - (barX - x) - 56);
                var g = (byte)Math.Clamp(item.MonochromeGray, 0, 255);
                var grayBrush = new SolidColorBrush(Color.FromRgb(g, g, g));
                grayBrush.Freeze();

                dc.DrawRoundedRectangle(grayBrush, BarBgPen, new Rect(barX - 24, midY - 7, 18, 14), 3, 3);
                var trackRect = new Rect(barX, midY - 5, barW, 10);
                dc.DrawRoundedRectangle(BarBgFill, BarBgPen, trackRect, 3, 3);
                var fillW = Math.Max(2, barW * (item.MonochromeGray / 255.0));
                dc.DrawRoundedRectangle(grayBrush, null, new Rect(barX, midY - 5, fillW, 10), 3, 3);

                DofText.Draw(dc, this, $"{item.MonochromeGray,3}", 9.5,
                    item.MonochromeGray >= 170 ? DofPalette.Dof : item.MonochromeGray <= 65 ? DofPalette.Warn : DofPalette.TextMain,
                    x + width, midY + 3.5, TextAlign.Right, bold: true);
            }
        }

        private static Point Polar(double cx, double cy, double r, double deg)
        {
            var rad = deg * Math.PI / 180.0;
            return new Point(cx + Math.Cos(rad) * r, cy + Math.Sin(rad) * r);
        }
    }
}
