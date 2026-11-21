using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Motion;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// 4-channel synchronized P-V-A-J oscilloscope comparing S-curve and trapezoidal motion profiles
    /// with a live playback cursor during simulated execution.
    /// </summary>
    public class MotionProfileCanvas : FrameworkElement
    {
        public static readonly DependencyProperty PrimaryPlanProperty = DependencyProperty.Register(
            nameof(PrimaryPlan), typeof(TrajectoryPlan), typeof(MotionProfileCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ComparisonPlanProperty = DependencyProperty.Register(
            nameof(ComparisonPlan), typeof(TrajectoryPlan), typeof(MotionProfileCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowComparisonProperty = DependencyProperty.Register(
            nameof(ShowComparison), typeof(bool), typeof(MotionProfileCanvas),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty CurrentTimeSecProperty = DependencyProperty.Register(
            nameof(CurrentTimeSec), typeof(double), typeof(MotionProfileCanvas),
            new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush TrackBg = DofPalette.Solid("#1A1B2A");
        private static readonly Brush HudBg = DofPalette.Solid("#1E1E2E", 0.90);

        private static readonly Pen TrackBorderPen = DofPalette.Pen(DofPalette.Surface, 1);
        private static readonly Pen GridPen = DofPalette.Pen(DofPalette.Solid("#313244", 0.45), 1);
        private static readonly Pen ZeroAxisPen = DofPalette.Pen(DofPalette.Solid("#585B70", 0.75), 1, 4, 3);
        private static readonly Pen SegmentLinePen = DofPalette.Pen(DofPalette.Solid("#6C7086", 0.35), 1, 2, 3);
        private static readonly Pen CursorPen = DofPalette.Pen(DofPalette.White, 1.5);

        private static readonly Pen PosPen = DofPalette.Pen(DofPalette.Fov, 2.0);
        private static readonly Pen VelPen = DofPalette.Pen(DofPalette.Dof, 2.0);
        private static readonly Pen AccPen = DofPalette.Pen(DofPalette.Ray, 2.0);
        private static readonly Pen JerkPen = DofPalette.Pen(DofPalette.Aperture, 2.0);

        private static readonly Pen PosCompPen = DofPalette.Pen(DofPalette.WithOpacity(DofPalette.Fov, 0.45), 1.3, 4, 3);
        private static readonly Pen VelCompPen = DofPalette.Pen(DofPalette.WithOpacity(DofPalette.Dof, 0.45), 1.3, 4, 3);
        private static readonly Pen AccCompPen = DofPalette.Pen(DofPalette.WithOpacity(DofPalette.Ray, 0.45), 1.3, 4, 3);
        private static readonly Pen JerkImpulsePen = DofPalette.Pen(DofPalette.WithOpacity(DofPalette.Warn, 0.72), 1.6, 3, 2);

        public MotionProfileCanvas()
        {
            ClipToBounds = true;
        }

        public TrajectoryPlan? PrimaryPlan
        {
            get => (TrajectoryPlan?)GetValue(PrimaryPlanProperty);
            set => SetValue(PrimaryPlanProperty, value);
        }

        public TrajectoryPlan? ComparisonPlan
        {
            get => (TrajectoryPlan?)GetValue(ComparisonPlanProperty);
            set => SetValue(ComparisonPlanProperty, value);
        }

        public bool ShowComparison
        {
            get => (bool)GetValue(ShowComparisonProperty);
            set => SetValue(ShowComparisonProperty, value);
        }

        public double CurrentTimeSec
        {
            get => (double)GetValue(CurrentTimeSecProperty);
            set => SetValue(CurrentTimeSecProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));

            if (w < 160 || h < 180)
            {
                return;
            }

            if (PrimaryPlan is not { TotalDurationSec: > 0 } primary)
            {
                DofText.Draw(dc, this, "请输入非零位移以预览 P-V-A-J 运动规划曲线", 13, DofPalette.Muted, w / 2, h / 2, TextAlign.Center);
                return;
            }

            var comp = ShowComparison && ComparisonPlan is { TotalDurationSec: > 0 } c ? c : null;
            var totalT = comp is not null ? Math.Max(primary.TotalDurationSec, comp.TotalDurationSec) : primary.TotalDurationSec;

            const double hudH = 34;
            const double bottomAxisH = 22;
            const double padL = 12;
            const double padR = 14;
            const double trackGap = 8;

            DrawHud(dc, w, padL, primary, comp);

            var plotTop = hudH + 4;
            var plotBottom = h - bottomAxisH;
            var availH = plotBottom - plotTop - trackGap * 3;
            if (availH < 80)
            {
                return;
            }

            var trackH = availH / 4.0;
            var plotL = padL + 108;
            var plotR = w - padR;
            var plotW = Math.Max(40, plotR - plotL);

            double MapX(double t) => plotL + Math.Clamp(t / totalT, 0, 1) * plotW;

            var sampleCount = Math.Clamp((int)(plotW / 2), 160, 480);
            var primarySamples = primary.SampleSeries(sampleCount);
            var compSamples = comp?.SampleSeries(sampleCount);

            var maxPos = Math.Max(1e-3, Math.Abs(primary.TotalDistanceMm));
            var maxVel = Math.Max(1e-3, Math.Max(primary.PeakVelocityMmPerSec, comp?.PeakVelocityMmPerSec ?? 0));
            var maxAcc = Math.Max(1e-3, Math.Max(primary.PeakAccelerationMmPerSec2, comp?.PeakAccelerationMmPerSec2 ?? 0));
            var maxJerk = Math.Max(1e-3, Math.Max(primary.PeakJerkMmPerSec3, comp?.PeakJerkMmPerSec3 ?? 0));

            var hasCursor = CurrentTimeSec >= 0 && CurrentTimeSec <= primary.TotalDurationSec;
            var cursorSample = hasCursor ? primary.Evaluate(CurrentTimeSec) : default;
            var cursorX = hasCursor ? MapX(CurrentTimeSec) : -1;

            DrawTrack(dc, 0, "位置 P", "mm", DofPalette.Fov, PosPen, PosCompPen,
                primarySamples, compSamples, s => s.PositionMm, primary.TotalDistanceMm >= 0 ? 0 : -maxPos, primary.TotalDistanceMm >= 0 ? maxPos : 0,
                hasCursor ? cursorSample.PositionMm : null, primary, totalT, plotTop, trackH, trackGap, padL, plotL, plotR, MapX, cursorX, drawTImpulses: false, comp);

            var signedVelMin = primary.TotalDistanceMm >= 0 ? 0 : -maxVel;
            var signedVelMax = primary.TotalDistanceMm >= 0 ? maxVel : 0;
            DrawTrack(dc, 1, "速度 V", "mm/s", DofPalette.Dof, VelPen, VelCompPen,
                primarySamples, compSamples, s => s.VelocityMmPerSec, signedVelMin, signedVelMax,
                hasCursor ? cursorSample.VelocityMmPerSec : null, primary, totalT, plotTop, trackH, trackGap, padL, plotL, plotR, MapX, cursorX, drawTImpulses: false, comp);

            DrawTrack(dc, 2, "加速度 A", "mm/s²", DofPalette.Ray, AccPen, AccCompPen,
                primarySamples, compSamples, s => s.AccelerationMmPerSec2, -maxAcc, maxAcc,
                hasCursor ? cursorSample.AccelerationMmPerSec2 : null, primary, totalT, plotTop, trackH, trackGap, padL, plotL, plotR, MapX, cursorX, drawTImpulses: false, comp);

            DrawTrack(dc, 3, "加加速度 J", "mm/s³", DofPalette.Aperture, JerkPen, JerkImpulsePen,
                primarySamples, compSamples, s => s.JerkMmPerSec3, -maxJerk, maxJerk,
                hasCursor ? cursorSample.JerkMmPerSec3 : null, primary, totalT, plotTop, trackH, trackGap, padL, plotL, plotR, MapX, cursorX, drawTImpulses: true, comp);

            // Shared horizontal time axis
            for (var i = 0; i <= 4; i++)
            {
                var frac = i / 4.0;
                var tx = plotL + plotW * frac;
                var tVal = totalT * frac;
                var align = i == 0 ? TextAlign.Left : (i == 4 ? TextAlign.Right : TextAlign.Center);
                DofText.Draw(dc, this, $"{tVal:F3} s", 10, DofPalette.Muted, tx, h - 6, align);
            }
        }

        private void DrawHud(DrawingContext dc, double w, double padL, TrajectoryPlan primary, TrajectoryPlan? comp)
        {
            var isS = primary.ProfileType == TrajectoryProfileType.SCurve;
            var primaryLabel = isS ? $"主曲线: S型 ({primary.Segments.Count}段)" : $"主曲线: T型 ({primary.Segments.Count}段)";
            var cruiseLabel = primary.ReachedCruiseVelocity ? "达巡航速" : "短行程退化";
            var summary = $"T={primary.TotalDurationSec:F3}s · Vp={primary.PeakVelocityMmPerSec:F1}mm/s · {cruiseLabel}";

            var compText = string.Empty;
            if (comp is not null)
            {
                var dtMs = (primary.TotalDurationSec - comp.TotalDurationSec) * 1000.0;
                var compName = comp.ProfileType == TrajectoryProfileType.SCurve ? "S型" : "T型";
                compText = $"  |  虚线对比 {compName}: T={comp.TotalDurationSec:F3}s (ΔT={dtMs:+0.0;-0.0;0.0} ms)";
            }

            dc.DrawRoundedRectangle(HudBg, TrackBorderPen, new Rect(padL, 6, w - padL * 2 - 2, 24), 6, 6);
            var x = padL + 10;
            x += DofText.Draw(dc, this, primaryLabel, 11, isS ? DofPalette.Dof : DofPalette.Ray, x, 22, bold: true) + 12;
            x += DofText.Draw(dc, this, summary, 11, DofPalette.TextMain, x, 22);
            if (!string.IsNullOrEmpty(compText))
            {
                DofText.Draw(dc, this, compText, 11, DofPalette.Muted, x, 22);
            }
        }

        private void DrawTrack(
            DrawingContext dc,
            int index,
            string title,
            string unit,
            Brush accent,
            Pen primaryPen,
            Pen compPen,
            IReadOnlyList<TrajectorySample> primarySamples,
            IReadOnlyList<TrajectorySample>? compSamples,
            Func<TrajectorySample, double> selector,
            double minVal,
            double maxVal,
            double? cursorVal,
            TrajectoryPlan primary,
            double totalT,
            double plotTop,
            double trackH,
            double trackGap,
            double padL,
            double plotL,
            double plotR,
            Func<double, double> mapX,
            double cursorX,
            bool drawTImpulses,
            TrajectoryPlan? comp)
        {
            var top = plotTop + index * (trackH + trackGap);
            var rect = new Rect(padL, top, plotR - padL, trackH);
            dc.DrawRoundedRectangle(TrackBg, TrackBorderPen, rect, 6, 6);

            const double padY = 8;
            var innerTop = top + padY;
            var innerBottom = top + trackH - padY;
            var innerH = Math.Max(10, innerBottom - innerTop);
            var range = Math.Max(1e-6, maxVal - minVal);

            double MapY(double v) => innerBottom - (v - minVal) / range * innerH;

            // Vertical time gridlines
            for (var i = 1; i <= 3; i++)
            {
                var gx = plotL + (plotR - plotL) * i / 4.0;
                dc.DrawLine(GridPen, new Point(gx, top + 2), new Point(gx, top + trackH - 2));
            }

            // Segment boundaries of primary profile
            for (var i = 0; i < primary.Segments.Count - 1; i++)
            {
                var sx = mapX(primary.Segments[i].EndTimeSec);
                dc.DrawLine(SegmentLinePen, new Point(sx, top + 2), new Point(sx, top + trackH - 2));
            }

            // Zero baseline
            if (minVal <= 0 && maxVal >= 0)
            {
                var y0 = MapY(0);
                dc.DrawLine(ZeroAxisPen, new Point(plotL, y0), new Point(plotR - 2, y0));
            }

            // Left label & peak/live readout
            DofText.Draw(dc, this, title, 11, accent, padL + 8, top + 16, bold: true);
            var peakDisplay = Math.Max(Math.Abs(minVal), Math.Abs(maxVal));
            string peakText;
            if (drawTImpulses && primary.ProfileType == TrajectoryProfileType.Trapezoidal)
            {
                peakText = "∞ (冲击)";
            }
            else if (cursorVal is { } cv)
            {
                peakText = $"{cv:F1} {unit}";
            }
            else
            {
                peakText = minVal >= 0 ? $"{maxVal:F1} {unit}" : $"±{peakDisplay:F0} {unit}";
            }

            DofText.Draw(dc, this, peakText, 10, cursorVal is not null ? DofPalette.White : DofPalette.Muted, padL + 8, top + 31);

            // Comparison curve (dashed)
            if (compSamples is not null && comp is not null)
            {
                if (drawTImpulses && comp.ProfileType == TrajectoryProfileType.Trapezoidal)
                {
                    DrawTrapezoidalJerkImpulses(dc, comp, mapX, innerTop, innerBottom);
                }
                else
                {
                    DrawPolyline(dc, compSamples, selector, mapX, MapY, compPen);
                }
            }

            // Primary curve (solid)
            if (drawTImpulses && primary.ProfileType == TrajectoryProfileType.Trapezoidal)
            {
                DrawTrapezoidalJerkImpulses(dc, primary, mapX, innerTop, innerBottom);
                DofText.Draw(dc, this, "T型加速度阶跃 → 理论冲击 J → ∞", 10, DofPalette.Warn, plotR - 8, top + 15, TextAlign.Right);
            }
            else
            {
                DrawPolyline(dc, primarySamples, selector, mapX, MapY, primaryPen);
            }

            // Live playback cursor
            if (cursorX >= plotL && cursorVal is { } vNow)
            {
                dc.DrawLine(CursorPen, new Point(cursorX, top + 2), new Point(cursorX, top + trackH - 2));
                var cy = Math.Clamp(MapY(vNow), innerTop, innerBottom);
                dc.DrawEllipse(accent, CursorPen, new Point(cursorX, cy), 4, 4);
            }
        }

        private static void DrawPolyline(
            DrawingContext dc,
            IReadOnlyList<TrajectorySample> samples,
            Func<TrajectorySample, double> selector,
            Func<double, double> mapX,
            Func<double, double> mapY,
            Pen pen)
        {
            if (samples.Count < 2)
            {
                return;
            }

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(mapX(samples[0].TimeSec), mapY(selector(samples[0]))), false, false);
                for (var i = 1; i < samples.Count; i++)
                {
                    ctx.LineTo(new Point(mapX(samples[i].TimeSec), mapY(selector(samples[i]))), true, false);
                }
            }

            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        private static void DrawTrapezoidalJerkImpulses(
            DrawingContext dc,
            TrajectoryPlan plan,
            Func<double, double> mapX,
            double topY,
            double bottomY)
        {
            // Acceleration steps at t = 0, end of each segment (Dirac delta spikes in jerk).
            var yMid = (topY + bottomY) * 0.5;
            var dir = Math.Sign(plan.TotalDistanceMm);
            if (dir == 0)
            {
                return;
            }

            var times = new List<(double Time, int SpikeDir)> { (0, dir) };
            for (var i = 0; i < plan.Segments.Count; i++)
            {
                var currAcc = plan.Segments[i].StartAccelerationMmPerSec2;
                var nextAcc = i + 1 < plan.Segments.Count ? plan.Segments[i + 1].StartAccelerationMmPerSec2 : 0;
                var dAcc = nextAcc - currAcc;
                if (Math.Abs(dAcc) > 1e-6)
                {
                    times.Add((plan.Segments[i].EndTimeSec, Math.Sign(dAcc)));
                }
            }

            foreach (var (t, spikeDir) in times)
            {
                var x = mapX(t);
                var tipY = spikeDir > 0 ? topY + 2 : bottomY - 2;
                dc.DrawLine(JerkImpulsePen, new Point(x, yMid), new Point(x, tipY));
                dc.DrawEllipse(DofPalette.Warn, null, new Point(x, tipY), 2.5, 2.5);
            }
        }
    }
}
