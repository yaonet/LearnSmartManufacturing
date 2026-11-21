using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Vision;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Renders the least-squares fitted Rotation Center circle (Cx, Cy, R), sampled rotation arc points,
    /// and a live visual comparison of Rotate-then-Translate compensation (0.000 mm error) vs
    /// naive offset compensation ignoring rotation eccentricity.
    /// </summary>
    public class RotationCenterCanvas : FrameworkElement
    {
        public static readonly DependencyProperty CircleResultProperty = DependencyProperty.Register(
            nameof(CircleResult), typeof(CircleFitResult), typeof(RotationCenterCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AlignmentResultProperty = DependencyProperty.Register(
            nameof(AlignmentResult), typeof(AlignmentCompensationResult), typeof(RotationCenterCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush SampleFill = DofPalette.Solid("#89DCEB", 0.90);
        private static readonly Brush CenterFill = DofPalette.Solid("#F9E2AF", 0.95);
        private static readonly Brush TeachFill = DofPalette.Solid("#A6E3A1", 0.95);
        private static readonly Brush CurrentFill = DofPalette.Solid("#CBA6F7", 0.95);
        private static readonly Brush NaiveFill = DofPalette.Solid("#F38BA8", 0.95);

        private static readonly Pen CirclePen = DofPalette.Pen(DofPalette.Fov, 1.3, dash: 5, gap: 4);
        private static readonly Pen ArmPen = DofPalette.Pen(DofPalette.Ray, 1.2, dash: 3, gap: 3);
        private static readonly Pen RotateArcPen = DofPalette.Pen(DofPalette.Aperture, 1.7);
        private static readonly Pen TranslatePen = DofPalette.Pen(DofPalette.Dof, 1.8);
        private static readonly Pen NaiveErrPen = DofPalette.Pen(DofPalette.Warn, 1.6, dash: 4, gap: 3);
        private static readonly Pen GridPen = DofPalette.Pen(DofPalette.Surface, 0.9);

        public RotationCenterCanvas()
        {
            ClipToBounds = true;
        }

        public CircleFitResult? CircleResult
        {
            get => (CircleFitResult?)GetValue(CircleResultProperty);
            set => SetValue(CircleResultProperty, value);
        }

        public AlignmentCompensationResult? AlignmentResult
        {
            get => (AlignmentCompensationResult?)GetValue(AlignmentResultProperty);
            set => SetValue(AlignmentResultProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 260 || h < 180)
            {
                return;
            }

            DofText.Draw(dc, this, "旋转中心圆拟合 & 先转角度后补 XY 纠偏矢量图", 10.5, DofPalette.White, 12, 16, bold: true);

            if (CircleResult is not { IsValid: true } circle)
            {
                DofText.Draw(dc, this, CircleResult?.ErrorMessage ?? "等待旋转中心采点数据", 11, DofPalette.Warn, w * 0.5, h * 0.5, TextAlign.Center, bold: true);
                return;
            }

            var align = AlignmentResult;
            var badge = $"圆心 ({circle.CenterX:F2}, {circle.CenterY:F2}) · R = {circle.RadiusMm:F2} mm";
            DofText.Draw(dc, this, badge, 9.8, DofPalette.Ray, w - 12, 16, TextAlign.Right, bold: true);

            // Compute uniform bounding box enclosing circle + alignment points
            var xs = new List<double> { circle.CenterX - circle.RadiusMm, circle.CenterX + circle.RadiusMm };
            var ys = new List<double> { circle.CenterY - circle.RadiusMm, circle.CenterY + circle.RadiusMm };
            if (align is not null)
            {
                xs.AddRange([align.TeachX, align.CurrentX, align.RotatedX, align.NaiveFinalX]);
                ys.AddRange([align.TeachY, align.CurrentY, align.RotatedY, align.NaiveFinalY]);
            }

            var minX = xs.Min();
            var maxX = xs.Max();
            var minY = ys.Min();
            var maxY = ys.Max();
            var span = Math.Max(20.0, Math.Max(maxX - minX, maxY - minY) * 1.24);
            var midX = (minX + maxX) * 0.5;
            var midY = (minY + maxY) * 0.5;
            var plotW = w - 36;
            var plotH = h - 54;
            var scale = Math.Min(plotW, plotH) / span;
            var pcx = w * 0.5;
            var pcy = 28 + plotH * 0.5;

            Point Map(double wx, double wy) => new(pcx + (wx - midX) * scale, pcy - (wy - midY) * scale);

            var centerPt = Map(circle.CenterX, circle.CenterY);
            var radPx = circle.RadiusMm * scale;

            // Crosshair grid through rotation center
            dc.DrawLine(GridPen, new Point(16, centerPt.Y), new Point(w - 16, centerPt.Y));
            dc.DrawLine(GridPen, new Point(centerPt.X, 26), new Point(centerPt.X, h - 22));

            // Fitted rotation circle
            dc.DrawEllipse(null, CirclePen, centerPt, radPx, radPx);

            // Rotation calibration sample points along arc
            foreach (var s in circle.Samples)
            {
                var sp = Map(s.X, s.Y);
                dc.DrawLine(ArmPen, centerPt, sp);
                dc.DrawEllipse(SampleFill, null, sp, 3.6, 3.6);
                DofText.Draw(dc, this, s.AngleText, 8, DofPalette.Fov, sp.X + 6, sp.Y - 4);
            }

            // Rotation center marker
            dc.DrawEllipse(CenterFill, null, centerPt, 4.5, 4.5);
            DofText.Draw(dc, this, "旋转中心 O", 8.8, DofPalette.Ray, centerPt.X + 8, centerPt.Y + 12, bold: true);

            // Live Alignment Compensation vectors
            if (align is not null)
            {
                var pCur = Map(align.CurrentX, align.CurrentY);
                var pRot = Map(align.RotatedX, align.RotatedY);
                var pTeach = Map(align.TeachX, align.TeachY);
                var pNaive = Map(align.NaiveFinalX, align.NaiveFinalY);

                // Step 1: Rotation swing Current -> Rotated
                DrawArrow(dc, RotateArcPen, pCur, pRot);
                // Step 2: Correct translation Rotated -> Teach (lands exactly on Teach!)
                DrawArrow(dc, TranslatePen, pRot, pTeach);

                // Naive mistake residual vector Teach -> NaiveFinal
                if (align.NaiveResidualErrorMm > 0.05)
                {
                    dc.DrawLine(NaiveErrPen, pTeach, pNaive);
                    dc.DrawEllipse(NaiveFill, null, pNaive, 4.5, 4.5);
                    DofText.Draw(dc, this, $"误补偏位 ({align.NaiveResidualErrorMm:F2}mm)", 8.5, DofPalette.Warn,
                        Math.Clamp(pNaive.X, 64, w - 64), Math.Max(34, pNaive.Y + 13), TextAlign.Center, bold: true);
                }

                dc.DrawEllipse(CurrentFill, null, pCur, 4.5, 4.5);
                DofText.Draw(dc, this, "来料位", 8.5, DofPalette.Aperture, pCur.X - 6, pCur.Y - 7, TextAlign.Right, bold: true);

                dc.DrawEllipse(SampleFill, null, pRot, 3.8, 3.8);
                DofText.Draw(dc, this, "①转后位", 8.2, DofPalette.Fov, pRot.X + 6, pRot.Y + 10);

                // Teach target crosshair
                dc.DrawLine(TranslatePen, new Point(pTeach.X - 7, pTeach.Y), new Point(pTeach.X + 7, pTeach.Y));
                dc.DrawLine(TranslatePen, new Point(pTeach.X, pTeach.Y - 7), new Point(pTeach.X, pTeach.Y + 7));
                dc.DrawEllipse(TeachFill, null, pTeach, 3.4, 3.4);
                DofText.Draw(dc, this, "②目标示教位", 8.5, DofPalette.Dof, pTeach.X + 8, pTeach.Y - 6, bold: true);
            }

            DofText.Draw(dc, this, "紫: 来料位  →  蓝: ①绕圆心旋转后  →  绿: ②平移精准重合  |  红: 忽略旋转中心的误补偏位",
                8.5, DofPalette.Muted, w * 0.5, h - 7, TextAlign.Center);
        }

        private static void DrawArrow(DrawingContext dc, Pen pen, Point from, Point to)
        {
            dc.DrawLine(pen, from, to);
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var len = double.Hypot(dx, dy);
            if (len < 8) return;
            var ux = dx / len;
            var uy = dy / len;
            dc.DrawLine(pen, new Point(to.X - ux * 6 - uy * 3.5, to.Y - uy * 6 + ux * 3.5), to);
            dc.DrawLine(pen, new Point(to.X - ux * 6 + uy * 3.5, to.Y - uy * 6 - ux * 3.5), to);
        }
    }
}
