using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.EtherCat;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Real-time Distributed Clocks (DC) SYNC0 phase offset oscilloscope plotting per-slave nanosecond
    /// clock skew relative to the Reference Clock (EK1100), with automatic Y-axis scaling to contrast
    /// DC-locked (±15~65 ns) vs unsynchronized (±10~25 µs) behavior.
    /// </summary>
    public class DcJitterCanvas : FrameworkElement
    {
        public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
            nameof(Samples), typeof(IReadOnlyList<DcJitterSample>), typeof(DcJitterCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty DcEnabledProperty = DependencyProperty.Register(
            nameof(DcEnabled), typeof(bool), typeof(DcJitterCanvas),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush SafeBandFill = DofPalette.Solid("#A6E3A1", 0.08);
        private static readonly Pen GridPen = DofPalette.Pen(DofPalette.Surface, 1.0);
        private static readonly Pen ZeroPen = DofPalette.Pen(DofPalette.Subtle, 1.1, dash: 4, gap: 4);
        private static readonly Pen LimitPen = DofPalette.Pen(DofPalette.Warn, 1.0, dash: 3, gap: 3);
        private static readonly Pen Slave1Pen = DofPalette.Pen(DofPalette.Fov, 1.6);
        private static readonly Pen Slave2Pen = DofPalette.Pen(DofPalette.Ray, 1.6);
        private static readonly Pen Slave3Pen = DofPalette.Pen(DofPalette.Aperture, 1.5);

        public DcJitterCanvas()
        {
            ClipToBounds = true;
        }

        public IReadOnlyList<DcJitterSample>? Samples
        {
            get => (IReadOnlyList<DcJitterSample>?)GetValue(SamplesProperty);
            set => SetValue(SamplesProperty, value);
        }

        public bool DcEnabled
        {
            get => (bool)GetValue(DcEnabledProperty);
            set => SetValue(DcEnabledProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 220 || h < 110)
            {
                return;
            }

            const double padLeft = 56;
            const double padRight = 14;
            const double padTop = 26;
            const double padBottom = 16;

            var plotW = w - padLeft - padRight;
            var plotH = h - padTop - padBottom;
            if (plotW <= 20 || plotH <= 20)
            {
                return;
            }

            var samples = Samples;
            var peak = samples is { Count: > 0 } ? samples.Max(s => s.MaxAbsOffsetNs) : 50;
            var yMax = ChooseNiceScale(peak);

            // Header bar with legend and active range badge
            DofText.Draw(dc, this, "DC SYNC0 同步偏差示波", 10.5, DofPalette.White, 12, 16, bold: true);
            DrawLegendDot(dc, 168, 12, DofPalette.Fov, "X伺服");
            DrawLegendDot(dc, 224, 12, DofPalette.Ray, "Y伺服");
            DrawLegendDot(dc, 280, 12, DofPalette.Aperture, "IO从站");

            var rangeBadge = DcEnabled
                ? $"量程 ±{FormatNs(yMax)} · DC PI 锁相开启"
                : $"量程 ±{FormatNs(yMax)} · DC 关闭 (自由晶振漂移)";
            DofText.Draw(dc, this, rangeBadge, 9.5, DcEnabled && peak <= 1000 ? DofPalette.Dof : DofPalette.Warn,
                w - padRight, 16, TextAlign.Right, bold: true);

            double MapY(double ns) => padTop + plotH * 0.5 - Math.Clamp(ns / yMax, -1.0, 1.0) * (plotH * 0.46);

            // Safe ±100 ns precision band when zoomed in
            if (yMax <= 500)
            {
                var bandTop = MapY(100);
                var bandBot = MapY(-100);
                dc.DrawRectangle(SafeBandFill, null, new Rect(padLeft, bandTop, plotW, bandBot - bandTop));
            }

            // Grid lines + Y-axis labels
            double[] ticks = [yMax, yMax * 0.5, 0, -yMax * 0.5, -yMax];
            foreach (var t in ticks)
            {
                var ty = MapY(t);
                dc.DrawLine(t == 0 ? ZeroPen : GridPen, new Point(padLeft, ty), new Point(padLeft + plotW, ty));
                var label = t == 0 ? "0 ns" : (t > 0 ? "+" : "-") + FormatNs(Math.Abs(t));
                DofText.Draw(dc, this, label, 8.5, DofPalette.Muted, padLeft - 6, ty + 3, TextAlign.Right);
            }

            // Draw ±1000 ns warning thresholds when zoomed out
            if (yMax >= 2000)
            {
                var limTop = MapY(1000);
                var limBot = MapY(-1000);
                dc.DrawLine(LimitPen, new Point(padLeft, limTop), new Point(padLeft + plotW, limTop));
                dc.DrawLine(LimitPen, new Point(padLeft, limBot), new Point(padLeft + plotW, limBot));
            }

            if (samples is null || samples.Count < 2)
            {
                DofText.Draw(dc, this, "点击“单步周期”或“启动周期交换”采集分布式时钟波形",
                    10.5, DofPalette.Muted, padLeft + plotW * 0.5, padTop + plotH * 0.5 + 4, TextAlign.Center);
                return;
            }

            var count = samples.Count;
            var dx = plotW / Math.Max(1, count - 1);

            DrawSeries(dc, samples, padLeft, dx, s => MapY(s.Slave3OffsetNs), Slave3Pen);
            DrawSeries(dc, samples, padLeft, dx, s => MapY(s.Slave2OffsetNs), Slave2Pen);
            DrawSeries(dc, samples, padLeft, dx, s => MapY(s.Slave1OffsetNs), Slave1Pen);
        }

        private static void DrawSeries(
            DrawingContext dc,
            IReadOnlyList<DcJitterSample> samples,
            double startX,
            double dx,
            Func<DcJitterSample, double> ySelector,
            Pen pen)
        {
            var prev = new Point(startX, ySelector(samples[0]));
            for (var i = 1; i < samples.Count; i++)
            {
                var next = new Point(startX + i * dx, ySelector(samples[i]));
                dc.DrawLine(pen, prev, next);
                prev = next;
            }
        }

        private void DrawLegendDot(DrawingContext dc, double x, double y, Brush brush, string label)
        {
            dc.DrawEllipse(brush, null, new Point(x, y), 3.5, 3.5);
            DofText.Draw(dc, this, label, 9, DofPalette.Muted, x + 7, y + 3.5);
        }

        private static double ChooseNiceScale(double peakNs)
        {
            if (peakNs <= 85) return 100;
            if (peakNs <= 420) return 500;
            if (peakNs <= 1800) return 2000;
            if (peakNs <= 9000) return 10_000;
            if (peakNs <= 26_000) return 30_000;
            return Math.Ceiling(peakNs / 10_000.0) * 10_000.0;
        }

        private static string FormatNs(double ns) => ns >= 1000 ? $"{ns / 1000.0:0.#} µs" : $"{ns:0} ns";
    }
}
