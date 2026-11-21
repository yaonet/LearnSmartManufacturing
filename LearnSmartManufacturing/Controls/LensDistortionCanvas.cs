using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Vision;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Renders a 9×7 calibration grid under Brown-Conrady radial distortion r' = r(1 + k1*r^2),
    /// visually demonstrating Barrel (&lt;0%) vs Pincushion (&gt;0%) distortion and calibration undistortion.
    /// </summary>
    public class LensDistortionCanvas : FrameworkElement
    {
        public static readonly DependencyProperty ResultProperty = DependencyProperty.Register(
            nameof(Result), typeof(DistortionSimulationResult), typeof(LensDistortionCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Pen RefRectPen = DofPalette.Pen(DofPalette.Subtle, 1.0, dash: 4, gap: 4);
        private static readonly Pen GridOkPen = DofPalette.Pen(DofPalette.Dof, 1.3);
        private static readonly Pen GridWarnPen = DofPalette.Pen(DofPalette.Warn, 1.4);
        private static readonly Pen GridMidPen = DofPalette.Pen(DofPalette.Fov, 1.3);
        private static readonly Brush CenterDot = DofPalette.Ray;

        public LensDistortionCanvas()
        {
            ClipToBounds = true;
        }

        public DistortionSimulationResult? Result
        {
            get => (DistortionSimulationResult?)GetValue(ResultProperty);
            set => SetValue(ResultProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 220 || h < 140 || Result is not { } res)
            {
                return;
            }

            DofText.Draw(dc, this, "镜头径向光学畸变仿真 (r' = r(1 + k₁r²))", 10.2, DofPalette.White, 12, 16, bold: true);
            var badge = res.UndistortApplied
                ? $"已标定校正 · 残留 {res.EdgeShiftPx:F2} px"
                : $"边缘偏移 {res.EdgeShiftPx:F1} px";
            DofText.Draw(dc, this, badge, 9.5,
                res.UndistortApplied || res.EdgeShiftPx <= 3.0 ? DofPalette.Dof : DofPalette.Warn,
                w - 12, 16, TextAlign.Right, bold: true);

            var cx = w * 0.5;
            var cy = h * 0.54;
            var halfW = Math.Min(w * 0.36, (h - 52) * 0.58);
            var halfH = halfW * 0.68;

            // Ideal undistorted reference boundary
            dc.DrawRectangle(null, RefRectPen, new Rect(cx - halfW, cy - halfH, halfW * 2, halfH * 2));

            // Exaggerate visual curvature gently so -5%..+5% industrial distortion is unmistakable at a glance
            var effPct = res.UndistortApplied ? res.DistortionPercent * 0.04 : res.DistortionPercent;
            var k = (effPct / 100.0) * 3.4;

            Point Distort(double nx, double ny)
            {
                var r2 = nx * nx + ny * ny;
                var scale = 1.0 + k * r2;
                return new Point(cx + nx * scale * halfW, cy + ny * scale * halfH);
            }

            var gridPen = res.UndistortApplied || res.EdgeShiftPx <= 2.5
                ? GridOkPen
                : res.EdgeShiftPx <= 10.0 ? GridMidPen : GridWarnPen;

            const int cols = 8;
            const int rows = 6;
            const int segs = 16;

            // Horizontal distorted grid lines
            for (var r = 0; r <= rows; r++)
            {
                var ny = -1.0 + (2.0 * r / rows);
                var prev = Distort(-1.0, ny);
                for (var s = 1; s <= segs; s++)
                {
                    var nx = -1.0 + (2.0 * s / segs);
                    var next = Distort(nx, ny);
                    dc.DrawLine(gridPen, prev, next);
                    prev = next;
                }
            }

            // Vertical distorted grid lines
            for (var c = 0; c <= cols; c++)
            {
                var nx = -1.0 + (2.0 * c / cols);
                var prev = Distort(nx, -1.0);
                for (var s = 1; s <= segs; s++)
                {
                    var ny = -1.0 + (2.0 * s / segs);
                    var next = Distort(nx, ny);
                    dc.DrawLine(gridPen, prev, next);
                    prev = next;
                }
            }

            // Optical center dot
            dc.DrawEllipse(CenterDot, null, new Point(cx, cy), 3.2, 3.2);
            DofText.Draw(dc, this, res.DistortionKindText, 8.8, DofPalette.Muted, cx, h - 7, TextAlign.Center);
        }
    }
}
