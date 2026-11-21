using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Vision;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Side-by-side visual comparison of Global Shutter (motion blur only, vertical geometry preserved)
    /// vs Rolling Shutter (row-by-row sequential readout causing classic horizontal skew / jello distortion).
    /// </summary>
    public class ShutterCompareCanvas : FrameworkElement
    {
        public static readonly DependencyProperty ResultProperty = DependencyProperty.Register(
            nameof(Result), typeof(ShutterSimulationResult), typeof(ShutterCompareCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush PanelFill = DofPalette.Solid("#0C0C16");
        private static readonly Brush PartFill = DofPalette.Solid("#89DCEB", 0.32);
        private static readonly Brush PartBlurFill = DofPalette.Solid("#89DCEB", 0.12);
        private static readonly Brush RollingFill = DofPalette.Solid("#F38BA8", 0.28);
        private static readonly Brush RollingOkFill = DofPalette.Solid("#A6E3A1", 0.26);
        private static readonly Brush HoleFill = DofPalette.Solid("#141420");

        private static readonly Pen PanelBorderPen = DofPalette.Pen(DofPalette.Surface, 1.2);
        private static readonly Pen RefGridPen = DofPalette.Pen(DofPalette.Subtle, 0.9, dash: 3, gap: 4);
        private static readonly Pen GlobalPen = DofPalette.Pen(DofPalette.Fov, 1.8);
        private static readonly Pen RollingErrPen = DofPalette.Pen(DofPalette.Warn, 1.8);
        private static readonly Pen RollingOkPen = DofPalette.Pen(DofPalette.Dof, 1.8);
        private static readonly Pen ScanRowPen = DofPalette.Pen(DofPalette.Ray, 0.9, dash: 3, gap: 3);

        public ShutterCompareCanvas()
        {
            ClipToBounds = true;
        }

        public ShutterSimulationResult? Result
        {
            get => (ShutterSimulationResult?)GetValue(ResultProperty);
            set => SetValue(ResultProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 260 || h < 150 || Result is not { } res)
            {
                return;
            }

            const double padX = 10;
            const double gap = 10;
            var paneW = (w - padX * 2 - gap) * 0.5;
            var paneY = 26.0;
            var paneH = h - paneY - 10;

            DofText.Draw(dc, this, $"全局快门 vs 卷帘快门运动畸变对比 · 线速度 v = {res.LineSpeedMmPerS:0} mm/s →",
                10.5, DofPalette.White, padX, 16, bold: true);

            var leftRect = new Rect(padX, paneY, paneW, paneH);
            var rightRect = new Rect(padX + paneW + gap, paneY, paneW, paneH);

            DrawGlobalPane(dc, leftRect, res);
            DrawRollingPane(dc, rightRect, res);
        }

        private void DrawGlobalPane(DrawingContext dc, Rect rect, ShutterSimulationResult res)
        {
            dc.DrawRoundedRectangle(PanelFill, PanelBorderPen, rect, 6, 6);
            dc.PushClip(new RectangleGeometry(rect, 6, 6));
            try
            {
                var cx = rect.Left + rect.Width * 0.5;
                var cy = rect.Top + rect.Height * 0.53;
                var partW = Math.Min(rect.Width * 0.28, 54);
                var partH = Math.Min(rect.Height * 0.56, 86);

                // Vertical reference hairline
                dc.DrawLine(RefGridPen, new Point(cx, rect.Top + 22), new Point(cx, rect.Bottom - 20));

                // Horizontal motion blur trail proportional to exposure blur
                var blurShift = Math.Clamp(res.MotionBlurPx * 2.2, 0, rect.Width * 0.22);
                if (blurShift > 1.5)
                {
                    for (var i = 1; i <= 3; i++)
                    {
                        var offset = (blurShift / 3.0) * i;
                        dc.DrawRoundedRectangle(PartBlurFill, null,
                            new Rect(cx - partW * 0.5 - offset, cy - partH * 0.5, partW, partH), 3, 3);
                    }
                }

                // Crisp upright vertical bracket (0 skew!)
                var body = new Rect(cx - partW * 0.5, cy - partH * 0.5, partW, partH);
                dc.DrawRoundedRectangle(PartFill, GlobalPen, body, 3, 3);
                dc.DrawEllipse(HoleFill, GlobalPen, new Point(cx, cy), partW * 0.24, partW * 0.24);

                DofText.Draw(dc, this, "Global 全局快门 (全阵同步曝光)", 9.5, DofPalette.Fov, rect.Left + 8, rect.Top + 14, bold: true);
                DofText.Draw(dc, this, $"斜切 Skew = 0 px · 拖影 {res.MotionBlurPx:F2} px",
                    8.8, res.MotionBlurPx <= 1.0 ? DofPalette.Dof : DofPalette.Ray,
                    cx, rect.Bottom - 7, TextAlign.Center, bold: true);
            }
            finally
            {
                dc.Pop();
            }
        }

        private void DrawRollingPane(DrawingContext dc, Rect rect, ShutterSimulationResult res)
        {
            dc.DrawRoundedRectangle(PanelFill, PanelBorderPen, rect, 6, 6);
            dc.PushClip(new RectangleGeometry(rect, 6, 6));
            try
            {
                var cx = rect.Left + rect.Width * 0.5;
                var cy = rect.Top + rect.Height * 0.53;
                var partW = Math.Min(rect.Width * 0.28, 54);
                var partH = Math.Min(rect.Height * 0.56, 86);

                // Vertical ideal reference hairline
                dc.DrawLine(RefGridPen, new Point(cx, rect.Top + 22), new Point(cx, rect.Bottom - 20));

                // Row-by-row sequential scan indicator lines
                for (var r = 0; r < 5; r++)
                {
                    var ry = cy - partH * 0.5 + (partH / 4.0) * r;
                    dc.DrawLine(ScanRowPen, new Point(rect.Left + 8, ry), new Point(rect.Left + 24, ry));
                }

                DofText.Draw(dc, this, "逐行", 7.8, DofPalette.Ray, rect.Left + 16, cy - partH * 0.5 - 4, TextAlign.Center);

                // Sheared parallelogram obeying tan(theta)
                var angleRad = Math.Clamp(res.RollingSkewAngleDeg, 0, 58) * Math.PI / 180.0;
                var halfShearX = Math.Clamp(Math.Tan(angleRad) * (partH * 0.5), 0, rect.Width * 0.34);

                var topY = cy - partH * 0.5;
                var botY = cy + partH * 0.5;
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    // Top captured earlier (further left), bottom captured later (further right as object moves right!)
                    ctx.BeginFigure(new Point(cx - partW * 0.5 - halfShearX, topY), true, true);
                    ctx.LineTo(new Point(cx + partW * 0.5 - halfShearX, topY), true, false);
                    ctx.LineTo(new Point(cx + partW * 0.5 + halfShearX, botY), true, false);
                    ctx.LineTo(new Point(cx - partW * 0.5 + halfShearX, botY), true, false);
                }

                geo.Freeze();
                var fill = res.IsRollingAcceptable ? RollingOkFill : RollingFill;
                var pen = res.IsRollingAcceptable ? RollingOkPen : RollingErrPen;
                dc.DrawGeometry(fill, pen, geo);

                // Sheared center hole using SkewTransform
                dc.PushTransform(new SkewTransform(Math.Clamp(res.RollingSkewAngleDeg, 0, 52), 0, cx, cy));
                dc.DrawEllipse(HoleFill, pen, new Point(cx, cy), partW * 0.24, partW * 0.24);
                dc.Pop();

                DofText.Draw(dc, this, $"Rolling 卷帘快门 (读出 {res.RollingReadoutMs:F1}ms)", 9.5,
                    res.IsRollingAcceptable ? DofPalette.Dof : DofPalette.Warn, rect.Left + 8, rect.Top + 14, bold: true);
                DofText.Draw(dc, this, $"果冻斜切 {res.RollingSkewPx:F1} px ({res.RollingSkewAngleDeg:F1}°)",
                    8.8, res.IsRollingAcceptable ? DofPalette.Dof : DofPalette.Warn,
                    cx, rect.Bottom - 7, TextAlign.Center, bold: true);
            }
            finally
            {
                dc.Pop();
            }
        }
    }
}
