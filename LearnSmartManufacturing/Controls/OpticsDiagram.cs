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
        private static readonly Brush LowBrush = CreateBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80));
        private static readonly Brush SensorBrush = CreateBrush(SensorColor);
        private static readonly Brush SensorFillBrush = CreateBrush(SensorColor, 0.12);
        private static readonly Brush RayBrush = CreateBrush(RayColor);
        private static readonly Brush RayFillBrush = CreateBrush(RayColor, 0.12);
        private static readonly Brush CenterBrush = CreateBrush(CenterColor);
        private static readonly Pen AxisPen = CreatePen(LowBrush, 1, dash: [4, 4]);
        private static readonly Pen SensorGuidePen = CreatePen(LowBrush, 1);
        private static readonly Pen ObjectGuidePen = CreatePen(LowBrush, 1.5, dash: [2, 2]);
        private static readonly Pen ArcPen = CreatePen(CenterBrush, 1.6);
        private static readonly Pen RayPen = CreatePen(RayBrush, 2);
        private static readonly Pen SensorPen = CreatePen(SensorBrush, 5, roundCaps: true);
        private static readonly Pen FovPen = CreatePen(RayBrush, 5, roundCaps: true);
        private static readonly Pen SensorDimPen = CreatePen(SensorBrush, 1.25);
        private static readonly Pen RayDimPen = CreatePen(RayBrush, 1.25);
        private static readonly Typeface TextFace = new("Microsoft YaHei UI");
        private static readonly Typeface MonoFace = new("Consolas");

        // Matches the previous 0.22-per-frame ease at 60 FPS across arbitrary display refresh rates.
        private static readonly double EaseRatePerSecond = -60.0 * Math.Log(1.0 - 0.22);

        private double _shownFocalLength = 16;
        private double _shownWorkingDistance = 300;
        private bool _animating;
        private TimeSpan _lastRenderTime;

        private Brush? _cachedPanel;
        private Brush? _cachedVariant;
        private Brush? _panelAlpha90;
        private Brush? _panelAlpha85;
        private Pen? _lensBorderPen;
        private Pen? _centerRingPen;

        public OpticsDiagram()
        {
            Unloaded += (_, _) => StopAnimation();
        }

        private static Brush CreateBrush(Color color, double opacity = 1)
        {
            var brush = new SolidColorBrush(color) { Opacity = opacity };
            brush.Freeze();
            return brush;
        }

        private static Pen CreatePen(Brush brush, double thickness, double[]? dash = null, bool roundCaps = false)
        {
            var pen = new Pen(brush, thickness);
            if (dash is not null)
            {
                pen.DashStyle = new DashStyle(dash, 0);
            }

            if (roundCaps)
            {
                pen.StartLineCap = PenLineCap.Round;
                pen.EndLineCap = PenLineCap.Round;
            }

            pen.Freeze();
            return pen;
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
                _lastRenderTime = TimeSpan.Zero;
                CompositionTarget.Rendering += OnRendering;
            }
        }

        private void StopAnimation()
        {
            if (_animating)
            {
                CompositionTarget.Rendering -= OnRendering;
                _animating = false;
                _lastRenderTime = TimeSpan.Zero;
            }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            double dt;
            if (e is RenderingEventArgs re)
            {
                if (_lastRenderTime == TimeSpan.Zero)
                {
                    _lastRenderTime = re.RenderingTime;
                    dt = 1.0 / 60.0;
                }
                else
                {
                    dt = (re.RenderingTime - _lastRenderTime).TotalSeconds;
                    if (dt <= 0)
                    {
                        return;
                    }

                    _lastRenderTime = re.RenderingTime;
                    dt = Math.Min(dt, 0.05);
                }
            }
            else
            {
                dt = 1.0 / 60.0;
            }

            var df = FocalLength - _shownFocalLength;
            var dwd = WorkingDistance - _shownWorkingDistance;

            if (Math.Abs(df) > 0.04 || Math.Abs(dwd) > 0.1)
            {
                var alpha = 1.0 - Math.Exp(-EaseRatePerSecond * dt);
                _shownFocalLength += df * alpha;
                _shownWorkingDistance += dwd * alpha;
            }
            else
            {
                _shownFocalLength = FocalLength;
                _shownWorkingDistance = WorkingDistance;
                StopAnimation();
            }

            InvalidateVisual();
        }

        private void EnsureThemeCache(Brush panel, Brush variant)
        {
            if (!ReferenceEquals(panel, _cachedPanel))
            {
                _cachedPanel = panel;
                _panelAlpha90 = WithOpacity(panel, 0.9);
                _panelAlpha85 = WithOpacity(panel, 0.85);
                _centerRingPen = CreatePen(panel, 1.5);
            }

            if (!ReferenceEquals(variant, _cachedVariant))
            {
                _cachedVariant = variant;
                _lensBorderPen = CreatePen(variant, 1.5);
            }
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
            EnsureThemeCache(panel, variant);

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

            // Optical axis and the vertical reference line through O
            dc.DrawLine(AxisPen, new Point(20, cy), new Point(w - 20, cy));
            dc.DrawLine(AxisPen, new Point(ox, 22), new Point(ox, h - 42));

            // Similar triangle fills
            dc.DrawGeometry(SensorFillBrush, null, Triangle(ox, cy, sx, cy - effSensorHalfH, sx, cy + effSensorHalfH));
            dc.DrawGeometry(RayFillBrush, null, Triangle(ox, cy, objX, cy - objHalfH, objX, cy + objHalfH));

            // FOV angle arcs (vertically opposite angles)
            var arcRight = Math.Min(44, wDist * 0.32);
            var arcLeft = Math.Min(34, sDist * 0.55);
            var halfAngle = Math.Atan2(objHalfH, wDist);
            dc.DrawGeometry(null, ArcPen, Arc(ox, cy, arcRight, halfAngle, left: false));
            dc.DrawGeometry(null, ArcPen, Arc(ox, cy, arcLeft, halfAngle, left: true));

            var angleLabel = $"θ = {fovAngleDeg:F1}°";
            var angleText = Text(angleLabel, MonoFace, 12, CenterBrush);
            var angleX = ox + arcRight + 8;
            var angleY = cy - 11;
            dc.DrawRoundedRectangle(_panelAlpha90, null,
                new Rect(angleX - 5, angleY - 9, angleText.Width + 10, 18), 5, 5);
            dc.DrawText(angleText, new Point(angleX, angleY - angleText.Height / 2));

            // Chief rays
            dc.DrawLine(RayPen, new Point(sx, cy - effSensorHalfH), new Point(objX, cy + objHalfH));
            dc.DrawLine(RayPen, new Point(sx, cy + effSensorHalfH), new Point(objX, cy - objHalfH));

            // Lens and optical center O
            var lensHalfH = Math.Max(46, effSensorHalfH + 22);
            dc.DrawEllipse(_panelAlpha85, _lensBorderPen, new Point(ox, cy), 9, lensHalfH);
            dc.DrawEllipse(CenterBrush, _centerRingPen, new Point(ox, cy), 4.5, 4.5);
            DrawText(dc, "镜头光心 O", TextFace, 12, onSurface, ox, cy - lensHalfH - 8, HAlign.Center, VAlign.Bottom);

            // Sensor plane
            dc.DrawLine(SensorGuidePen, new Point(sx, cy - effSensorHalfH - 22), new Point(sx, h - 38));
            dc.DrawLine(SensorPen, new Point(sx, cy - effSensorHalfH), new Point(sx, cy + effSensorHalfH));
            dc.DrawEllipse(SensorBrush, null, new Point(sx, cy - effSensorHalfH), 3.5, 3.5);
            dc.DrawEllipse(SensorBrush, null, new Point(sx, cy + effSensorHalfH), 3.5, 3.5);
            DrawText(dc, "[Sensor]", TextFace, 12, SensorBrush, sx, cy - effSensorHalfH - 20, HAlign.Center, VAlign.Bottom);
            DrawText(dc, $"{sw:F2} mm", MonoFace, 11, onSurface, sx, cy - effSensorHalfH - 6, HAlign.Center, VAlign.Bottom);

            // Object plane and the highlighted FOV
            dc.DrawLine(ObjectGuidePen, new Point(objX, 20), new Point(objX, h - 38));
            dc.DrawLine(FovPen, new Point(objX, cy - objHalfH), new Point(objX, cy + objHalfH));
            dc.DrawEllipse(RayBrush, null, new Point(objX, cy - objHalfH), 4, 4);
            dc.DrawEllipse(RayBrush, null, new Point(objX, cy + objHalfH), 4, 4);
            DrawText(dc, $"被测物视野 FOV = {fovWidth:F1} mm", TextFace, 12, RayBrush,
                objX - 8, Math.Max(22, cy - objHalfH - 6), HAlign.Right, VAlign.Bottom);

            // Dimension lines for f and WD
            var dimY = h - 22;
            DrawDimensionLine(dc, sx, ox, dimY, $"f = {Math.Round(f)} mm", SensorBrush, SensorDimPen, panel);
            DrawDimensionLine(dc, ox, objX, dimY, $"WD = {Math.Round(wd)} mm", RayBrush, RayDimPen, panel);

            DrawText(dc, $"相似比: FOV / Ws = WD / f = {wd / f:F1}", MonoFace, 12, variant, 16, 14, HAlign.Left, VAlign.Top);
        }

        private void DrawDimensionLine(DrawingContext dc, double x1, double x2, double y, string label, Brush color, Pen pen, Brush pillBackground)
        {
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

        private static Brush WithOpacity(Brush source, double opacity)
        {
            var brush = source.Clone();
            brush.Opacity = opacity;
            brush.Freeze();
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
