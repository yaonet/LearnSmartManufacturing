using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Vision;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Interactive 2D optical ray-tracing cross-section illustrating Bright-Field vs Dark-Field reflection geometry,
    /// Coaxial 45° beam-splitter double-pass loss, Dome 360° diffuse bounce, Backlight silhouette, and Cross-Polarizer filters.
    /// </summary>
    public class LightingOpticalCanvas : FrameworkElement
    {
        public static readonly DependencyProperty ResultProperty = DependencyProperty.Register(
            nameof(Result), typeof(LightingSimulationResult), typeof(LightingOpticalCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush CameraFill = DofPalette.Solid("#313244", 0.95);
        private static readonly Brush LensFill = DofPalette.Solid("#89DCEB", 0.20);
        private static readonly Brush ConeFill = DofPalette.Solid("#89DCEB", 0.06);
        private static readonly Brush FixtureFill = DofPalette.Solid("#45475A", 0.95);
        private static readonly Brush WorkpieceFill = DofPalette.Solid("#585B70", 0.90);
        private static readonly Brush PcbFill = DofPalette.Solid("#2E9E62", 0.85);
        private static readonly Brush FilmFill = DofPalette.Solid("#89DCEB", 0.25);
        private static readonly Brush SplitterFill = DofPalette.Solid("#89DCEB", 0.35);
        private static readonly Brush DomeWallFill = DofPalette.Solid("#CDD6F4", 0.14);
        private static readonly Brush PolarizerFill = DofPalette.Solid("#CBA6F7", 0.35);

        private static readonly Pen CameraPen = DofPalette.Pen(DofPalette.Subtle, 1.3);
        private static readonly Pen LensPen = DofPalette.Pen(DofPalette.Fov, 1.5);
        private static readonly Pen ConePen = DofPalette.Pen(DofPalette.Fov, 1.0, dash: 4, gap: 4);
        private static readonly Pen FixturePen = DofPalette.Pen(DofPalette.Muted, 1.2);
        private static readonly Pen WorkpiecePen = DofPalette.Pen(DofPalette.TextMain, 1.4);
        private static readonly Pen SplitterPen = DofPalette.Pen(DofPalette.Fov, 1.8);
        private static readonly Pen DomePen = DofPalette.Pen(DofPalette.TextMain, 2.0);
        private static readonly Pen PolarizerPen = DofPalette.Pen(DofPalette.Aperture, 1.5);
        private static readonly Pen DefectRayPen = DofPalette.Pen(DofPalette.Dof, 1.8);
        private static readonly Pen MissRayPen = DofPalette.Pen(DofPalette.Subtle, 1.2, dash: 4, gap: 3);

        private static readonly Dictionary<LightWavelength, (Brush Fill, Pen MainPen, Pen SoftPen)> WaveStyles = new()
        {
            [LightWavelength.White] = CreateWaveStyle("#F8FAFC"),
            [LightWavelength.Red630] = CreateWaveStyle("#F38BA8"),
            [LightWavelength.Green525] = CreateWaveStyle("#A6E3A1"),
            [LightWavelength.Blue470] = CreateWaveStyle("#89B4FA"),
            [LightWavelength.Ir850] = CreateWaveStyle("#E05263"),
            [LightWavelength.Uv365] = CreateWaveStyle("#CBA6F7"),
        };

        public LightingOpticalCanvas()
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
            if (w < 240 || h < 200)
            {
                return;
            }

            if (Result is not { } res)
            {
                return;
            }

            var p = res.Parameters;
            var (waveFill, wavePen, waveSoftPen) = WaveStyles[p.Wavelength];

            var cx = w * 0.5;
            const double camTop = 26;
            const double camW = 68;
            const double camH = 30;
            var lensY = camTop + camH;
            const double lensW = 38;
            const double lensH = 22;
            var lensExitY = lensY + lensH;
            var stageY = h - 54;

            // Title & mode badge
            DofText.Draw(dc, this, "光学几何光路剖面 (2D 实时射线追踪)", 10.5, DofPalette.White, 12, 16, bold: true);
            DofText.Draw(dc, this, res.IlluminationModeText, 10,
                res.IsDarkFieldMode ? DofPalette.Aperture : DofPalette.Fov,
                w - 12, 16, TextAlign.Right, bold: true);

            // Lens FOV acceptance cone
            var coneHalfW = Math.Min(w * 0.28, (stageY - lensExitY) * 0.42);
            var coneGeo = new StreamGeometry();
            using (var ctx = coneGeo.Open())
            {
                ctx.BeginFigure(new Point(cx - lensW * 0.35, lensExitY), true, true);
                ctx.LineTo(new Point(cx + lensW * 0.35, lensExitY), false, false);
                ctx.LineTo(new Point(cx + coneHalfW, stageY), false, false);
                ctx.LineTo(new Point(cx - coneHalfW, stageY), false, false);
            }

            coneGeo.Freeze();
            dc.DrawGeometry(ConeFill, ConePen, coneGeo);

            // Camera + Lens assembly
            dc.DrawRoundedRectangle(CameraFill, CameraPen, new Rect(cx - camW * 0.5, camTop, camW, camH), 4, 4);
            DofText.Draw(dc, this, "黑白相机", 9.5, DofPalette.White, cx, camTop + 18, TextAlign.Center, bold: true);
            dc.DrawRoundedRectangle(LensFill, LensPen, new Rect(cx - lensW * 0.5, lensY, lensW, lensH), 3, 3);
            DofText.Draw(dc, this, "镜头", 8.5, DofPalette.Fov, cx, lensY + 14, TextAlign.Center, bold: true);

            // Optional Cross-Polarizer on lens front
            if (p.UseCrossPolarizer)
            {
                var polRect = new Rect(cx - lensW * 0.6, lensExitY + 2, lensW * 1.2, 6);
                dc.DrawRoundedRectangle(PolarizerFill, PolarizerPen, polRect, 2, 2);
                DofText.Draw(dc, this, "镜头偏振片 90°", 8.5, DofPalette.Aperture, polRect.Right + 6, polRect.Bottom, bold: true);
            }

            // Draw specific light fixture & physical ray paths
            switch (p.LightType)
            {
                case LightGeometryType.Coaxial:
                    DrawCoaxialOptics(dc, cx, lensExitY, stageY, waveFill, wavePen, waveSoftPen, p.UseCrossPolarizer);
                    break;
                case LightGeometryType.Dome:
                    DrawDomeOptics(dc, cx, lensExitY, stageY, w, waveFill, wavePen, waveSoftPen);
                    break;
                case LightGeometryType.Backlight:
                    DrawBacklightOptics(dc, cx, lensExitY, stageY, waveFill, wavePen);
                    break;
                default:
                    DrawDirectionalRingOrBarOptics(dc, cx, lensExitY, stageY, w, p, res.IsDarkFieldMode, waveFill, wavePen, waveSoftPen);
                    break;
            }

            // Draw workpiece geometry on stage
            DrawWorkpiece(dc, cx, stageY, p.Workpiece);

            // Bottom engineering ray physics caption
            var physicsNote = p.LightType switch
            {
                LightGeometryType.Backlight => "底部平行光被不透光工件遮挡 → 相机只捕获锐利纯黑轮廓剪影",
                LightGeometryType.Coaxial => "45° 分光镜二次透反 (50%×50%=25% 有效光通) → 平面亮、微凹坑偏折变黑",
                LightGeometryType.Dome => "光线经半球漫反射内壁 360° 均匀入射 → 彻底消除曲面金属镜面耀斑",
                _ => res.IsDarkFieldMode
                    ? $"低角度暗场 ({p.IncidenceAngleDeg:0}°)：镜面反射光偏离镜头呈暗底，划痕/凸起散射光入镜变亮！"
                    : $"高角度明场 ({p.IncidenceAngleDeg:0}°)：镜面反射光直接进入镜头，平面明亮（镜面工件易炫光过曝）",
            };
            DofText.Draw(dc, this, physicsNote, 9.2, DofPalette.TextMain, cx, h - 10, TextAlign.Center);
        }

        private void DrawDirectionalRingOrBarOptics(
            DrawingContext dc,
            double cx,
            double lensExitY,
            double stageY,
            double w,
            LightingParameters p,
            bool isDarkField,
            Brush waveFill,
            Pen wavePen,
            Pen waveSoftPen)
        {
            var angleDeg = Math.Clamp(p.IncidenceAngleDeg, 10, 82);
            var rad = angleDeg * Math.PI / 180.0;
            var radius = Math.Min(w * 0.36, (stageY - lensExitY) * 0.78);
            var dx = Math.Max(42, Math.Cos(rad) * radius);
            var dy = Math.Clamp(Math.Sin(rad) * radius, 16, stageY - lensExitY - 22);
            var lightY = stageY - dy;

            var leftLight = new Point(cx - dx, lightY);
            var rightLight = new Point(cx + dx, lightY);

            // Light emitter housings (ring cross-section or dual bar blocks)
            DrawLightBlock(dc, leftLight, waveFill, p.LightType == LightGeometryType.DualBar ? "左条光" : "LED 环", p.UseCrossPolarizer);
            DrawLightBlock(dc, rightLight, waveFill, p.LightType == LightGeometryType.DualBar ? "右条光" : "LED 环", p.UseCrossPolarizer);

            // Angle arc annotation on left
            DofText.Draw(dc, this, $"入射角 {angleDeg:0}°", 9, DofPalette.Ray, leftLight.X - 6, leftLight.Y - 14, TextAlign.Center, bold: true);

            // Incident rays hitting center & side surface
            var hitCenter = new Point(cx, stageY);
            var hitLeft = new Point(cx - 28, stageY);
            var hitRight = new Point(cx + 28, stageY);

            DrawRayArrow(dc, wavePen, leftLight, hitCenter);
            DrawRayArrow(dc, wavePen, rightLight, hitCenter);
            dc.DrawLine(waveSoftPen, leftLight, hitLeft);
            dc.DrawLine(waveSoftPen, rightLight, hitRight);

            // Reflected specular ray from leftLight across center obeying reflection angle = incidence angle
            var reflLen = Math.Min(w * 0.34, stageY - lensExitY - 8);
            var reflEnd = new Point(cx + Math.Cos(rad) * reflLen, stageY - Math.Sin(rad) * reflLen);

            if (isDarkField)
            {
                // Specular reflection misses the lens!
                DrawRayArrow(dc, MissRayPen, hitCenter, reflEnd);
                DofText.Draw(dc, this, "镜面反射偏离镜头", 8.5, DofPalette.Muted, Math.Min(w - 64, reflEnd.X), reflEnd.Y - 6, TextAlign.Center);

                // Defect scatters straight up into lens
                DrawRayArrow(dc, DefectRayPen, new Point(cx, stageY - 2), new Point(cx, lensExitY + 8));
                DofText.Draw(dc, this, "缺陷散射入镜", 8.5, DofPalette.Dof, cx + 8, (stageY + lensExitY) * 0.5 + 8, bold: true);
            }
            else
            {
                // High-angle bright field: reflection enters the lens cone!
                var intoLens = new Point(cx + 8, lensExitY + 8);
                DrawRayArrow(dc, p.UseCrossPolarizer ? MissRayPen : wavePen, hitCenter, intoLens);
                DofText.Draw(dc, this,
                    p.UseCrossPolarizer ? "偏振反光被滤除" : "镜面反射入镜",
                    8.5, p.UseCrossPolarizer ? DofPalette.Aperture : DofPalette.Warn,
                    cx + 14, (stageY + lensExitY) * 0.5 + 10, bold: true);
            }
        }

        private void DrawCoaxialOptics(
            DrawingContext dc,
            double cx,
            double lensExitY,
            double stageY,
            Brush waveFill,
            Pen wavePen,
            Pen waveSoftPen,
            bool usePolarizer)
        {
            var splitY = lensExitY + (stageY - lensExitY) * 0.42;
            var emitterX = Math.Max(46, cx - 115);

            // Side coaxial light housing
            var box = new Rect(emitterX - 24, splitY - 14, 48, 28);
            dc.DrawRoundedRectangle(FixtureFill, FixturePen, box, 4, 4);
            dc.DrawRectangle(waveFill, null, new Rect(box.Right - 4, box.Top + 3, 4, box.Height - 6));
            DofText.Draw(dc, this, "同轴源", 8.5, DofPalette.White, box.Left + 22, splitY + 3, TextAlign.Center, bold: true);
            if (usePolarizer)
            {
                dc.DrawRectangle(PolarizerFill, PolarizerPen, new Rect(box.Right + 2, box.Top + 2, 5, box.Height - 4));
            }

            // 45° Beam Splitter mirror
            const double halfGlass = 22;
            dc.DrawLine(SplitterPen, new Point(cx - halfGlass, splitY - halfGlass), new Point(cx + halfGlass, splitY + halfGlass));
            DofText.Draw(dc, this, "45° 半透分光镜", 8.5, DofPalette.Fov, cx + halfGlass + 8, splitY + 4, bold: true);

            // Horizontal ray -> hits splitter -> 50% passes through (lost), 50% goes down to workpiece
            DrawRayArrow(dc, wavePen, new Point(box.Right + 4, splitY), new Point(cx, splitY));
            dc.DrawLine(MissRayPen, new Point(cx, splitY), new Point(cx + 68, splitY));
            DofText.Draw(dc, this, "50% 透射损耗", 8, DofPalette.Muted, cx + 68, splitY - 6, TextAlign.Right);

            // Vertical coaxial beam straight down & reflected straight back up
            DrawRayArrow(dc, wavePen, new Point(cx - 5, splitY + 2), new Point(cx - 5, stageY - 2));
            DrawRayArrow(dc, DefectRayPen, new Point(cx + 5, stageY - 2), new Point(cx + 5, lensExitY + 8));

            // Micro-pit deflection ray missing lens
            DrawRayArrow(dc, MissRayPen, new Point(cx - 18, stageY - 2), new Point(cx - 64, splitY + 24));
            DofText.Draw(dc, this, "凹坑偏折不入镜", 8, DofPalette.Warn, cx - 68, splitY + 38, TextAlign.Center);

            dc.DrawLine(waveSoftPen, new Point(cx, splitY), new Point(cx, stageY));
        }

        private void DrawDomeOptics(
            DrawingContext dc,
            double cx,
            double lensExitY,
            double stageY,
            double w,
            Brush waveFill,
            Pen wavePen,
            Pen waveSoftPen)
        {
            var domeR = Math.Min(w * 0.34, stageY - lensExitY - 14);
            var domeBaseY = stageY - 14;

            // Semicircular dome shell with top camera hole
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(cx - domeR, domeBaseY), true, false);
                ctx.ArcTo(new Point(cx - 18, domeBaseY - domeR * 0.96), new Size(domeR, domeR), 0, false, SweepDirection.Clockwise, true, false);
                ctx.BeginFigure(new Point(cx + 18, domeBaseY - domeR * 0.96), true, false);
                ctx.ArcTo(new Point(cx + domeR, domeBaseY), new Size(domeR, domeR), 0, false, SweepDirection.Clockwise, true, false);
            }

            geo.Freeze();
            dc.DrawGeometry(DomeWallFill, DomePen, geo);

            // Bottom rim LED rings firing UPWARD into the diffuse dome ceiling
            dc.DrawRoundedRectangle(waveFill, FixturePen, new Rect(cx - domeR - 6, domeBaseY - 4, 16, 10), 2, 2);
            dc.DrawRoundedRectangle(waveFill, FixturePen, new Rect(cx + domeR - 10, domeBaseY - 4, 16, 10), 2, 2);

            // Multi-angle soft diffuse bounce rays illuminating workpiece from 360°
            double[] anglesDeg = [35, 65, 115, 145];
            foreach (var deg in anglesDeg)
            {
                var rad = deg * Math.PI / 180.0;
                var wallPt = new Point(cx - Math.Cos(rad) * domeR * 0.92, domeBaseY - Math.Sin(rad) * domeR * 0.92);
                var rimPt = deg < 90 ? new Point(cx - domeR + 2, domeBaseY - 4) : new Point(cx + domeR - 2, domeBaseY - 4);
                dc.DrawLine(waveSoftPen, rimPt, wallPt);
                DrawRayArrow(dc, wavePen, wallPt, new Point(cx + (deg < 90 ? -8 : 8), stageY - 8));
            }

            DofText.Draw(dc, this, "漫反射穹顶内壁", 8.5, DofPalette.TextMain, cx + domeR * 0.65, domeBaseY - domeR * 0.72, bold: true);
        }

        private void DrawBacklightOptics(
            DrawingContext dc,
            double cx,
            double lensExitY,
            double stageY,
            Brush waveFill,
            Pen wavePen)
        {
            var panelRect = new Rect(cx - 115, stageY + 12, 230, 14);
            dc.DrawRoundedRectangle(waveFill, FixturePen, panelRect, 4, 4);
            DofText.Draw(dc, this, "底部平行背光源 (Backlight)", 8.5, DofPalette.Canvas, cx, panelRect.Bottom - 3, TextAlign.Center, bold: true);

            // Parallel vertical rays: outer rays reach camera, center rays are blocked by workpiece
            double[] xs = [-84, -56, -24, 0, 24, 56, 84];
            foreach (var ox in xs)
            {
                var blocked = Math.Abs(ox) <= 36;
                var start = new Point(cx + ox, panelRect.Top);
                var end = blocked ? new Point(cx + ox, stageY + 2) : new Point(cx + ox, lensExitY + 14);
                DrawRayArrow(dc, blocked ? MissRayPen : wavePen, start, end);
            }
        }

        private void DrawLightBlock(DrawingContext dc, Point center, Brush emitterBrush, string label, bool usePolarizer)
        {
            var rect = new Rect(center.X - 20, center.Y - 9, 40, 18);
            dc.DrawRoundedRectangle(FixtureFill, FixturePen, rect, 4, 4);
            dc.DrawRectangle(emitterBrush, null, new Rect(rect.Left + 4, rect.Bottom - 4, rect.Width - 8, 3));
            DofText.Draw(dc, this, label, 8, DofPalette.White, center.X, center.Y + 2, TextAlign.Center, bold: true);

            if (usePolarizer)
            {
                dc.DrawRectangle(PolarizerFill, PolarizerPen, new Rect(rect.Left + 3, rect.Bottom + 1, rect.Width - 6, 3.5));
            }
        }

        private void DrawWorkpiece(DrawingContext dc, double cx, double stageY, WorkpieceType workpiece)
        {
            switch (workpiece)
            {
                case WorkpieceType.CurvedSpecularMetal:
                    {
                        var geo = new StreamGeometry();
                        using (var ctx = geo.Open())
                        {
                            ctx.BeginFigure(new Point(cx - 56, stageY + 10), true, true);
                            ctx.ArcTo(new Point(cx + 56, stageY + 10), new Size(56, 26), 0, false, SweepDirection.Clockwise, true, false);
                        }

                        geo.Freeze();
                        dc.DrawGeometry(WorkpieceFill, WorkpiecePen, geo);
                        DofText.Draw(dc, this, "曲面反光金属", 8.5, DofPalette.White, cx, stageY + 5, TextAlign.Center);
                        break;
                    }

                case WorkpieceType.GreenPcbSilkscreen:
                    dc.DrawRoundedRectangle(PcbFill, WorkpiecePen, new Rect(cx - 68, stageY, 136, 12), 2, 2);
                    dc.DrawRectangle(DofPalette.White, null, new Rect(cx - 32, stageY - 1.5, 16, 2.5));
                    dc.DrawRectangle(DofPalette.Ray, null, new Rect(cx + 12, stageY - 1.5, 18, 2.5));
                    DofText.Draw(dc, this, "绿底 PCB + 丝印/焊盘", 8.5, DofPalette.White, cx, stageY + 9.5, TextAlign.Center);
                    break;

                case WorkpieceType.StampedContour:
                    dc.DrawRoundedRectangle(WorkpieceFill, WorkpiecePen, new Rect(cx - 42, stageY - 4, 84, 14), 2, 2);
                    DofText.Draw(dc, this, "不透明冲压件", 8.5, DofPalette.White, cx, stageY + 6, TextAlign.Center);
                    break;

                case WorkpieceType.GlossyFilmLabel:
                    dc.DrawRoundedRectangle(WorkpieceFill, WorkpiecePen, new Rect(cx - 66, stageY, 132, 12), 2, 2);
                    dc.DrawRectangle(FilmFill, LensPen, new Rect(cx - 68, stageY - 4, 136, 4));
                    DofText.Draw(dc, this, "高反光透明覆膜 + 印刷字符", 8.5, DofPalette.White, cx, stageY + 9.5, TextAlign.Center);
                    break;

                default:
                    // Flat mirror metal / wafer with micro V-notch scratch at center
                    dc.DrawRoundedRectangle(WorkpieceFill, WorkpiecePen, new Rect(cx - 68, stageY, 136, 12), 2, 2);
                    dc.DrawLine(DefectRayPen, new Point(cx - 5, stageY), new Point(cx, stageY + 5));
                    dc.DrawLine(DefectRayPen, new Point(cx, stageY + 5), new Point(cx + 5, stageY));
                    DofText.Draw(dc, this, workpiece == WorkpieceType.WaferMicroPit ? "镜面硅片 + 微凹坑" : "镜面金属 + 微划痕",
                        8.5, DofPalette.White, cx, stageY + 10, TextAlign.Center);
                    break;
            }
        }

        private static void DrawRayArrow(DrawingContext dc, Pen pen, Point from, Point to)
        {
            dc.DrawLine(pen, from, to);
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var len = double.Hypot(dx, dy);
            if (len < 12) return;
            var ux = dx / len;
            var uy = dy / len;
            var mid = new Point(from.X + dx * 0.62, from.Y + dy * 0.62);
            dc.DrawLine(pen, new Point(mid.X - ux * 6 - uy * 3.5, mid.Y - uy * 6 + ux * 3.5), mid);
            dc.DrawLine(pen, new Point(mid.X - ux * 6 + uy * 3.5, mid.Y - uy * 6 - ux * 3.5), mid);
        }

        private static (Brush Fill, Pen MainPen, Pen SoftPen) CreateWaveStyle(string hex)
        {
            var fill = DofPalette.Solid(hex, 0.92);
            var main = DofPalette.Pen(DofPalette.Solid(hex, 0.95), 1.6);
            var soft = DofPalette.Pen(DofPalette.Solid(hex, 0.35), 1.1);
            return (fill, main, soft);
        }
    }
}
