using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Draws the similar-triangle optical path of a lens. FocalLength / WorkingDistance changes are eased toward the target.
    /// </summary>
    public class OpticsDiagram : FrameworkElement
    {
        public static readonly DependencyProperty FocalLengthProperty = DependencyProperty.Register(
            nameof(FocalLength), typeof(double), typeof(OpticsDiagram),
            new FrameworkPropertyMetadata(16.0, OnTargetChanged));

        public static readonly DependencyProperty WorkingDistanceProperty = DependencyProperty.Register(
            nameof(WorkingDistance), typeof(double), typeof(OpticsDiagram),
            new FrameworkPropertyMetadata(300.0, OnTargetChanged));

        public static readonly DependencyProperty SensorWidthProperty = DependencyProperty.Register(
            nameof(SensorWidth), typeof(double), typeof(OpticsDiagram),
            new FrameworkPropertyMetadata(8.45, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Color SensorColor = Color.FromRgb(0x2B, 0x9A, 0x57);
        private static readonly Color RayColor = Color.FromRgb(0x25, 0x75, 0xFC);
        private static readonly Color CenterColor = Color.FromRgb(0xC9, 0x93, 0x1A);
        private static readonly Typeface TextFace = new("Microsoft YaHei UI");
        private static readonly Typeface MonoFace = new("Consolas");

        private const double Ease = 0.22;

        private double _shownFocalLength = 16;
        private double _shownWorkingDistance = 300;
        private bool _animating;

        public OpticsDiagram()
        {
            Unloaded += (_, _) => StopAnimation();
        }

        public double FocalLength
        {
            get => (double)GetValue(FocalLengthProperty);
            set => SetValue(FocalLengthProperty, value);
        }

        public double WorkingDistance
        {
            get => (double)GetValue(WorkingDistanceProperty);
            set => SetValue(WorkingDistanceProperty, value);
        }

        public double SensorWidth
        {
            get => (double)GetValue(SensorWidthProperty);
            set => SetValue(SensorWidthProperty, value);
        }

        private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((OpticsDiagram)d).StartAnimation();
        }

        private void StartAnimation()
        {
            // Before the first layout there is nothing to animate from, so snap to the target.
            if (!IsLoaded)
            {
                _shownFocalLength = FocalLength;
                _shownWorkingDistance = WorkingDistance;
                InvalidateVisual();
                return;
            }

            if (!_animating)
            {
                _animating = true;
                CompositionTarget.Rendering += OnRendering;
            }
        }

        private void StopAnimation()
        {
            if (_animating)
            {
                CompositionTarget.Rendering -= OnRendering;
                _animating = false;
            }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            var df = FocalLength - _shownFocalLength;
            var dwd = WorkingDistance - _shownWorkingDistance;

            if (Math.Abs(df) > 0.04 || Math.Abs(dwd) > 0.1)
            {
                _shownFocalLength += df * Ease;
                _shownWorkingDistance += dwd * Ease;
            }
            else
            {
                _shownFocalLength = FocalLength;
                _shownWorkingDistance = WorkingDistance;
                StopAnimation();
            }

            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 200 || h < 150)
            {
                return;
            }

            var onSurface = ThemeBrush("TextFillColorPrimaryBrush", SystemColors.WindowTextBrush);
            var variant = ThemeBrush("TextFillColorSecondaryBrush", SystemColors.GrayTextBrush);
            var panel = ThemeBrush("SolidBackgroundFillColorTertiaryBrush", SystemColors.WindowBrush);
            var low = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80));
            var sensorBrush = new SolidColorBrush(SensorColor);
            var rayBrush = new SolidColorBrush(RayColor);
            var centerBrush = new SolidColorBrush(CenterColor);

            var f = _shownFocalLength;
            var wd = _shownWorkingDistance;
            var sw = SensorWidth;
            var (fovWidth, fovAngleDeg, _) = OpticsCalculator.Compute(f, wd, sw);

            var fRange = OpticsCalculator.MaxFocalLength - OpticsCalculator.MinFocalLength;
            var wdRange = OpticsCalculator.MaxWorkingDistance - OpticsCalculator.MinWorkingDistance;

            // Layout: sensor on the left, optical center O in the middle, object plane on the right.
            var cy = Math.Round(h * 0.47);
            var ox = Math.Round(w * 0.36);

            var sensorHalfH = Math.Min(25, h * 0.082);
            var sMinDist = Math.Max(48, w * 0.085);
            var sMaxDist = Math.Max(135, ox - 56);
            var sDist = sMinDist + (f - OpticsCalculator.MinFocalLength) / fRange * (sMaxDist - sMinDist);
            var sx = ox - sDist;

            var wMaxDist = Math.Max(150, w - ox - 68);
            var wMinDist = Math.Max(105, wMaxDist * 0.52);
            var wDist = wMinDist + (wd - OpticsCalculator.MinWorkingDistance) / wdRange * (wMaxDist - wMinDist);
            var objX = ox + wDist;

            // Both chief rays pass straight through O; clamp the object side and rescale the sensor side to keep them straight.
            var rawObjHalfH = sensorHalfH / sDist * wDist;
            var maxObjHalfH = cy - 36;
            var objHalfH = Math.Min(rawObjHalfH, maxObjHalfH);
            var effSensorHalfH = rawObjHalfH > maxObjHalfH ? objHalfH / wDist * sDist : sensorHalfH;

            var dashed = new DashStyle([4, 4], 0);

            // Optical axis and the vertical reference line through O
            var axisPen = new Pen(low, 1) { DashStyle = dashed };
            dc.DrawLine(axisPen, new Point(20, cy), new Point(w - 20, cy));
            dc.DrawLine(axisPen, new Point(ox, 22), new Point(ox, h - 42));

            // Similar triangle fills
            dc.DrawGeometry(WithOpacity(SensorColor, 0.12), null, Triangle(ox, cy, sx, cy - effSensorHalfH, sx, cy + effSensorHalfH));
            dc.DrawGeometry(WithOpacity(RayColor, 0.12), null, Triangle(ox, cy, objX, cy - objHalfH, objX, cy + objHalfH));

            // FOV angle arcs (vertically opposite angles)
            var arcRight = Math.Min(44, wDist * 0.32);
            var arcLeft = Math.Min(34, sDist * 0.55);
            var halfAngle = Math.Atan2(objHalfH, wDist);
            var arcPen = new Pen(centerBrush, 1.6);
            dc.DrawGeometry(null, arcPen, Arc(ox, cy, arcRight, halfAngle, left: false));
            dc.DrawGeometry(null, arcPen, Arc(ox, cy, arcLeft, halfAngle, left: true));

            var angleLabel = $"θ = {fovAngleDeg:F1}°";
            var angleText = Text(angleLabel, MonoFace, 12, centerBrush);
            var angleX = ox + arcRight + 8;
            var angleY = cy - 11;
            dc.DrawRoundedRectangle(WithOpacity(panel, 0.9), null,
                new Rect(angleX - 5, angleY - 9, angleText.Width + 10, 18), 5, 5);
            dc.DrawText(angleText, new Point(angleX, angleY - angleText.Height / 2));

            // Chief rays
            var rayPen = new Pen(rayBrush, 2);
            dc.DrawLine(rayPen, new Point(sx, cy - effSensorHalfH), new Point(objX, cy + objHalfH));
            dc.DrawLine(rayPen, new Point(sx, cy + effSensorHalfH), new Point(objX, cy - objHalfH));

            // Lens and optical center O
            var lensHalfH = Math.Max(46, effSensorHalfH + 22);
            dc.DrawEllipse(WithOpacity(panel, 0.85), new Pen(variant, 1.5), new Point(ox, cy), 9, lensHalfH);
            dc.DrawEllipse(centerBrush, new Pen(panel, 1.5), new Point(ox, cy), 4.5, 4.5);
            DrawText(dc, "镜头光心 O", TextFace, 12, onSurface, ox, cy - lensHalfH - 8, HAlign.Center, VAlign.Bottom);

            // Sensor plane
            dc.DrawLine(new Pen(low, 1), new Point(sx, cy - effSensorHalfH - 22), new Point(sx, h - 38));
            var sensorPen = new Pen(sensorBrush, 5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(sensorPen, new Point(sx, cy - effSensorHalfH), new Point(sx, cy + effSensorHalfH));
            dc.DrawEllipse(sensorBrush, null, new Point(sx, cy - effSensorHalfH), 3.5, 3.5);
            dc.DrawEllipse(sensorBrush, null, new Point(sx, cy + effSensorHalfH), 3.5, 3.5);
            DrawText(dc, "[Sensor]", TextFace, 12, sensorBrush, sx, cy - effSensorHalfH - 20, HAlign.Center, VAlign.Bottom);
            DrawText(dc, $"{sw:F2} mm", MonoFace, 11, onSurface, sx, cy - effSensorHalfH - 6, HAlign.Center, VAlign.Bottom);

            // Object plane and the highlighted FOV
            dc.DrawLine(new Pen(low, 1.5) { DashStyle = new DashStyle([2, 2], 0) }, new Point(objX, 20), new Point(objX, h - 38));
            var fovPen = new Pen(rayBrush, 5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(fovPen, new Point(objX, cy - objHalfH), new Point(objX, cy + objHalfH));
            dc.DrawEllipse(rayBrush, null, new Point(objX, cy - objHalfH), 4, 4);
            dc.DrawEllipse(rayBrush, null, new Point(objX, cy + objHalfH), 4, 4);
            DrawText(dc, $"被测物视野 FOV = {fovWidth:F1} mm", TextFace, 12, rayBrush,
                objX - 8, Math.Max(22, cy - objHalfH - 6), HAlign.Right, VAlign.Bottom);

            // Dimension lines for f and WD
            var dimY = h - 22;
            DrawDimensionLine(dc, sx, ox, dimY, $"f = {Math.Round(f)} mm", sensorBrush, panel);
            DrawDimensionLine(dc, ox, objX, dimY, $"WD = {Math.Round(wd)} mm", rayBrush, panel);

            DrawText(dc, $"相似比: FOV / Ws = WD / f = {wd / f:F1}", MonoFace, 12, variant, 16, 14, HAlign.Left, VAlign.Top);
        }

        private void DrawDimensionLine(DrawingContext dc, double x1, double x2, double y, string label, Brush color, Brush pillBackground)
        {
            var pen = new Pen(color, 1.25);
            dc.DrawLine(pen, new Point(x1, y), new Point(x2, y));
            dc.DrawLine(pen, new Point(x1, y - 5), new Point(x1, y + 5));
            dc.DrawLine(pen, new Point(x2, y - 5), new Point(x2, y + 5));

            if (Math.Abs(x2 - x1) > 22)
            {
                var dir = x2 >= x1 ? 1 : -1;
                dc.DrawLine(pen, new Point(x1 + dir * 5, y - 3.5), new Point(x1, y));
                dc.DrawLine(pen, new Point(x1 + dir * 5, y + 3.5), new Point(x1, y));
                dc.DrawLine(pen, new Point(x2 - dir * 5, y - 3.5), new Point(x2, y));
                dc.DrawLine(pen, new Point(x2 - dir * 5, y + 3.5), new Point(x2, y));
            }

            var text = Text(label, MonoFace, 12, color);
            var midX = (x1 + x2) / 2;
            dc.DrawRoundedRectangle(pillBackground, null, new Rect(midX - (text.Width + 12) / 2, y - 9, text.Width + 12, 18), 6, 6);
            dc.DrawText(text, new Point(midX - text.Width / 2, y - text.Height / 2));
        }

        private enum HAlign { Left, Center, Right }

        private enum VAlign { Top, Middle, Bottom }

        private void DrawText(DrawingContext dc, string s, Typeface face, double size, Brush brush, double x, double y, HAlign ha, VAlign va)
        {
            var text = Text(s, face, size, brush);
            var left = ha switch { HAlign.Center => x - text.Width / 2, HAlign.Right => x - text.Width, _ => x };
            var top = va switch { VAlign.Middle => y - text.Height / 2, VAlign.Bottom => y - text.Height, _ => y };
            dc.DrawText(text, new Point(left, top));
        }

        private FormattedText Text(string s, Typeface face, double size, Brush brush) =>
            new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

        private Brush ThemeBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

        private static Brush WithOpacity(Color color, double opacity)
        {
            var brush = new SolidColorBrush(color) { Opacity = opacity };
            brush.Freeze();
            return brush;
        }

        private static Brush WithOpacity(Brush source, double opacity)
        {
            var brush = source.Clone();
            brush.Opacity = opacity;
            return brush;
        }

        private static Geometry Triangle(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(x1, y1), true, true);
                ctx.LineTo(new Point(x2, y2), false, false);
                ctx.LineTo(new Point(x3, y3), false, false);
            }

            geometry.Freeze();
            return geometry;
        }

        private static Geometry Arc(double cx, double cy, double r, double halfAngle, bool left)
        {
            var dx = r * Math.Cos(halfAngle);
            var dy = r * Math.Sin(halfAngle);
            var sign = left ? -1 : 1;

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                // Right arc runs top to bottom, left arc bottom to top; both are clockwise on screen.
                var start = left ? new Point(cx + sign * dx, cy + dy) : new Point(cx + sign * dx, cy - dy);
                var end = left ? new Point(cx + sign * dx, cy - dy) : new Point(cx + sign * dx, cy + dy);
                ctx.BeginFigure(start, false, false);
                ctx.ArcTo(end, new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            }

            geometry.Freeze();
            return geometry;
        }
    }
}
