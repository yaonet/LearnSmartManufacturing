using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Vision;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Dual-plane Nine-Point Hand-Eye Calibration canvas displaying the Pixel Plane (u, v) on the left
    /// and the Mechanical World Plane (X, Y) on the right, with outlier highlighting and click-to-map testing.
    /// </summary>
    public class NinePointCalibrationCanvas : FrameworkElement
    {
        public static readonly DependencyProperty ResultProperty = DependencyProperty.Register(
            nameof(Result), typeof(AffineCalibrationResult), typeof(NinePointCalibrationCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TestPixelUProperty = DependencyProperty.Register(
            nameof(TestPixelU), typeof(double), typeof(NinePointCalibrationCanvas),
            new FrameworkPropertyMetadata(1480.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty TestPixelVProperty = DependencyProperty.Register(
            nameof(TestPixelV), typeof(double), typeof(NinePointCalibrationCanvas),
            new FrameworkPropertyMetadata(820.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        private const double SensorResW = 2448.0;
        private const double SensorResH = 2048.0;

        private static readonly Brush PaneFill = DofPalette.Solid("#0C0C16");
        private static readonly Brush PointFill = DofPalette.Solid("#89DCEB", 0.90);
        private static readonly Brush WorldPtFill = DofPalette.Solid("#A6E3A1", 0.90);
        private static readonly Brush OutlierFill = DofPalette.Solid("#F38BA8", 0.95);
        private static readonly Brush TestFill = DofPalette.Solid("#F9E2AF", 0.95);

        private static readonly Pen PanePen = DofPalette.Pen(DofPalette.Surface, 1.2);
        private static readonly Pen GridPen = DofPalette.Pen(DofPalette.Surface, 1.0, dash: 3, gap: 3);
        private static readonly Pen PixelMeshPen = DofPalette.Pen(DofPalette.Fov, 1.1, dash: 4, gap: 3);
        private static readonly Pen WorldMeshPen = DofPalette.Pen(DofPalette.Dof, 1.1, dash: 4, gap: 3);
        private static readonly Pen ErrorPen = DofPalette.Pen(DofPalette.Warn, 1.8);
        private static readonly Pen TestPen = DofPalette.Pen(DofPalette.Ray, 1.4);
        private static readonly Pen ArrowPen = DofPalette.Pen(DofPalette.Aperture, 1.6);

        private Rect _leftPaneRect;
        private bool _dragging;

        public NinePointCalibrationCanvas()
        {
            ClipToBounds = true;
            Cursor = Cursors.Cross;
        }

        public AffineCalibrationResult? Result
        {
            get => (AffineCalibrationResult?)GetValue(ResultProperty);
            set => SetValue(ResultProperty, value);
        }

        public double TestPixelU
        {
            get => (double)GetValue(TestPixelUProperty);
            set => SetValue(TestPixelUProperty, value);
        }

        public double TestPixelV
        {
            get => (double)GetValue(TestPixelVProperty);
            set => SetValue(TestPixelVProperty, value);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            var pos = e.GetPosition(this);
            if (_leftPaneRect.Contains(pos))
            {
                _dragging = true;
                CaptureMouse();
                UpdateTestPointFromMouse(pos);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging && e.LeftButton == MouseButtonState.Pressed)
            {
                UpdateTestPointFromMouse(e.GetPosition(this));
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_dragging)
            {
                _dragging = false;
                ReleaseMouseCapture();
            }
        }

        private void UpdateTestPointFromMouse(Point pos)
        {
            var pad = 18.0;
            var innerW = Math.Max(10, _leftPaneRect.Width - pad * 2);
            var innerH = Math.Max(10, _leftPaneRect.Height - pad * 2 - 14);
            var u = ((pos.X - (_leftPaneRect.Left + pad)) / innerW) * SensorResW;
            var v = ((pos.Y - (_leftPaneRect.Top + pad + 14)) / innerH) * SensorResH;
            TestPixelU = Math.Round(Math.Clamp(u, 0, SensorResW), 1);
            TestPixelV = Math.Round(Math.Clamp(v, 0, SensorResH), 1);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 300 || h < 170)
            {
                return;
            }

            const double padX = 10;
            const double midGap = 38;
            var paneW = (w - padX * 2 - midGap) * 0.5;
            const double paneY = 26;
            var paneH = h - paneY - 10;

            _leftPaneRect = new Rect(padX, paneY, paneW, paneH);
            var rightPaneRect = new Rect(padX + paneW + midGap, paneY, paneW, paneH);

            DofText.Draw(dc, this, "九点标定双向映射画布 · 点击左侧像素平面任意点实时验证机械坐标", 10.2, DofPalette.White, padX, 16, bold: true);

            if (Result is not { IsValid: true } res)
            {
                dc.DrawRoundedRectangle(PaneFill, PanePen, _leftPaneRect, 6, 6);
                dc.DrawRoundedRectangle(PaneFill, PanePen, rightPaneRect, 6, 6);
                DofText.Draw(dc, this, Result?.ErrorMessage ?? "等待标定数据", 11, DofPalette.Warn, w * 0.5, h * 0.5, TextAlign.Center, bold: true);
                return;
            }

            var status = res.RmseMm <= 0.05
                ? $"RMSE = {res.RmseMm:F4} mm · 精度优秀"
                : $"RMSE = {res.RmseMm:F4} mm · 存在较大残差/异常点";
            DofText.Draw(dc, this, status, 9.8, res.RmseMm <= 0.05 ? DofPalette.Dof : DofPalette.Warn,
                w - padX, 16, TextAlign.Right, bold: true);

            // Center M(2x3) arrow between panes
            var midX = _leftPaneRect.Right + midGap * 0.5;
            var midY = paneY + paneH * 0.5;
            dc.DrawLine(ArrowPen, new Point(_leftPaneRect.Right + 5, midY), new Point(rightPaneRect.Left - 5, midY));
            dc.DrawLine(ArrowPen, new Point(rightPaneRect.Left - 10, midY - 4), new Point(rightPaneRect.Left - 5, midY));
            dc.DrawLine(ArrowPen, new Point(rightPaneRect.Left - 10, midY + 4), new Point(rightPaneRect.Left - 5, midY));
            DofText.Draw(dc, this, "M", 11, DofPalette.Aperture, midX, midY - 8, TextAlign.Center, bold: true);

            DrawPixelPane(dc, _leftPaneRect, res);
            DrawWorldPane(dc, rightPaneRect, res);
        }

        private void DrawPixelPane(DrawingContext dc, Rect rect, AffineCalibrationResult res)
        {
            dc.DrawRoundedRectangle(PaneFill, PanePen, rect, 6, 6);
            const double pad = 18;
            var plot = new Rect(rect.Left + pad, rect.Top + pad + 14, rect.Width - pad * 2, rect.Height - pad * 2 - 14);

            DofText.Draw(dc, this, "像素坐标系 (u →, v ↓ · 2448×2048 px)", 9, DofPalette.Fov, rect.Left + 8, rect.Top + 14, bold: true);

            Point MapPx(double u, double v) => new(
                plot.Left + (u / SensorResW) * plot.Width,
                plot.Top + (v / SensorResH) * plot.Height);

            // Border & center cross
            dc.DrawRectangle(null, GridPen, plot);
            dc.DrawLine(GridPen, new Point(plot.Left, plot.Top + plot.Height * 0.5), new Point(plot.Right, plot.Top + plot.Height * 0.5));
            dc.DrawLine(GridPen, new Point(plot.Left + plot.Width * 0.5, plot.Top), new Point(plot.Left + plot.Width * 0.5, plot.Bottom));

            var pts = res.EvaluatedPoints;
            if (pts.Count == 9)
            {
                DrawGridMesh(dc, pts.Select(p => MapPx(p.PixelU, p.PixelV)).ToArray(), PixelMeshPen);
            }

            foreach (var p in pts)
            {
                var pt = MapPx(p.PixelU, p.PixelV);
                dc.DrawEllipse(p.IsOutlier ? OutlierFill : PointFill, null, pt, p.IsOutlier ? 5.0 : 3.8, p.IsOutlier ? 5.0 : 3.8);
                DofText.Draw(dc, this, p.Index.ToString(), 8.5, p.IsOutlier ? DofPalette.Warn : DofPalette.White,
                    pt.X + 7, pt.Y - 4, bold: p.IsOutlier);
            }

            // Interactive test point crosshair
            var testPt = MapPx(TestPixelU, TestPixelV);
            DrawCrosshair(dc, testPt, TestPen, TestFill);
            DofText.Draw(dc, this, $"({TestPixelU:0}, {TestPixelV:0}) px", 8.5, DofPalette.Ray,
                Math.Clamp(testPt.X, plot.Left + 44, plot.Right - 44), Math.Max(plot.Top + 10, testPt.Y - 10), TextAlign.Center, bold: true);
        }

        private void DrawWorldPane(DrawingContext dc, Rect rect, AffineCalibrationResult res)
        {
            dc.DrawRoundedRectangle(PaneFill, PanePen, rect, 6, 6);
            const double pad = 18;
            var plot = new Rect(rect.Left + pad, rect.Top + pad + 14, rect.Width - pad * 2, rect.Height - pad * 2 - 14);

            DofText.Draw(dc, this, $"机械世界坐标系 (X →, Y ↑ · θ={res.RotationDeg:+0.0;-0.0;0.0}°)", 9, DofPalette.Dof, rect.Left + 8, rect.Top + 14, bold: true);

            var pts = res.EvaluatedPoints;
            var (testWx, testWy) = res.TransformPixelToWorld(TestPixelU, TestPixelV);

            var allX = pts.Select(p => p.WorldX).Append(testWx).ToArray();
            var allY = pts.Select(p => p.WorldY).Append(testWy).ToArray();
            var minX = allX.Min();
            var maxX = allX.Max();
            var minY = allY.Min();
            var maxY = allY.Max();
            var span = Math.Max(10.0, Math.Max(maxX - minX, maxY - minY) * 1.28);
            var midWx = (minX + maxX) * 0.5;
            var midWy = (minY + maxY) * 0.5;
            var scale = Math.Min(plot.Width, plot.Height) / span;
            var pcx = plot.Left + plot.Width * 0.5;
            var pcy = plot.Top + plot.Height * 0.5;

            // Mechanical Y goes UP (inverted from screen Y)
            Point MapWorld(double wx, double wy) => new(
                pcx + (wx - midWx) * scale,
                pcy - (wy - midWy) * scale);

            dc.DrawLine(GridPen, new Point(plot.Left, pcy), new Point(plot.Right, pcy));
            dc.DrawLine(GridPen, new Point(pcx, plot.Top), new Point(pcx, plot.Bottom));

            if (pts.Count == 9)
            {
                DrawGridMesh(dc, pts.Select(p => MapWorld(p.WorldX, p.WorldY)).ToArray(), WorldMeshPen);
            }

            foreach (var p in pts)
            {
                var actual = MapWorld(p.WorldX, p.WorldY);
                if (p.IsOutlier)
                {
                    var pred = MapWorld(p.PredictedX, p.PredictedY);
                    dc.DrawLine(ErrorPen, actual, pred);
                }

                dc.DrawEllipse(p.IsOutlier ? OutlierFill : WorldPtFill, null, actual, p.IsOutlier ? 5.0 : 3.8, p.IsOutlier ? 5.0 : 3.8);
                DofText.Draw(dc, this, p.Index.ToString(), 8.5, p.IsOutlier ? DofPalette.Warn : DofPalette.White,
                    actual.X + 7, actual.Y - 4, bold: p.IsOutlier);
            }

            // Mapped test world point
            var mappedPt = MapWorld(testWx, testWy);
            DrawCrosshair(dc, mappedPt, TestPen, TestFill);
            DofText.Draw(dc, this, $"({testWx:F2}, {testWy:F2}) mm", 8.5, DofPalette.Ray,
                Math.Clamp(mappedPt.X, plot.Left + 52, plot.Right - 52), Math.Max(plot.Top + 10, mappedPt.Y - 10), TextAlign.Center, bold: true);
        }

        private static void DrawGridMesh(DrawingContext dc, Point[] pts, Pen pen)
        {
            // 3 rows
            for (var r = 0; r < 3; r++)
            {
                dc.DrawLine(pen, pts[r * 3], pts[r * 3 + 1]);
                dc.DrawLine(pen, pts[r * 3 + 1], pts[r * 3 + 2]);
            }

            // 3 cols
            for (var c = 0; c < 3; c++)
            {
                dc.DrawLine(pen, pts[c], pts[c + 3]);
                dc.DrawLine(pen, pts[c + 3], pts[c + 6]);
            }
        }

        private static void DrawCrosshair(DrawingContext dc, Point p, Pen pen, Brush fill)
        {
            dc.DrawLine(pen, new Point(p.X - 8, p.Y), new Point(p.X + 8, p.Y));
            dc.DrawLine(pen, new Point(p.X, p.Y - 8), new Point(p.X, p.Y + 8));
            dc.DrawEllipse(fill, null, p, 3.2, 3.2);
        }
    }
}
