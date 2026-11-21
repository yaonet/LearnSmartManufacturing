using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Async;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Live multi-device parallel execution Gantt chart showing SemaphoreSlim queue waits,
    /// concurrent running bars with managed Thread IDs, timeouts, faults and cancellations.
    /// </summary>
    public class ConcurrencyGanttCanvas : FrameworkElement
    {
        public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(
            nameof(Snapshot), typeof(OrchestrationSnapshot), typeof(ConcurrencyGanttCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush LaneBg = DofPalette.Solid("#1B1C2B");
        private static readonly Brush HudBg = DofPalette.Solid("#1E1E2E", 0.90);
        private static readonly Brush QueuedBarFill = DofPalette.Solid("#6C7086", 0.35);
        private static readonly Brush RunningBarFill = DofPalette.Solid("#89DCEB", 0.45);
        private static readonly Brush SucceededBarFill = DofPalette.Solid("#A6E3A1", 0.45);
        private static readonly Brush TimedOutBarFill = DofPalette.Solid("#F9E2AF", 0.45);
        private static readonly Brush FaultedBarFill = DofPalette.Solid("#F38BA8", 0.45);
        private static readonly Brush CanceledBarFill = DofPalette.Solid("#CBA6F7", 0.35);

        private static readonly Pen LaneBorderPen = DofPalette.Pen(DofPalette.Surface, 1);
        private static readonly Pen GridPen = DofPalette.Pen(DofPalette.Solid("#313244", 0.5), 1);
        private static readonly Pen CursorPen = DofPalette.Pen(DofPalette.White, 1.5, 4, 3);
        private static readonly Pen RunningPen = DofPalette.Pen(DofPalette.Fov, 1.4);
        private static readonly Pen SucceededPen = DofPalette.Pen(DofPalette.Dof, 1.4);
        private static readonly Pen TimedOutPen = DofPalette.Pen(DofPalette.Ray, 1.4);
        private static readonly Pen FaultedPen = DofPalette.Pen(DofPalette.Warn, 1.5);
        private static readonly Pen CanceledPen = DofPalette.Pen(DofPalette.Aperture, 1.4);

        public ConcurrencyGanttCanvas()
        {
            ClipToBounds = true;
        }

        public OrchestrationSnapshot? Snapshot
        {
            get => (OrchestrationSnapshot?)GetValue(SnapshotProperty);
            set => SetValue(SnapshotProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 240 || h < 180)
            {
                return;
            }

            if (Snapshot is not { Devices.Count: > 0 } snap)
            {
                DofText.Draw(dc, this, "点击“启动并行巡检”查看 SemaphoreSlim 并发泳道甘特图", 13, DofPalette.Muted, w / 2, h / 2, TextAlign.Center);
                return;
            }

            const double pad = 12;
            const double hudH = 32;
            const double axisH = 22;
            const double labelW = 166;
            const double detailW = 126;

            // HUD
            var speedup = snap.ElapsedSec > 0.05 ? snap.TheoreticalSerialSec / snap.ElapsedSec : 1.0;
            dc.DrawRoundedRectangle(HudBg, LaneBorderPen, new Rect(pad, 6, w - pad * 2, 24), 6, 6);
            var hx = pad + 10.0;
            hx += DofText.Draw(dc, this, $"限流 SemaphoreSlim({snap.MaxConcurrency})", 11, DofPalette.Fov, hx, 22, bold: true) + 14;
            hx += DofText.Draw(dc, this, $"峰值并发 {snap.PeakConcurrency} · 实测 {snap.ElapsedSec:F2}s (串行理论 {snap.TheoreticalSerialSec:F2}s)",
                11, DofPalette.TextMain, hx, 22) + 12;
            if (snap.ElapsedSec > 0.05)
            {
                DofText.Draw(dc, this, $"加速 {speedup:F2}×", 11, DofPalette.Dof, w - pad - 10, 22, TextAlign.Right, bold: true);
            }

            var plotL = pad + labelW;
            var plotR = Math.Max(plotL + 60, w - pad - detailW);
            var plotW = plotR - plotL;
            var plotT = hudH + 6;
            var plotB = h - axisH;
            var count = snap.Devices.Count;
            const double laneGap = 5;
            var laneH = Math.Max(18, (plotB - plotT - laneGap * (count - 1)) / count);

            var timeSpan = Math.Max(1.6, Math.Ceiling(Math.Max(snap.ElapsedSec, 1.2) * 2.0) / 2.0);
            double MapX(double tSec) => plotL + Math.Clamp(tSec / timeSpan, 0, 1) * plotW;

            // Background vertical gridlines
            for (var i = 0; i <= 4; i++)
            {
                var frac = i / 4.0;
                var gx = plotL + plotW * frac;
                if (i > 0 && i < 4)
                {
                    dc.DrawLine(GridPen, new Point(gx, plotT), new Point(gx, plotB));
                }

                var align = i == 0 ? TextAlign.Left : (i == 4 ? TextAlign.Right : TextAlign.Center);
                DofText.Draw(dc, this, $"{timeSpan * frac:F2} s", 10, DofPalette.Muted, gx, h - 6, align);
            }

            // Swimlanes
            for (var i = 0; i < count; i++)
            {
                var dev = snap.Devices[i];
                var y = plotT + i * (laneH + laneGap);
                var laneRect = new Rect(pad, y, w - pad * 2, laneH);
                dc.DrawRoundedRectangle(LaneBg, LaneBorderPen, laneRect, 5, 5);

                var midY = y + laneH * 0.5 + 3.5;
                DofText.Draw(dc, this, dev.Name, 10.5, DofPalette.TextMain, pad + 8, midY, bold: dev.Status == DeviceTaskStatus.Running);

                // Queued wait bar (gray)
                if (dev.QueuedAtSec >= 0)
                {
                    var qEnd = dev.StartedAtSec >= 0 ? dev.StartedAtSec : snap.ElapsedSec;
                    var x0 = MapX(dev.QueuedAtSec);
                    var x1 = MapX(qEnd);
                    if (x1 > x0 + 1)
                    {
                        dc.DrawRoundedRectangle(QueuedBarFill, null, new Rect(x0, y + 5, x1 - x0, laneH - 10), 2, 2);
                    }
                }

                // Active / Finished execution bar
                if (dev.StartedAtSec >= 0 && dev.Status != DeviceTaskStatus.Idle && dev.Status != DeviceTaskStatus.Queued)
                {
                    var endT = dev.FinishedAtSec >= 0 ? dev.FinishedAtSec : snap.ElapsedSec;
                    var x0 = MapX(dev.StartedAtSec);
                    var x1 = Math.Max(x0 + 4, MapX(endT));
                    var (fill, pen) = dev.Status switch
                    {
                        DeviceTaskStatus.Running => (RunningBarFill, RunningPen),
                        DeviceTaskStatus.Succeeded => (SucceededBarFill, SucceededPen),
                        DeviceTaskStatus.TimedOut => (TimedOutBarFill, TimedOutPen),
                        DeviceTaskStatus.Faulted => (FaultedBarFill, FaultedPen),
                        DeviceTaskStatus.Canceled => (CanceledBarFill, CanceledPen),
                        _ => (QueuedBarFill, LaneBorderPen),
                    };

                    var barRect = new Rect(x0, y + 3, x1 - x0, laneH - 6);
                    dc.DrawRoundedRectangle(fill, pen, barRect, 4, 4);

                    if (dev.WorkerThreadId > 0 && barRect.Width >= 34)
                    {
                        DofText.Draw(dc, this, $"T{dev.WorkerThreadId:D2}", 9.5, DofPalette.White,
                            barRect.Left + barRect.Width * 0.5, midY, TextAlign.Center, bold: true);
                    }
                }

                // Right status/detail text
                var detailBrush = dev.Status switch
                {
                    DeviceTaskStatus.Running => DofPalette.Fov,
                    DeviceTaskStatus.Succeeded => DofPalette.Dof,
                    DeviceTaskStatus.TimedOut => DofPalette.Ray,
                    DeviceTaskStatus.Faulted => DofPalette.Warn,
                    DeviceTaskStatus.Canceled => DofPalette.Aperture,
                    _ => DofPalette.Subtle,
                };
                DofText.Draw(dc, this, dev.Detail, 10, detailBrush, w - pad - 8, midY, TextAlign.Right);
            }

            // Live elapsed cursor
            if (snap.IsRunning)
            {
                var cx = MapX(snap.ElapsedSec);
                dc.DrawLine(CursorPen, new Point(cx, plotT), new Point(cx, plotB));
            }
        }
    }
}
