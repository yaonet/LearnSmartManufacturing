using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Motion;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Interactive 2D XY gantry canvas displaying workspace coordinates, teach points,
    /// planned/preview interpolation trajectories, actual executed trail, stop-capture &amp; PSO fly-capture markers.
    /// </summary>
    public class XyStageCanvas : FrameworkElement
    {
        public static readonly DependencyProperty StageSnapshotProperty = DependencyProperty.Register(
            nameof(StageSnapshot), typeof(XyStageSnapshot), typeof(XyStageCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TeachPointsProperty = DependencyProperty.Register(
            nameof(TeachPoints), typeof(IReadOnlyList<TeachPoint>), typeof(XyStageCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SelectedTeachPointProperty = DependencyProperty.Register(
            nameof(SelectedTeachPoint), typeof(TeachPoint), typeof(XyStageCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty PreviewPlanProperty = DependencyProperty.Register(
            nameof(PreviewPlan), typeof(XyTrajectoryPlan), typeof(XyStageCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty WorkspaceWidthMmProperty = DependencyProperty.Register(
            nameof(WorkspaceWidthMm), typeof(double), typeof(XyStageCanvas),
            new FrameworkPropertyMetadata(200.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty WorkspaceHeightMmProperty = DependencyProperty.Register(
            nameof(WorkspaceHeightMm), typeof(double), typeof(XyStageCanvas),
            new FrameworkPropertyMetadata(150.0, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush WorkspaceBg = DofPalette.Solid("#191A28");
        private static readonly Brush HudBg = DofPalette.Solid("#1E1E2E", 0.90);
        private static readonly Brush PointFill = DofPalette.Solid("#24263B");
        private static readonly Brush SelectedPointFill = DofPalette.Solid("#89B4FA", 0.28);
        private static readonly Brush StopCaptureHalo = DofPalette.Solid("#A6E3A1", 0.25);
        private static readonly Brush PsoHalo = DofPalette.Solid("#F9E2AF", 0.28);
        private static readonly Brush HeadHalo = DofPalette.Solid("#89DCEB", 0.26);
        private static readonly Brush HeadAlarmHalo = DofPalette.Solid("#F38BA8", 0.30);

        private static readonly Pen WorkspaceBorderPen = DofPalette.Pen(DofPalette.Solid("#45475A"), 1.5);
        private static readonly Pen MinorGridPen = DofPalette.Pen(DofPalette.Solid("#28293D"), 1);
        private static readonly Pen MajorGridPen = DofPalette.Pen(DofPalette.Solid("#373952"), 1);
        private static readonly Pen RecipeGuidePen = DofPalette.Pen(DofPalette.Solid("#6C7086", 0.55), 1.2, 3, 3);
        private static readonly Pen PreviewPtpPen = DofPalette.Pen(DofPalette.Warn, 1.8, 5, 3);
        private static readonly Pen PreviewLinearPen = DofPalette.Pen(DofPalette.Fov, 1.8, 5, 3);
        private static readonly Pen PreviewArcPen = DofPalette.Pen(DofPalette.Aperture, 1.8, 5, 3);
        private static readonly Pen ArcRadiusPen = DofPalette.Pen(DofPalette.Solid("#CBA6F7", 0.45), 1, 3, 3);
        private static readonly Pen TrailGlowPen = DofPalette.Pen(DofPalette.Solid("#A6E3A1", 0.25), 6);
        private static readonly Pen TrailCorePen = DofPalette.Pen(DofPalette.Dof, 2.0);
        private static readonly Pen BridgePen = DofPalette.Pen(DofPalette.Solid("#89DCEB", 0.24), 1.2, 4, 4);
        private static readonly Pen PointBorderPen = DofPalette.Pen(DofPalette.Accent, 1.4);
        private static readonly Pen SelectedPointBorderPen = DofPalette.Pen(DofPalette.Ray, 2.2);
        private static readonly Pen StopCapturePen = DofPalette.Pen(DofPalette.Dof, 1.8);
        private static readonly Pen PsoBorderPen = DofPalette.Pen(DofPalette.Ray, 1.5);
        private static readonly Pen HeadRingPen = DofPalette.Pen(DofPalette.Fov, 2.0);
        private static readonly Pen HeadAlarmPen = DofPalette.Pen(DofPalette.Warn, 2.2);
        private static readonly Pen HudBorderPen = DofPalette.Pen(DofPalette.Surface, 1);

        private Rect _stageRect;

        public XyStageCanvas()
        {
            ClipToBounds = true;
            Cursor = Cursors.Cross;
        }

        public event Action<Point2D>? StageClicked;

        public XyStageSnapshot? StageSnapshot
        {
            get => (XyStageSnapshot?)GetValue(StageSnapshotProperty);
            set => SetValue(StageSnapshotProperty, value);
        }

        public IReadOnlyList<TeachPoint>? TeachPoints
        {
            get => (IReadOnlyList<TeachPoint>?)GetValue(TeachPointsProperty);
            set => SetValue(TeachPointsProperty, value);
        }

        public TeachPoint? SelectedTeachPoint
        {
            get => (TeachPoint?)GetValue(SelectedTeachPointProperty);
            set => SetValue(SelectedTeachPointProperty, value);
        }

        public XyTrajectoryPlan? PreviewPlan
        {
            get => (XyTrajectoryPlan?)GetValue(PreviewPlanProperty);
            set => SetValue(PreviewPlanProperty, value);
        }

        public double WorkspaceWidthMm
        {
            get => (double)GetValue(WorkspaceWidthMmProperty);
            set => SetValue(WorkspaceWidthMmProperty, value);
        }

        public double WorkspaceHeightMm
        {
            get => (double)GetValue(WorkspaceHeightMmProperty);
            set => SetValue(WorkspaceHeightMmProperty, value);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (_stageRect.Width <= 10 || _stageRect.Height <= 10)
            {
                return;
            }

            var pt = e.GetPosition(this);
            if (!_stageRect.Contains(pt))
            {
                return;
            }

            var x = (pt.X - _stageRect.Left) / _stageRect.Width * WorkspaceWidthMm;
            var y = (_stageRect.Bottom - pt.Y) / _stageRect.Height * WorkspaceHeightMm;
            x = Math.Clamp(Math.Round(x * 2) / 2, 0, WorkspaceWidthMm);
            y = Math.Clamp(Math.Round(y * 2) / 2, 0, WorkspaceHeightMm);
            StageClicked?.Invoke(new Point2D(x, y));
            e.Handled = true;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 180 || h < 160)
            {
                return;
            }

            const double hudH = 34;
            const double padL = 44;
            const double padR = 20;
            const double padB = 28;
            var availW = Math.Max(40, w - padL - padR);
            var availH = Math.Max(40, h - hudH - padB - 10);

            var scale = Math.Min(availW / WorkspaceWidthMm, availH / WorkspaceHeightMm);
            var stageW = WorkspaceWidthMm * scale;
            var stageH = WorkspaceHeightMm * scale;
            var stageL = padL + (availW - stageW) * 0.5;
            var stageT = hudH + 6 + (availH - stageH) * 0.5;
            _stageRect = new Rect(stageL, stageT, stageW, stageH);

            Point Map(double xMm, double yMm) =>
                new(stageL + xMm * scale, stageT + stageH - yMm * scale);

            DrawHud(dc, w);
            DrawGridAndAxes(dc, stageL, stageT, stageW, stageH, Map);
            DrawRecipeGuide(dc, Map);
            DrawPreviewPlan(dc, Map);
            DrawActualTrailAndTriggers(dc, Map);
            DrawTeachPoints(dc, Map);
            DrawGantryHead(dc, stageL, stageT, stageW, stageH, Map);
        }

        private void DrawHud(DrawingContext dc, double w)
        {
            var snap = StageSnapshot;
            var activeMode = (snap?.ActivePlan ?? PreviewPlan)?.Mode switch
            {
                XyMotionMode.IndependentPtp => "独立 PTP (各轴独立到位，折线路径)",
                XyMotionMode.LinearInterpolation => "直线插补 G01 (矢量同步合成)",
                XyMotionMode.CircularArcCw => "顺圆插补 G02",
                XyMotionMode.CircularArcCcw => "逆圆插补 G03",
                _ => "就绪",
            };

            dc.DrawRoundedRectangle(HudBg, HudBorderPen, new Rect(12, 6, w - 24, 24), 6, 6);
            var x = 22.0;
            x += DofText.Draw(dc, this, "XY 龙门平台", 11, DofPalette.Fov, x, 22, bold: true) + 10;
            x += DofText.Draw(dc, this, activeMode, 11, DofPalette.TextMain, x, 22) + 12;
            DofText.Draw(dc, this, "左键点击工作区可快速设定目标坐标", 10, DofPalette.Muted, w - 22, 22, TextAlign.Right);
        }

        private void DrawGridAndAxes(DrawingContext dc, double stageL, double stageT, double stageW, double stageH, Func<double, double, Point> map)
        {
            dc.DrawRectangle(WorkspaceBg, WorkspaceBorderPen, new Rect(stageL, stageT, stageW, stageH));

            for (var x = 0.0; x <= WorkspaceWidthMm + 1e-6; x += 25.0)
            {
                var p0 = map(x, 0);
                var p1 = map(x, WorkspaceHeightMm);
                var major = Math.Abs(x % 50) < 1e-6;
                if (x > 0 && x < WorkspaceWidthMm)
                {
                    dc.DrawLine(major ? MajorGridPen : MinorGridPen, p0, p1);
                }

                if (major)
                {
                    DofText.Draw(dc, this, $"{x:F0}", 10, DofPalette.Subtle, p0.X, stageT + stageH + 14, TextAlign.Center);
                }
            }

            for (var y = 0.0; y <= WorkspaceHeightMm + 1e-6; y += 25.0)
            {
                var p0 = map(0, y);
                var p1 = map(WorkspaceWidthMm, y);
                var major = Math.Abs(y % 50) < 1e-6;
                if (y > 0 && y < WorkspaceHeightMm)
                {
                    dc.DrawLine(major ? MajorGridPen : MinorGridPen, p0, p1);
                }

                if (major)
                {
                    DofText.Draw(dc, this, $"{y:F0}", 10, DofPalette.Subtle, stageL - 6, p0.Y + 3, TextAlign.Right);
                }
            }

            DofText.Draw(dc, this, "X (mm) →", 10, DofPalette.Muted, stageL + stageW, stageT + stageH + 14, TextAlign.Right);
            DofText.Draw(dc, this, "↑ Y", 10, DofPalette.Muted, stageL - 6, stageT + 10, TextAlign.Right);

            var origin = map(0, 0);
            dc.DrawEllipse(DofPalette.Ray, null, origin, 3.5, 3.5);
        }

        private void DrawRecipeGuide(DrawingContext dc, Func<double, double, Point> map)
        {
            if (TeachPoints is not { Count: >= 2 } points)
            {
                return;
            }

            for (var i = 1; i < points.Count; i++)
            {
                var prev = points[i - 1];
                var curr = points[i];
                var plan = XyTrajectoryPlanner.Plan(
                    new Point2D(prev.X, prev.Y), new Point2D(curr.X, curr.Y),
                    curr.Mode, 120, 500, 2500, TrajectoryProfileType.Trapezoidal, curr.ArcRadiusMm);
                DrawPolyline(dc, plan.SamplePolyline(40), map, RecipeGuidePen);
            }
        }

        private void DrawPreviewPlan(DrawingContext dc, Func<double, double, Point> map)
        {
            var plan = StageSnapshot?.State == AxisState.DiscreteMotion
                ? StageSnapshot.ActivePlan
                : PreviewPlan;

            if (plan is not { TotalDurationSec: > 0 })
            {
                return;
            }

            var pen = plan.Mode switch
            {
                XyMotionMode.IndependentPtp => PreviewPtpPen,
                XyMotionMode.LinearInterpolation => PreviewLinearPen,
                _ => PreviewArcPen,
            };

            DrawPolyline(dc, plan.SamplePolyline(96), map, pen);

            // Show arc center and radii for circular interpolation
            if (plan.ArcCenter is { } center)
            {
                var cp = map(center.X, center.Y);
                var sp = map(plan.Start.X, plan.Start.Y);
                var ep = map(plan.End.X, plan.End.Y);
                dc.DrawLine(ArcRadiusPen, cp, sp);
                dc.DrawLine(ArcRadiusPen, cp, ep);
                dc.DrawEllipse(DofPalette.Aperture, null, cp, 3, 3);
                DofText.Draw(dc, this, $"C({center.X:F0},{center.Y:F0}) R={plan.ArcRadiusMm:F1}", 10, DofPalette.Aperture, cp.X + 6, cp.Y + 3);
            }
        }

        private void DrawActualTrailAndTriggers(DrawingContext dc, Func<double, double, Point> map)
        {
            if (StageSnapshot is not { } snap)
            {
                return;
            }

            if (snap.Trail.Count >= 2)
            {
                DrawPolyline(dc, snap.Trail, map, TrailGlowPen);
                DrawPolyline(dc, snap.Trail, map, TrailCorePen);
            }

            foreach (var trig in snap.Triggers)
            {
                var p = map(trig.Position.X, trig.Position.Y);
                if (trig.IsPso)
                {
                    dc.DrawEllipse(PsoHalo, null, p, 7, 7);
                    var diamond = new StreamGeometry();
                    using (var g = diamond.Open())
                    {
                        g.BeginFigure(new Point(p.X, p.Y - 4), true, true);
                        g.LineTo(new Point(p.X + 4, p.Y), false, false);
                        g.LineTo(new Point(p.X, p.Y + 4), false, false);
                        g.LineTo(new Point(p.X - 4, p.Y), false, false);
                    }

                    diamond.Freeze();
                    dc.DrawGeometry(DofPalette.Ray, PsoBorderPen, diamond);
                }
                else
                {
                    dc.DrawEllipse(StopCaptureHalo, StopCapturePen, p, 9, 9);
                    dc.DrawEllipse(DofPalette.Dof, null, p, 2.5, 2.5);
                }
            }
        }

        private void DrawTeachPoints(DrawingContext dc, Func<double, double, Point> map)
        {
            if (TeachPoints is not { Count: > 0 } points)
            {
                return;
            }

            foreach (var tp in points)
            {
                var p = map(tp.X, tp.Y);
                var isSelected = SelectedTeachPoint is not null && SelectedTeachPoint.Index == tp.Index;
                dc.DrawEllipse(isSelected ? SelectedPointFill : PointFill,
                    isSelected ? SelectedPointBorderPen : PointBorderPen, p, 9, 9);
                DofText.Draw(dc, this, tp.Index.ToString(), 9, DofPalette.White, p.X, p.Y + 3, TextAlign.Center, bold: true);
            }
        }

        private void DrawGantryHead(DrawingContext dc, double stageL, double stageT, double stageW, double stageH, Func<double, double, Point> map)
        {
            if (StageSnapshot is not { } snap)
            {
                return;
            }

            var p = map(snap.ActualX, snap.ActualY);
            var isAlarm = snap.AlarmCode != MotionAlarmCode.None;

            // Gantry cross-beams
            dc.DrawLine(BridgePen, new Point(p.X, stageT), new Point(p.X, stageT + stageH));
            dc.DrawLine(BridgePen, new Point(stageL, p.Y), new Point(stageL + stageW, p.Y));

            // Head marker
            dc.DrawEllipse(isAlarm ? HeadAlarmHalo : HeadHalo, null, p, 12, 12);
            dc.DrawEllipse(DofPalette.Canvas, isAlarm ? HeadAlarmPen : HeadRingPen, p, 6, 6);
            dc.DrawEllipse(isAlarm ? DofPalette.Warn : DofPalette.White, null, p, 2.2, 2.2);

            var label = $"({snap.ActualX:F1}, {snap.ActualY:F1})";
            var align = p.X > stageL + stageW - 95 ? TextAlign.Right : TextAlign.Left;
            var lx = align == TextAlign.Right ? p.X - 12 : p.X + 12;
            var ly = p.Y < stageT + 22 ? p.Y + 16 : p.Y - 10;
            DofText.Draw(dc, this, label, 10, DofPalette.Fov, lx, ly, align, bold: true);
        }

        private static void DrawPolyline(DrawingContext dc, IReadOnlyList<Point2D> points, Func<double, double, Point> map, Pen pen)
        {
            if (points.Count < 2)
            {
                return;
            }

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(map(points[0].X, points[0].Y), false, false);
                for (var i = 1; i < points.Count; i++)
                {
                    ctx.LineTo(map(points[i].X, points[i].Y), true, false);
                }
            }

            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }
}
