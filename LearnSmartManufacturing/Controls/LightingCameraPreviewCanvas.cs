using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Vision;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Simulates the 8-bit Monochrome Industrial Camera sensor image (including dark-field inversion,
    /// glare saturation bloom, and silhouette backlight) plus a real-time horizontal 0..255 grayscale scanline profile.
    /// </summary>
    public class LightingCameraPreviewCanvas : FrameworkElement
    {
        public static readonly DependencyProperty ResultProperty = DependencyProperty.Register(
            nameof(Result), typeof(LightingSimulationResult), typeof(LightingCameraPreviewCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush SensorBg = DofPalette.Solid("#0A0A12");
        private static readonly Pen BorderPen = DofPalette.Pen(DofPalette.Surface, 1.2);
        private static readonly Pen ScanlineRefPen = DofPalette.Pen(DofPalette.Fov, 1.0, dash: 4, gap: 4);
        private static readonly Pen ProfilePen = DofPalette.Pen(DofPalette.Fov, 1.7);
        private static readonly Pen SatPen = DofPalette.Pen(DofPalette.Warn, 1.0, dash: 3, gap: 3);
        private static readonly Pen GridPen = DofPalette.Pen(DofPalette.Surface, 0.9);

        public LightingCameraPreviewCanvas()
        {
            ClipToBounds = true;
        }

        public LightingSimulationResult? Result
        {
            get => (LightingSimulationResult?)GetValue(ResultProperty);
            set => SetValue(ResultProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 200 || h < 200)
            {
                return;
            }

            if (Result is not { } res)
            {
                return;
            }

            const double padX = 12;
            DofText.Draw(dc, this, "黑白工业相机实时成像模拟 (8-bit Mono)", 10.5, DofPalette.White, padX, 16, bold: true);
            DofText.Draw(dc, this, $"ΔGray = {Math.Abs(res.FeatureGray - res.BackgroundGray)}", 10,
                res.IsOptimalSetup ? DofPalette.Dof : DofPalette.Warn, w - padX, 16, TextAlign.Right, bold: true);

            // Top 62%: simulated monochrome camera frame
            var sensorTop = 24.0;
            var sensorH = Math.Max(90, (h - 40) * 0.60);
            var sensorRect = new Rect(padX, sensorTop, w - padX * 2, sensorH);
            DrawMonochromeSensorView(dc, sensorRect, res);

            // Bottom 38%: 0..255 horizontal scanline grayscale profile
            var profTop = sensorRect.Bottom + 18;
            var profH = Math.Max(52, h - profTop - 14);
            var profRect = new Rect(padX + 28, profTop, w - padX * 2 - 32, profH);
            DrawScanlineProfile(dc, profRect, res);
        }

        private void DrawMonochromeSensorView(DrawingContext dc, Rect rect, LightingSimulationResult res)
        {
            dc.PushClip(new RectangleGeometry(rect, 6, 6));
            try
            {
                var bgByte = (byte)Math.Clamp(res.BackgroundGray, 0, 255);
                var featByte = (byte)Math.Clamp(res.FeatureGray, 0, 255);
                var bgBrush = GrayBrush(bgByte);
                var featBrush = GrayBrush(featByte);
                var featPen = new Pen(featBrush, 2.4);

                dc.DrawRectangle(SensorBg, null, rect);
                var cx = rect.Left + rect.Width * 0.5;
                var cy = rect.Top + rect.Height * 0.5;

                var p = res.Parameters;
                if (p.LightType == LightGeometryType.Backlight)
                {
                    // Pure bright field background + opaque silhouette
                    dc.DrawRectangle(GrayBrush((byte)Math.Clamp(p.Workpiece == WorkpieceType.StampedContour ? res.BackgroundGray : 244, 0, 255)), null, rect);
                    var silBrush = GrayBrush(10);
                    if (p.Workpiece == WorkpieceType.StampedContour)
                    {
                        DrawStampedPartSilhouette(dc, cx, cy, Math.Min(rect.Width, rect.Height) * 0.34, silBrush, GrayBrush(244));
                    }
                    else
                    {
                        dc.DrawRoundedRectangle(silBrush, null, new Rect(cx - rect.Width * 0.34, cy - rect.Height * 0.30, rect.Width * 0.68, rect.Height * 0.60), 6, 6);
                        DofText.Draw(dc, this, "背光下仅见轮廓剪影，正面缺陷不可见", 9.5, DofPalette.Warn, cx, cy + 4, TextAlign.Center, bold: true);
                    }
                }
                else
                {
                    // Reflective surface fill
                    dc.DrawRectangle(bgBrush, null, rect);

                    switch (p.Workpiece)
                    {
                        case WorkpieceType.MetalMirrorScratch:
                            // Diagonal fine scratches
                            dc.DrawLine(new Pen(featBrush, 2.6), new Point(cx - 52, cy - 28), new Point(cx + 22, cy + 32));
                            dc.DrawLine(new Pen(featBrush, 1.8), new Point(cx + 12, cy - 30), new Point(cx + 58, cy + 18));
                            dc.DrawLine(new Pen(featBrush, 1.4), new Point(cx - 24, cy + 16), new Point(cx + 42, cy - 10));
                            break;

                        case WorkpieceType.GreenPcbSilkscreen:
                            // PCB copper pads + silkscreen characters + red fiducial
                            var padBrush = GrayBrush((byte)Math.Clamp((int)(res.FeatureGray * 0.88), 0, 255));
                            var redMarkGray = (byte)Math.Clamp((int)(LightingEngine.GetSpectralReflectance(TargetSurfaceColor.Red, p.Wavelength) * 235), 12, 245);
                            dc.DrawRoundedRectangle(padBrush, null, new Rect(cx - 88, cy - 26, 22, 52), 3, 3);
                            dc.DrawRoundedRectangle(padBrush, null, new Rect(cx + 66, cy - 26, 22, 52), 3, 3);
                            dc.DrawEllipse(GrayBrush(redMarkGray), null, new Point(cx + 44, cy - 24), 9, 9);
                            DofText.Draw(dc, this, "IC-U12", 18, featBrush, cx - 10, cy + 7, TextAlign.Center, bold: true);
                            break;

                        case WorkpieceType.StampedContour:
                            DrawStampedPartSilhouette(dc, cx, cy, Math.Min(rect.Width, rect.Height) * 0.34, featBrush, bgBrush);
                            break;

                        case WorkpieceType.CurvedSpecularMetal:
                            // Cylindrical brightness rolloff + engraved marking
                            var cylGrad = new LinearGradientBrush(
                                Color.FromRgb((byte)(bgByte * 0.55), (byte)(bgByte * 0.55), (byte)(bgByte * 0.55)),
                                Color.FromRgb(bgByte, bgByte, bgByte),
                                new Point(0, 0), new Point(0, 0.5))
                            {
                                SpreadMethod = GradientSpreadMethod.Reflect,
                            };
                            dc.DrawRectangle(cylGrad, null, rect);
                            DofText.Draw(dc, this, "LOT-26A", 18, featBrush, cx, cy + 7, TextAlign.Center, bold: true);
                            break;

                        case WorkpieceType.WaferMicroPit:
                            // Wafer grid lines + micro-pits
                            var gridBrush = GrayBrush((byte)Math.Clamp(bgByte - 28, 0, 255));
                            var gp = new Pen(gridBrush, 1.0);
                            dc.DrawLine(gp, new Point(rect.Left, cy - 22), new Point(rect.Right, cy - 22));
                            dc.DrawLine(gp, new Point(rect.Left, cy + 22), new Point(rect.Right, cy + 22));
                            dc.DrawLine(gp, new Point(cx - 48, rect.Top), new Point(cx - 48, rect.Bottom));
                            dc.DrawLine(gp, new Point(cx + 48, rect.Top), new Point(cx + 48, rect.Bottom));
                            dc.DrawEllipse(featBrush, null, new Point(cx - 14, cy - 2), 7.5, 7.5);
                            dc.DrawEllipse(featBrush, null, new Point(cx + 34, cy + 8), 5.5, 5.5);
                            break;

                        case WorkpieceType.GlossyFilmLabel:
                            DofText.Draw(dc, this, "SN-8042", 18, featBrush, cx, cy - 4, TextAlign.Center, bold: true);
                            // Barcode stripes
                            double bx = cx - 42;
                            double[] bars = [4, 2, 5, 2, 3, 6, 2, 4, 2, 5];
                            for (var i = 0; i < bars.Length; i++)
                            {
                                if (i % 2 == 0)
                                {
                                    dc.DrawRectangle(featBrush, null, new Rect(bx, cy + 10, bars[i] * 2.1, 18));
                                }

                                bx += bars[i] * 2.6;
                            }

                            break;
                    }

                    // Specular Glare hotspot overlay (washes out underlying features when glareRisk is high!)
                    if (res.GlareRiskPercent > 15)
                    {
                        var alpha = (byte)Math.Clamp(res.GlareRiskPercent / 100.0 * 235, 0, 240);
                        var glareGrad = new RadialGradientBrush(
                            Color.FromArgb(alpha, 255, 255, 255),
                            Color.FromArgb(0, 255, 255, 255));
                        dc.DrawEllipse(glareGrad, null, new Point(cx + rect.Width * 0.08, cy - rect.Height * 0.05),
                            rect.Width * 0.34, rect.Height * 0.42);
                        if (res.GlareRiskPercent >= 60)
                        {
                            DofText.Draw(dc, this, "⚠ 过曝炫光区 (255)", 9, DofPalette.Warn, cx + 18, cy + 4, TextAlign.Center, bold: true);
                        }
                    }

                    _ = featPen;
                }

                // Horizontal scanline indicator across sensor midline
                dc.DrawLine(ScanlineRefPen, new Point(rect.Left, cy), new Point(rect.Right, cy));
                DofText.Draw(dc, this, "Scanline Y=50%", 8.5, DofPalette.Fov, rect.Left + 6, cy - 4);
            }
            finally
            {
                dc.Pop();
            }

            dc.DrawRoundedRectangle(null, BorderPen, rect, 6, 6);
        }

        private void DrawScanlineProfile(DrawingContext dc, Rect rect, LightingSimulationResult res)
        {
            DofText.Draw(dc, this, "中心扫描线灰度剖面 (0~255)", 9.5, DofPalette.Muted, rect.Left, rect.Top - 4, bold: true);
            DofText.Draw(dc, this, $"背景 {res.BackgroundGray} / 特征 {res.FeatureGray}", 9, DofPalette.Fov, rect.Right, rect.Top - 4, TextAlign.Right, bold: true);

            double MapY(double gray) => rect.Bottom - Math.Clamp(gray / 255.0, 0, 1) * rect.Height;

            // Grid lines (255, 128, 0)
            dc.DrawLine(SatPen, new Point(rect.Left, MapY(255)), new Point(rect.Right, MapY(255)));
            dc.DrawLine(GridPen, new Point(rect.Left, MapY(128)), new Point(rect.Right, MapY(128)));
            dc.DrawLine(GridPen, new Point(rect.Left, MapY(0)), new Point(rect.Right, MapY(0)));

            DofText.Draw(dc, this, "255", 8, DofPalette.Warn, rect.Left - 4, MapY(255) + 3, TextAlign.Right);
            DofText.Draw(dc, this, "128", 8, DofPalette.Muted, rect.Left - 4, MapY(128) + 3, TextAlign.Right);
            DofText.Draw(dc, this, "0", 8, DofPalette.Muted, rect.Left - 4, MapY(0) + 3, TextAlign.Right);

            var data = res.ScanlineProfile;
            if (data.Count < 2)
            {
                return;
            }

            var dx = rect.Width / (data.Count - 1);
            var prev = new Point(rect.Left, MapY(data[0]));
            for (var i = 1; i < data.Count; i++)
            {
                var next = new Point(rect.Left + i * dx, MapY(data[i]));
                dc.DrawLine(ProfilePen, prev, next);
                prev = next;
            }
        }

        private static void DrawStampedPartSilhouette(DrawingContext dc, double cx, double cy, double r, Brush bodyBrush, Brush holeBrush)
        {
            dc.DrawRoundedRectangle(bodyBrush, null, new Rect(cx - r * 1.4, cy - r * 0.85, r * 2.8, r * 1.7), 8, 8);
            dc.DrawEllipse(holeBrush, null, new Point(cx - r * 0.65, cy), r * 0.34, r * 0.34);
            dc.DrawEllipse(holeBrush, null, new Point(cx + r * 0.65, cy), r * 0.34, r * 0.34);
        }

        private static Brush GrayBrush(byte v)
        {
            var b = new SolidColorBrush(Color.FromRgb(v, v, v));
            b.Freeze();
            return b;
        }
    }
}
