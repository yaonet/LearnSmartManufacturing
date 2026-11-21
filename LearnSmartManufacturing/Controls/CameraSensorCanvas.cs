using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Vision;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Visualizes the recommended CMOS sensor active area, workpiece FOV footprint + safety margin,
    /// and lens image-circle coverage (with exact corner vignetting overlay when lens circle &lt; sensor diagonal).
    /// Automatically switches to a Line-Scan (1×N strip) diagram when required resolution exceeds standard area-scan cameras.
    /// </summary>
    public class CameraSensorCanvas : FrameworkElement
    {
        public static readonly DependencyProperty VisualDataProperty = DependencyProperty.Register(
            nameof(VisualData), typeof(CameraSelectionVisualData), typeof(CameraSensorCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush SensorFill = DofPalette.Solid("#1E1E2E", 0.95);
        private static readonly Brush TargetFill = DofPalette.Solid("#89DCEB", 0.20);
        private static readonly Brush MarginFill = DofPalette.Solid("#A6E3A1", 0.08);
        private static readonly Brush VignetteFill = DofPalette.Solid("#F38BA8", 0.34);
        private static readonly Brush LensOkFill = DofPalette.Solid("#A6E3A1", 0.05);
        private static readonly Brush LineScanStripFill = DofPalette.Solid("#CBA6F7", 0.25);

        private static readonly Pen SensorPen = DofPalette.Pen(DofPalette.TextMain, 1.5);
        private static readonly Pen TargetPen = DofPalette.Pen(DofPalette.Fov, 1.6);
        private static readonly Pen MarginPen = DofPalette.Pen(DofPalette.Dof, 1.2, dash: 4, gap: 3);
        private static readonly Pen LensOkPen = DofPalette.Pen(DofPalette.Dof, 1.4, dash: 5, gap: 4);
        private static readonly Pen LensWarnPen = DofPalette.Pen(DofPalette.Warn, 1.8, dash: 5, gap: 3);
        private static readonly Pen LineScanPen = DofPalette.Pen(DofPalette.Aperture, 1.8);
        private static readonly Pen ArrowPen = DofPalette.Pen(DofPalette.Ray, 1.6);

        public CameraSensorCanvas()
        {
            ClipToBounds = true;
        }

        public CameraSelectionVisualData? VisualData
        {
            get => (CameraSelectionVisualData?)GetValue(VisualDataProperty);
            set => SetValue(VisualDataProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 240 || h < 150)
            {
                return;
            }

            if (VisualData is not { IsValid: true } data)
            {
                DofText.Draw(dc, this, "请输入大于 0 的像素精度与目标长宽，实时预览传感器靶面与镜头像圆匹配",
                    11, DofPalette.Muted, w * 0.5, h * 0.5 + 4, TextAlign.Center);
                return;
            }

            if (data.ExceedsAreaScan || data.RecommendedCamera is null)
            {
                DrawLineScanFallback(dc, w, h, data);
                return;
            }

            var cam = data.RecommendedCamera;
            var cx = w * 0.5;
            var cy = h * 0.53;

            // Header bar
            DofText.Draw(dc, this, $"传感器靶面与镜头像圆匹配 · {cam.SensorModel} ({cam.SensorFormat})",
                10.5, DofPalette.White, 12, 16, bold: true);

            var statusText = data.HasVignetting
                ? $"⚠ 暗角截断！像圆 Ø{data.SelectedLensCircle.DiameterMm:F1}mm < 对角线 Ø{cam.DiagonalMm:F1}mm"
                : $"✓ 像圆全覆盖 · 像素利用率 {data.SensorUtilizationPercent:F1}%{(data.IsRotatedMatch ? " (90° 旋转适配)" : string.Empty)}";
            DofText.Draw(dc, this, statusText, 9.8, data.HasVignetting ? DofPalette.Warn : DofPalette.Dof,
                w - 12, 16, TextAlign.Right, bold: true);

            // Scale millimeters to canvas pixels so both sensor diagonal and lens circle fit comfortably
            var maxExtentMm = Math.Max(cam.DiagonalMm, Math.Min(data.SelectedLensCircle.DiameterMm, cam.DiagonalMm * 1.45));
            var availR = Math.Min(w * 0.36, (h - 54) * 0.44);
            var pxPerMm = availR / Math.Max(1.0, maxExtentMm * 0.5);

            var sensW = cam.SensorWidthMm * pxPerMm;
            var sensH = cam.SensorHeightMm * pxPerMm;
            var sensorRect = new Rect(cx - sensW * 0.5, cy - sensH * 0.5, sensW, sensH);
            var lensR = (data.SelectedLensCircle.DiameterMm * 0.5) * pxPerMm;

            // 1. Draw Lens Image Circle
            var lensGeo = new EllipseGeometry(new Point(cx, cy), lensR, lensR);
            lensGeo.Freeze();
            dc.DrawGeometry(data.HasVignetting ? null : LensOkFill, data.HasVignetting ? LensWarnPen : LensOkPen, lensGeo);

            // 2. Draw CMOS Sensor Active Area
            dc.DrawRectangle(SensorFill, SensorPen, sensorRect);

            // 3. Draw Target + Margin footprint scaled proportionally inside sensor pixel grid
            var effPxX = data.IsRotatedMatch ? data.RequiredNy : data.RequiredNx;
            var effPxY = data.IsRotatedMatch ? data.RequiredNx : data.RequiredNy;
            var fracX = Math.Clamp(effPxX / (double)cam.Width, 0.05, 1.0);
            var fracY = Math.Clamp(effPxY / (double)cam.Height, 0.05, 1.0);
            var marginW = sensW * fracX;
            var marginH = sensH * fracY;
            var marginRect = new Rect(cx - marginW * 0.5, cy - marginH * 0.5, marginW, marginH);

            var rawScale = 1.0 / (1.0 + Math.Max(0, data.FovMarginPercent) / 100.0);
            var rawW = marginW * rawScale;
            var rawH = marginH * rawScale;
            var rawRect = new Rect(cx - rawW * 0.5, cy - rawH * 0.5, rawW, rawH);

            if (data.FovMarginPercent > 0.1)
            {
                dc.DrawRectangle(MarginFill, MarginPen, marginRect);
            }

            dc.DrawRectangle(TargetFill, TargetPen, rawRect);

            // 4. If lens image circle is smaller than sensor diagonal, shade exact vignetted corners in RED!
            if (data.HasVignetting)
            {
                var sensorGeo = new RectangleGeometry(sensorRect);
                var vignettedCorners = Geometry.Combine(sensorGeo, lensGeo, GeometryCombineMode.Exclude, null);
                vignettedCorners.Freeze();
                dc.DrawGeometry(VignetteFill, null, vignettedCorners);
                dc.DrawGeometry(null, LensWarnPen, lensGeo);
            }

            // Dimension labels
            DofText.Draw(dc, this, $"工件投影 {data.RawLengthMm:0.#}×{data.RawWidthMm:0.#}mm",
                9, DofPalette.Fov, cx, cy - 2, TextAlign.Center, bold: true);
            DofText.Draw(dc, this, data.FovMarginPercent > 0.1
                ? $"含 +{data.FovMarginPercent:0}% 余量 → {data.RequiredNx:N0}×{data.RequiredNy:N0} px"
                : $"需 {data.RequiredNx:N0}×{data.RequiredNy:N0} px",
                8.5, DofPalette.Dof, cx, cy + 12, TextAlign.Center);

            DofText.Draw(dc, this, $"靶面 {cam.SensorWidthMm:F1}×{cam.SensorHeightMm:F1} mm ({cam.ResolutionText} px, {cam.PixelSizeUm:F2}µm)",
                8.8, DofPalette.Muted, cx, Math.Min(h - 8, sensorRect.Bottom + 15), TextAlign.Center);

            var circleLabelY = Math.Max(30, cy - lensR - 5);
            DofText.Draw(dc, this, $"镜头像圆 Ø{data.SelectedLensCircle.DiameterMm:F1}mm",
                8.5, data.HasVignetting ? DofPalette.Warn : DofPalette.Dof,
                Math.Min(w - 14, cx + lensR * 0.72), circleLabelY, TextAlign.Center, bold: true);
        }

        private void DrawLineScanFallback(DrawingContext dc, double w, double h, CameraSelectionVisualData data)
        {
            var cx = w * 0.5;
            var cy = h * 0.54;
            DofText.Draw(dc, this, "超出常见面阵相机范围 → 推荐线阵相机 (Line Scan) / 多相机拼接",
                10.5, DofPalette.Warn, 12, 16, bold: true);

            // Moving continuous web/strip
            var webRect = new Rect(cx - w * 0.34, cy - 38, w * 0.68, 76);
            dc.DrawRoundedRectangle(TargetFill, TargetPen, webRect, 5, 5);

            // Single-line high-resolution sensor strip across the web
            var stripRect = new Rect(cx - 5, webRect.Top - 10, 10, webRect.Height + 20);
            dc.DrawRoundedRectangle(LineScanStripFill, LineScanPen, stripRect, 2, 2);

            // Motion direction arrow
            var ay = webRect.Bottom + 16;
            dc.DrawLine(ArrowPen, new Point(cx - 70, ay), new Point(cx + 70, ay));
            dc.DrawLine(ArrowPen, new Point(cx + 62, ay - 4), new Point(cx + 70, ay));
            dc.DrawLine(ArrowPen, new Point(cx + 62, ay + 4), new Point(cx + 70, ay));

            DofText.Draw(dc, this, $"线阵单行采样 (1 × {Math.Min(data.RequiredNx, data.RequiredNy):N0} ~ {Math.Max(data.RequiredNx, data.RequiredNy):N0} px)",
                9.5, DofPalette.Aperture, cx, webRect.Top - 16, TextAlign.Center, bold: true);
            DofText.Draw(dc, this, $"所需面阵 {data.RequiredNx:N0} × {data.RequiredNy:N0} px ({data.RequiredNx * (double)data.RequiredNy / 1_000_000:F1} MP)",
                9.5, DofPalette.White, cx, cy + 3, TextAlign.Center, bold: true);
            DofText.Draw(dc, this, $"编码器同步行频需求 ≈ {data.LineScanSuggestedRateKHz:F1} kHz (按线速 / 像素精度计算)",
                8.8, DofPalette.Ray, cx, ay + 14, TextAlign.Center);
        }
    }
}
