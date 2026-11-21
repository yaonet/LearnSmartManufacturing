using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Vision;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Side-by-side optical comparison of a standard entocentric FA lens (converging chief rays -> severe perspective error with ΔZ)
    /// vs an object-space Telecentric lens (parallel chief rays -> invariant magnification independent of ΔZ).
    /// </summary>
    public class TelecentricCompareCanvas : FrameworkElement
    {
        public static readonly DependencyProperty ResultProperty = DependencyProperty.Register(
            nameof(Result), typeof(TelecentricComparisonResult), typeof(TelecentricCompareCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush PaneFill = DofPalette.Solid("#0C0C16");
        private static readonly Brush LensFill = DofPalette.Solid("#89DCEB", 0.24);
        private static readonly Brush TeleLensFill = DofPalette.Solid("#A6E3A1", 0.22);
        private static readonly Brush PartFill = DofPalette.Solid("#CBA6F7", 0.34);
        private static readonly Brush ErrorBandFill = DofPalette.Solid("#F38BA8", 0.28);

        private static readonly Pen PaneBorderPen = DofPalette.Pen(DofPalette.Surface, 1.2);
        private static readonly Pen LensPen = DofPalette.Pen(DofPalette.Fov, 1.5);
        private static readonly Pen TeleLensPen = DofPalette.Pen(DofPalette.Dof, 1.6);
        private static readonly Pen RefPlanePen = DofPalette.Pen(DofPalette.Subtle, 1.0, dash: 4, gap: 4);
        private static readonly Pen FaRayPen = DofPalette.Pen(DofPalette.Warn, 1.5);
        private static readonly Pen FaRayOkPen = DofPalette.Pen(DofPalette.Fov, 1.4);
        private static readonly Pen TeleRayPen = DofPalette.Pen(DofPalette.Dof, 1.6);
        private static readonly Pen PartPen = DofPalette.Pen(DofPalette.Aperture, 1.6);

        public TelecentricCompareCanvas()
        {
            ClipToBounds = true;
        }

        public TelecentricComparisonResult? Result
        {
            get => (TelecentricComparisonResult?)GetValue(ResultProperty);
            set => SetValue(ResultProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 260 || h < 160 || Result is not { } res)
            {
                return;
            }

            const double padX = 10;
            const double gap = 10;
            var paneW = (w - padX * 2 - gap) * 0.5;
            const double paneY = 26;
            var paneH = h - paneY - 10;

            DofText.Draw(dc, this, $"普通 FA 镜头 vs 远心镜头 · 工件高度浮动 ΔZ = {res.DeltaZMm:+0.0;-0.0;0.0} mm",
                10.5, DofPalette.White, padX, 16, bold: true);

            DrawFaPane(dc, new Rect(padX, paneY, paneW, paneH), res);
            DrawTelecentricPane(dc, new Rect(padX + paneW + gap, paneY, paneW, paneH), res);
        }

        private void DrawFaPane(DrawingContext dc, Rect rect, TelecentricComparisonResult res)
        {
            dc.DrawRoundedRectangle(PaneFill, PaneBorderPen, rect, 6, 6);
            dc.PushClip(new RectangleGeometry(rect, 6, 6));
            try
            {
                var cx = rect.Left + rect.Width * 0.5;
                var lensY = rect.Top + 34;
                var refY = rect.Bottom - 44;
                var partHalfW = Math.Min(rect.Width * 0.24, 42);

                // Visual vertical shift proportional to deltaZ / WD (amplified nicely for clarity)
                var zFrac = Math.Clamp(res.DeltaZMm / Math.Max(60.0, res.WorkingDistanceMm), -0.32, 0.32);
                var partY = refY - zFrac * (refY - lensY - 22) * 1.45;
                var partH = 12.0;

                // Small entocentric FA lens at top center
                dc.DrawRoundedRectangle(LensFill, LensPen, new Rect(cx - 18, lensY - 8, 36, 16), 4, 4);
                DofText.Draw(dc, this, "FA 光心", 8, DofPalette.Fov, cx, lensY + 3, TextAlign.Center, bold: true);

                // Nominal calibration plane Z = 0
                dc.DrawLine(RefPlanePen, new Point(rect.Left + 10, refY), new Point(rect.Right - 10, refY));
                DofText.Draw(dc, this, "Z=0", 8, DofPalette.Muted, rect.Left + 12, refY - 3);

                // Chief rays passing from FA optical center through actual moved workpiece edges down to refY
                var rayPen = res.FaWithinTolerance ? FaRayOkPen : FaRayPen;
                var ratio = (refY - lensY) / Math.Max(14.0, partY - lensY);
                var apparentHalfW = Math.Clamp(partHalfW * ratio, 10, rect.Width * 0.45);

                dc.DrawLine(rayPen, new Point(cx, lensY + 6), new Point(cx - apparentHalfW, refY));
                dc.DrawLine(rayPen, new Point(cx, lensY + 6), new Point(cx + apparentHalfW, refY));

                // Highlight apparent perspective error span on Z=0 plane
                if (Math.Abs(apparentHalfW - partHalfW) > 1.5)
                {
                    var minHalf = Math.Min(apparentHalfW, partHalfW);
                    var maxHalf = Math.Max(apparentHalfW, partHalfW);
                    dc.DrawRectangle(ErrorBandFill, null, new Rect(cx - maxHalf, refY - 3, maxHalf - minHalf, 6));
                    dc.DrawRectangle(ErrorBandFill, null, new Rect(cx + minHalf, refY - 3, maxHalf - minHalf, 6));
                }

                // Actual physical workpiece at shifted height
                dc.DrawRoundedRectangle(PartFill, PartPen, new Rect(cx - partHalfW, partY - partH * 0.5, partHalfW * 2, partH), 2, 2);

                DofText.Draw(dc, this, "普通 FA 定焦镜头 (中心透视)", 9.2, DofPalette.Fov, rect.Left + 8, rect.Top + 14, bold: true);
                DofText.Draw(dc, this, $"实测 {res.FaMeasuredMm:F2}mm ({res.FaErrorPercent:+0.00;-0.00;0.00}%)",
                    9, res.FaWithinTolerance ? DofPalette.Dof : DofPalette.Warn, cx, rect.Bottom - 8, TextAlign.Center, bold: true);
            }
            finally
            {
                dc.Pop();
            }
        }

        private void DrawTelecentricPane(DrawingContext dc, Rect rect, TelecentricComparisonResult res)
        {
            dc.DrawRoundedRectangle(PaneFill, PaneBorderPen, rect, 6, 6);
            dc.PushClip(new RectangleGeometry(rect, 6, 6));
            try
            {
                var cx = rect.Left + rect.Width * 0.5;
                var lensY = rect.Top + 34;
                var refY = rect.Bottom - 44;
                var partHalfW = Math.Min(rect.Width * 0.24, 42);

                var zFrac = Math.Clamp(res.DeltaZMm / Math.Max(60.0, res.WorkingDistanceMm), -0.32, 0.32);
                var partY = refY - zFrac * (refY - lensY - 22) * 1.45;
                var partH = 12.0;

                // Large front objective lens (must be >= FOV diameter!)
                var bigLensHalfW = partHalfW + 14;
                dc.DrawRoundedRectangle(TeleLensFill, TeleLensPen, new Rect(cx - bigLensHalfW, lensY - 8, bigLensHalfW * 2, 16), 4, 4);
                DofText.Draw(dc, this, "远心大口径物镜 (Ø ≥ FOV)", 8, DofPalette.Dof, cx, lensY + 3, TextAlign.Center, bold: true);

                // Nominal calibration plane Z = 0
                dc.DrawLine(RefPlanePen, new Point(rect.Left + 10, refY), new Point(rect.Right - 10, refY));
                DofText.Draw(dc, this, "Z=0", 8, DofPalette.Muted, rect.Left + 12, refY - 3);

                // Strictly parallel vertical chief rays!
                dc.DrawLine(TeleRayPen, new Point(cx - partHalfW, lensY + 8), new Point(cx - partHalfW, refY));
                dc.DrawLine(TeleRayPen, new Point(cx + partHalfW, lensY + 8), new Point(cx + partHalfW, refY));

                // Actual physical workpiece at exact same shifted height
                dc.DrawRoundedRectangle(PartFill, PartPen, new Rect(cx - partHalfW, partY - partH * 0.5, partHalfW * 2, partH), 2, 2);

                DofText.Draw(dc, this, "物方远心镜头 (平行光路)", 9.2, DofPalette.Dof, rect.Left + 8, rect.Top + 14, bold: true);
                DofText.Draw(dc, this, $"实测 {res.TcMeasuredMm:F2}mm ({res.TcErrorPercent:+0.00;-0.00;0.00}%)",
                    9, DofPalette.Dof, cx, rect.Bottom - 8, TextAlign.Center, bold: true);
            }
            finally
            {
                dc.Pop();
            }
        }
    }
}
