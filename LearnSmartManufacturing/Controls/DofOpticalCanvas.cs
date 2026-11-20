using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LearnSmartManufacturing.Models;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Main optical path view: object space above the lens, image space below, DOF band and a draggable workpiece.
    /// </summary>
    public class DofOpticalCanvas : FrameworkElement
    {
        public static readonly DependencyProperty ResultProperty = DependencyProperty.Register(
            nameof(Result), typeof(DofResult), typeof(DofOpticalCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>Written while the workpiece is dragged; the displayed value always comes from <see cref="Result"/>.</summary>
        public static readonly DependencyProperty DeltaZMmProperty = DependencyProperty.Register(
            nameof(DeltaZMm), typeof(double), typeof(DofOpticalCanvas),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty DeltaZLimitMmProperty = DependencyProperty.Register(
            nameof(DeltaZLimitMm), typeof(double), typeof(DofOpticalCanvas), new PropertyMetadata(60.0));

        private static readonly Brush HatchBrush = CreateHatchBrush();

        private double _lensY;
        private double _sObj = 1;
        private Rect _workpieceHit;
        private bool _dragging;
        private bool _hover;

        public DofOpticalCanvas()
        {
            ClipToBounds = true;
        }

        public DofResult? Result
        {
            get => (DofResult?)GetValue(ResultProperty);
            set => SetValue(ResultProperty, value);
        }

        public double DeltaZMm
        {
            get => (double)GetValue(DeltaZMmProperty);
            set => SetValue(DeltaZMmProperty, value);
        }

        public double DeltaZLimitMm
        {
            get => (double)GetValue(DeltaZLimitMmProperty);
            set => SetValue(DeltaZLimitMmProperty, value);
        }

        private static Brush CreateHatchBrush()
        {
            var drawing = new GeometryDrawing(null, DofPalette.Pen(DofPalette.Solid("#A6E3A1", 0.14), 1),
                new LineGeometry(new Point(0, 12), new Point(12, 0)));
            var brush = new DrawingBrush(drawing)
            {
                TileMode = TileMode.Tile,
                Viewbox = new Rect(0, 0, 12, 12),
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 12, 12),
                ViewportUnits = BrushMappingMode.Absolute,
            };
            brush.Freeze();
            return brush;
        }

        #region Dragging

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (_workpieceHit.Contains(e.GetPosition(this)))
            {
                _dragging = true;
                CaptureMouse();
                Cursor = Cursors.SizeNS;
                InvalidateVisual();
                e.Handled = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var pt = e.GetPosition(this);

            if (_dragging && Result is { } r)
            {
                // Pointer position -> object distance s -> offset from the focus plane, snapped to 0.5 mm.
                var s = (_lensY - pt.Y) / _sObj;
                var dz = Math.Round((s - r.WdMm) * 2) / 2;
                dz = Math.Clamp(dz, -DeltaZLimitMm, DeltaZLimitMm);
                if (Math.Abs(dz) <= 0.5)
                {
                    dz = 0;
                }

                DeltaZMm = dz;
                return;
            }

            var hover = _workpieceHit.Contains(pt);
            if (hover != _hover)
            {
                _hover = hover;
                Cursor = hover ? Cursors.SizeNS : null;
                InvalidateVisual();
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_dragging)
            {
                _dragging = false;
                ReleaseMouseCapture();
                Cursor = _hover ? Cursors.SizeNS : null;
                InvalidateVisual();
            }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_dragging && _hover)
            {
                _hover = false;
                Cursor = null;
                InvalidateVisual();
            }
        }

        #endregion

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));

            if (Result is not { } r || w < 50 || h < 50)
            {
                return;
            }

            // Optical center O sits at the horizontal middle and 62% of the height.
            var cx = w * 0.5;
            var lensY = h * 0.62;
            var maxObjPx = lensY - 46;
            var maxImgPx = h - lensY - 46;
            if (maxObjPx <= 0 || maxImgPx <= 0)
            {
                return;
            }

            // Object space fits WD = 500 mm, image space fits f = 50 mm; the two scales are independent.
            var sObj = maxObjPx / 500.0;
            var sImg = maxImgPx / 50.0;

            // A wide FOV shrinks both spaces by the same factor so the chief rays stay collinear.
            var halfFovPx = r.FovMm / 2 * sObj;
            var maxHalfWidth = Math.Max(80, w / 2 - 72);
            if (halfFovPx > maxHalfWidth)
            {
                var shrink = maxHalfWidth / halfFovPx;
                sObj *= shrink;
                sImg *= shrink;
                halfFovPx = r.FovMm / 2 * sObj;
            }

            var wdPx = r.WdMm * sObj;
            var fPx = r.FocalMm * sImg;
            var halfSensorPx = r.SensorWidthMm / 2 * sImg;

            var yWd = lensY - wdPx;
            var ySensor = lensY + fPx;
            var yWorkpiece = Math.Max(16, Math.Min(lensY - 14, lensY - r.ObjectDistanceMm * sObj));

            var yNear = lensY - r.NearLimitMm * sObj;
            var rawYFar = double.IsFinite(r.FarLimitMm) ? lensY - r.FarLimitMm * sObj : -9999;
            var isFarClipped = rawYFar < 14;
            var yFar = Math.Max(14, rawYFar);
            var dofPx = Math.Abs(yNear - yFar);

            _lensY = lensY;
            _sObj = sObj;

            DrawBackground(dc, w, h, cx, lensY, sObj);
            DrawDofBand(dc, w, cx, lensY, yWd, yNear, yFar, isFarClipped, dofPx, halfFovPx, r);

            // Object-space and image-space field cones
            dc.DrawGeometry(DofPalette.Solid("#89DCEB", 0.11), null, Triangle(cx, lensY, cx - halfFovPx, yWd, cx + halfFovPx, yWd));
            dc.DrawGeometry(DofPalette.Solid("#A6E3A1", 0.13), null, Triangle(cx, lensY, cx - halfSensorPx, ySensor, cx + halfSensorPx, ySensor));

            // The aperture opening narrows as the F-number grows.
            var lensHalfW = Math.Min(72, Math.Max(48, w * 0.08));
            var apertureRatio = Math.Max(0.08, Math.Min(0.88, 1.35 / r.FNumber));
            var apertureHalf = Math.Max(4, lensHalfW * apertureRatio);

            DrawMarginalRays(dc, cx, lensY, yWd, ySensor, yWorkpiece, fPx, apertureHalf, r);

            // Chief rays through O
            var chiefPen = DofPalette.Pen(DofPalette.Ray, 1.8);
            dc.DrawLine(chiefPen, new Point(cx - halfFovPx, yWd), new Point(cx + halfSensorPx, ySensor));
            dc.DrawLine(chiefPen, new Point(cx + halfFovPx, yWd), new Point(cx - halfSensorPx, ySensor));

            DrawFovArc(dc, cx, lensY, halfFovPx, wdPx, r.FovAngleDeg);
            DrawLensAndBlades(dc, cx, lensY, lensHalfW, apertureHalf, r);
            DrawFovAndSensor(dc, cx, lensY, yWd, ySensor, halfFovPx, halfSensorPx, r);
            DrawWorkpiece(dc, cx, yWorkpiece, r);
            DrawHud(dc, sObj, sImg, dofPx);
        }

        private void DrawBackground(DrawingContext dc, double w, double h, double cx, double lensY, double sObj)
        {
            var gridPen = DofPalette.Pen(DofPalette.Solid("#313244", 0.45), 1);
            const double step = 36;
            for (var x = cx % step; x < w; x += step)
            {
                dc.DrawLine(gridPen, new Point(x, 0), new Point(x, h));
            }

            for (var y = lensY % step; y < h; y += step)
            {
                dc.DrawLine(gridPen, new Point(0, y), new Point(w, y));
            }

            // Object-space ruler (distance from the lens in mm)
            var rulerPen = DofPalette.Pen(DofPalette.Surface, 1);
            for (var mm = 0; mm <= 500; mm += 50)
            {
                var y = lensY - mm * sObj;
                if (y < 14)
                {
                    break;
                }

                var major = mm % 100 == 0;
                dc.DrawLine(rulerPen, new Point(0, y), new Point(major ? 14 : 7, y));
                if (major && mm > 0)
                {
                    DofText.Draw(dc, this, $"{mm}mm", 10, DofPalette.Subtle, 18, y + 3);
                }
            }

            dc.DrawLine(DofPalette.Pen(DofPalette.Solid("#585B70"), 1.2, 5, 5), new Point(cx, 12), new Point(cx, h - 12));
            dc.DrawLine(DofPalette.Pen(DofPalette.Solid("#45475A", 0.85), 1, 3, 4), new Point(16, lensY), new Point(w - 16, lensY));
        }

        private void DrawDofBand(DrawingContext dc, double w, double cx, double lensY, double yWd, double yNear, double yFar,
            bool isFarClipped, double dofPx, double halfFovPx, DofResult r)
        {
            var bandLeft = Math.Max(58, cx - Math.Max(halfFovPx + 36, 130));
            var bandRight = Math.Min(w - 36, cx + Math.Max(halfFovPx + 36, 130));
            var bandW = bandRight - bandLeft;
            var dofPen = DofPalette.Pen(DofPalette.Dof, 1.4, 6, 4);

            if (dofPx < 3)
            {
                // A very thin DOF is drawn as a glowing line at the focus plane.
                dc.DrawLine(DofPalette.Pen(DofPalette.Solid("#A6E3A1", 0.3), 9), new Point(bandLeft, yWd), new Point(bandRight, yWd));
                dc.DrawLine(DofPalette.Pen(DofPalette.Dof, 2.5), new Point(bandLeft, yWd), new Point(bandRight, yWd));
            }
            else
            {
                var wdStop = Math.Max(0.05, Math.Min(0.95, (yWd - yFar) / (yNear - yFar)));
                var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb(isFarClipped ? (byte)56 : (byte)31, 166, 227, 161), 0));
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb(77, 166, 227, 161), wdStop));
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb(31, 166, 227, 161), 1));
                gradient.Freeze();

                var band = new Rect(bandLeft, yFar, bandW, yNear - yFar);
                dc.DrawRectangle(gradient, null, band);
                dc.DrawRectangle(HatchBrush, null, band);

                dc.DrawLine(dofPen, new Point(bandLeft, yNear), new Point(bandRight, yNear));
                if (!isFarClipped)
                {
                    dc.DrawLine(dofPen, new Point(bandLeft, yFar), new Point(bandRight, yFar));
                }
                else
                {
                    var message = r.IsHyperfocal
                        ? "▲ 远界 Df → ∞ (已达超焦距，景深延伸至无穷远)"
                        : $"▲ 远界 Df = {r.FarLimitMm:F1} mm (超出画布顶端)";
                    DofText.Draw(dc, this, message, 11, DofPalette.Dof, bandLeft + 6, 26, bold: true);
                }
            }

            if (dofPx >= 14)
            {
                DofText.Draw(dc, this, $"近界 Dn = {r.NearLimitMm:F1} mm", 11, DofPalette.Dof, bandLeft + 6, yNear + 13);
                if (!isFarClipped)
                {
                    DofText.Draw(dc, this, $"远界 Df = {r.FarLimitMm:F1} mm", 11, DofPalette.Dof, bandLeft + 6, Math.Max(26, yFar - 5));
                }
            }

            // Bracket with the total / front / rear DOF
            var bracketX = bandLeft - 8;
            if (bracketX <= 52)
            {
                return;
            }

            var bTop = dofPx < 3 ? yWd - 2 : yFar;
            var bBot = dofPx < 3 ? yWd + 2 : yNear;
            var bracketPen = DofPalette.Pen(DofPalette.Dof, 1.5);
            dc.DrawLine(bracketPen, new Point(bracketX + 6, bTop), new Point(bracketX, bTop));
            dc.DrawLine(bracketPen, new Point(bracketX, bTop), new Point(bracketX, bBot));
            dc.DrawLine(bracketPen, new Point(bracketX, bBot), new Point(bracketX + 6, bBot));

            var midY = (bTop + bBot) / 2;
            var dofLabel = double.IsFinite(r.TotalDofMm) ? $"DOF {r.TotalDofMm:F1}mm" : "DOF ∞";
            var detailLabel = double.IsFinite(r.RearDofMm)
                ? $"前{r.FrontDofMm:F1} / 后{r.RearDofMm:F1}"
                : $"前{r.FrontDofMm:F1} / 后∞";

            var textW = Math.Max(DofText.Measure(this, dofLabel, 11, true), DofText.Measure(this, detailLabel, 11, true)) + 10;
            var fitsLeft = bracketX - textW - 6 > 52;
            var boxX = Math.Max(54, bracketX - textW - 6);
            var boxY = Math.Max(36, Math.Min(lensY - 40, midY - 16));
            var finalX = fitsLeft ? boxX : bandLeft + 6;
            var finalY = fitsLeft ? boxY : Math.Max(42, yWd + 16);

            dc.DrawRectangle(DofPalette.Solid("#141420", 0.85), DofPalette.Pen(DofPalette.Solid("#A6E3A1", 0.45), 1),
                new Rect(finalX - 2, finalY - 11, textW, 28));
            DofText.Draw(dc, this, dofLabel, 11, DofPalette.Dof, finalX + 3, finalY, bold: true);
            DofText.Draw(dc, this, detailLabel, 10, DofPalette.TextMain, finalX + 3, finalY + 13);
        }

        private void DrawMarginalRays(DrawingContext dc, double cx, double lensY, double yWd, double ySensor, double yWorkpiece,
            double fPx, double apertureHalf, DofResult r)
        {
            // Focus-plane point -> aperture edge -> sensor center
            var cone = new StreamGeometry();
            using (var g = cone.Open())
            {
                g.BeginFigure(new Point(cx, yWd), true, true);
                g.LineTo(new Point(cx - apertureHalf, lensY), false, false);
                g.LineTo(new Point(cx, ySensor), false, false);
                g.LineTo(new Point(cx + apertureHalf, lensY), false, false);
            }

            cone.Freeze();
            dc.DrawGeometry(DofPalette.Solid("#CBA6F7", 0.16), null, cone);

            var conePen = DofPalette.Pen(DofPalette.Solid("#CBA6F7", 0.72), 1.25, 4, 3);
            dc.DrawLine(conePen, new Point(cx, yWd), new Point(cx - apertureHalf, lensY));
            dc.DrawLine(conePen, new Point(cx - apertureHalf, lensY), new Point(cx, ySensor));
            dc.DrawLine(conePen, new Point(cx, yWd), new Point(cx + apertureHalf, lensY));
            dc.DrawLine(conePen, new Point(cx + apertureHalf, lensY), new Point(cx, ySensor));

            if (Math.Abs(r.DeltaZMm) <= 0.1 || r.IsVirtualImage)
            {
                return;
            }

            // Rays from the displaced workpiece converge before/after the sensor and spread into a blur spot on it.
            var color = r.IsInDof ? DofPalette.Dof : DofPalette.Warn;
            var focusRatio = r.ImageDistanceMm / r.BestImageDistanceMm;
            var yActualFocus = lensY + fPx * Math.Max(0.25, Math.Min(2.2, focusRatio));
            var t = (ySensor - lensY) / (yActualFocus - lensY);
            var xLeft = cx - apertureHalf * (1 - t);
            var xRight = cx + apertureHalf * (1 - t);

            var rayPen = DofPalette.Pen(DofPalette.WithOpacity(color, 0.9), 1.3);
            dc.DrawLine(rayPen, new Point(cx, yWorkpiece), new Point(cx - apertureHalf, lensY));
            dc.DrawLine(rayPen, new Point(cx - apertureHalf, lensY), new Point(xLeft, ySensor));
            dc.DrawLine(rayPen, new Point(cx, yWorkpiece), new Point(cx + apertureHalf, lensY));
            dc.DrawLine(rayPen, new Point(cx + apertureHalf, lensY), new Point(xRight, ySensor));

            var spotHalf = Math.Max(2, Math.Abs(xRight - xLeft) / 2);
            dc.DrawLine(DofPalette.Pen(DofPalette.WithOpacity(color, 0.3), 11), new Point(cx - spotHalf, ySensor), new Point(cx + spotHalf, ySensor));
            dc.DrawLine(DofPalette.Pen(color, 5), new Point(cx - spotHalf, ySensor), new Point(cx + spotHalf, ySensor));
        }

        private void DrawFovArc(DrawingContext dc, double cx, double lensY, double halfFovPx, double wdPx, double fovAngleDeg)
        {
            var halfAngle = Math.Atan2(halfFovPx, wdPx);
            var radius = Math.Min(44, wdPx * 0.35);
            if (radius < 14)
            {
                return;
            }

            var a0 = -Math.PI / 2 - halfAngle;
            var a1 = -Math.PI / 2 + halfAngle;
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                g.BeginFigure(new Point(cx + radius * Math.Cos(a0), lensY + radius * Math.Sin(a0)), false, false);
                g.ArcTo(new Point(cx + radius * Math.Cos(a1), lensY + radius * Math.Sin(a1)), new Size(radius, radius), 0, false,
                    SweepDirection.Clockwise, true, false);
            }

            arc.Freeze();
            dc.DrawGeometry(null, DofPalette.Pen(DofPalette.Solid("#F9E2AF", 0.65), 1.2), arc);
            DofText.Draw(dc, this, $"2θ={fovAngleDeg:F1}°", 10, DofPalette.Ray, cx, lensY - radius - 5, TextAlign.Center);
        }

        private void DrawLensAndBlades(DrawingContext dc, double cx, double lensY, double lensHalfW, double apertureHalf, DofResult r)
        {
            const double lensHalfH = 11;
            var lens = new StreamGeometry();
            using (var g = lens.Open())
            {
                g.BeginFigure(new Point(cx - lensHalfW, lensY), true, true);
                g.QuadraticBezierTo(new Point(cx, lensY - lensHalfH * 1.9), new Point(cx + lensHalfW, lensY), true, false);
                g.QuadraticBezierTo(new Point(cx, lensY + lensHalfH * 1.9), new Point(cx - lensHalfW, lensY), true, false);
            }

            lens.Freeze();
            dc.DrawGeometry(DofPalette.Solid("#89B4FA", 0.22), DofPalette.Pen(DofPalette.Accent, 1.8), lens);

            // Left and right aperture blades
            var bladeOuterX = lensHalfW + 10;
            const double bladeThickness = 8;
            var bladePen = DofPalette.Pen(DofPalette.Aperture, 1.5);
            var bladeW = bladeOuterX - apertureHalf;
            dc.DrawRoundedRectangle(DofPalette.Surface, bladePen, new Rect(cx - bladeOuterX, lensY - bladeThickness / 2, bladeW, bladeThickness), 2, 2);
            dc.DrawRoundedRectangle(DofPalette.Surface, bladePen, new Rect(cx + apertureHalf, lensY - bladeThickness / 2, bladeW, bladeThickness), 2, 2);

            DofText.Draw(dc, this, $"光圈 F{r.FNumber:F1} (D={r.ApertureDiameterMm:F1}mm)", 11, DofPalette.Aperture,
                cx + bladeOuterX + 8, lensY + 4, bold: true);
            DofText.Draw(dc, this, "光心 O", 11, DofPalette.Warn, cx - bladeOuterX - 8, lensY + 4, TextAlign.Right, bold: true);
            dc.DrawEllipse(DofPalette.Warn, null, new Point(cx, lensY), 4, 4);
        }

        private void DrawFovAndSensor(DrawingContext dc, double cx, double lensY, double yWd, double ySensor,
            double halfFovPx, double halfSensorPx, DofResult r)
        {
            // FOV bar at the focus plane
            var fovPen = new Pen(DofPalette.Fov, 5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(fovPen, new Point(cx - halfFovPx, yWd), new Point(cx + halfFovPx, yWd));
            var tickPen = DofPalette.Pen(DofPalette.Fov, 2);
            dc.DrawLine(tickPen, new Point(cx - halfFovPx, yWd - 6), new Point(cx - halfFovPx, yWd + 6));
            dc.DrawLine(tickPen, new Point(cx + halfFovPx, yWd - 6), new Point(cx + halfFovPx, yWd + 6));
            DofText.Draw(dc, this, $"最佳对焦面 · FOV = {r.FovMm:F1} mm", 12, DofPalette.Fov, cx, Math.Max(18, yWd - 12), TextAlign.Center, bold: true);

            // Sensor chip
            dc.DrawLine(DofPalette.Pen(DofPalette.Dof, 6), new Point(cx - halfSensorPx, ySensor), new Point(cx + halfSensorPx, ySensor));
            dc.DrawRectangle(null, DofPalette.Pen(DofPalette.Solid("#585B70"), 1.5),
                new Rect(cx - halfSensorPx - 6, ySensor - 4, halfSensorPx * 2 + 12, 12));
            DofText.Draw(dc, this, $"CMOS 传感器靶面 h = {r.SensorWidthMm:F2} mm", 12, DofPalette.Dof, cx, ySensor + 24, TextAlign.Center, bold: true);

            DofText.Draw(dc, this, $"WD = {r.WdMm:F0} mm", 11, DofPalette.Ray, cx + 10, (yWd + lensY) / 2 + 12);
            DofText.Draw(dc, this, $"f = {r.FocalMm:F1} mm", 11, DofPalette.Dof, cx + 10, (lensY + ySensor) / 2 + 4);
        }

        private void DrawWorkpiece(DrawingContext dc, double cx, double yWorkpiece, DofResult r)
        {
            var color = r.IsInDof ? DofPalette.Dof : DofPalette.Warn;
            const double pillW = 132;
            const double pillH = 26;
            var x = cx - pillW / 2;
            var y = yWorkpiece - pillH / 2;
            _workpieceHit = new Rect(x - 12, y - 10, pillW + 24, pillH + 20);

            dc.DrawLine(DofPalette.Pen(DofPalette.WithOpacity(color, r.IsInDof ? 0.5 : 0.6), 1.2, 2, 3),
                new Point(cx - 90, yWorkpiece), new Point(cx + 90, yWorkpiece));

            if (_dragging || _hover)
            {
                dc.DrawRoundedRectangle(DofPalette.WithOpacity(color, _dragging ? 0.3 : 0.2), null,
                    new Rect(x - 4, y - 4, pillW + 8, pillH + 8), 17, 17);
            }

            dc.DrawRoundedRectangle(_dragging ? DofPalette.Solid("#28283D") : DofPalette.Solid("#1E1E2E", 0.94),
                DofPalette.Pen(color, _dragging ? 2.5 : 1.8), new Rect(x, y, pillW, pillH), 13, 13);

            DofText.Draw(dc, this, "↕", 12, color, x + 9, yWorkpiece + 4, bold: true);
            var sign = r.DeltaZMm > 0 ? "+" : string.Empty;
            DofText.Draw(dc, this, $"工件 ΔZ {sign}{r.DeltaZMm:F1}mm", 11, DofPalette.White, cx + 6, yWorkpiece + 4, TextAlign.Center, bold: true);
        }

        private void DrawHud(DrawingContext dc, double sObj, double sImg, double dofPx)
        {
            var title = "双空间等比缩放";
            var info = $"物方 1mm={sObj:F2}px · 像方放大 {sImg / Math.Max(0.001, sObj):F1}×";
            var titleW = DofText.Measure(this, title, 11, true);
            var infoW = DofText.Measure(this, info, 11);
            dc.DrawRoundedRectangle(DofPalette.Solid("#1E1E2E", 0.85), DofPalette.Pen(DofPalette.Surface, 1),
                new Rect(12, 12, titleW + infoW + 28, 24), 8, 8);
            DofText.Draw(dc, this, title, 11, DofPalette.Ray, 22, 28, bold: true);
            DofText.Draw(dc, this, info, 11, DofPalette.Muted, 22 + titleW + 8, 28);

            if (dofPx < 3)
            {
                var badge = "景深极薄（屏幕厚度 < 3px），请查看右侧局部放大剖面 ↗";
                var badgeW = DofText.Measure(this, badge, 11);
                dc.DrawRoundedRectangle(DofPalette.Solid("#A6E3A1", 0.15), DofPalette.Pen(DofPalette.Solid("#A6E3A1", 0.4), 1),
                    new Rect(12, 42, badgeW + 20, 24), 8, 8);
                DofText.Draw(dc, this, badge, 11, DofPalette.Dof, 22, 58);
            }
        }

        private static Geometry Triangle(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(x1, y1), true, true);
                g.LineTo(new Point(x2, y2), false, false);
                g.LineTo(new Point(x3, y3), false, false);
            }

            geometry.Freeze();
            return geometry;
        }
    }
}
