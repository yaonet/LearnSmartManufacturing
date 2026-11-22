using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.OpcUa;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Scrolling oscilloscope contrasting Server-side PLC sampling against Absolute Deadband filtering
    /// and batched OPC UA Subscription DataChangeNotification / KeepAlive pushes.
    /// </summary>
    public class OpcUaSubscriptionCanvas : FrameworkElement
    {
        public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
            nameof(Samples), typeof(IReadOnlyList<OpcUaMonitoredSample>), typeof(OpcUaSubscriptionCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty DeadbandAbsoluteProperty = DependencyProperty.Register(
            nameof(DeadbandAbsolute), typeof(double), typeof(OpcUaSubscriptionCanvas),
            new FrameworkPropertyMetadata(0.6, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush DeadbandFill = DofPalette.Solid("#CBA6F7", 0.14);
        private static readonly Brush PublishedDot = DofPalette.Dof;
        private static readonly Brush FilteredDot = DofPalette.Subtle;
        private static readonly Brush KeepAliveDot = DofPalette.Ray;
        private static readonly Brush BadStatusDot = DofPalette.Warn;

        private static readonly Pen GridPen = DofPalette.Pen(DofPalette.Surface, 0.9);
        private static readonly Pen DeadbandPen = DofPalette.Pen(DofPalette.Aperture, 1.0, dash: 4, gap: 3);
        private static readonly Pen SignalPen = DofPalette.Pen(DofPalette.Fov, 1.6);
        private static readonly Pen PublishStemPen = DofPalette.Pen(DofPalette.Dof, 1.2, dash: 2, gap: 2);

        public OpcUaSubscriptionCanvas()
        {
            ClipToBounds = true;
        }

        public IReadOnlyList<OpcUaMonitoredSample>? Samples
        {
            get => (IReadOnlyList<OpcUaMonitoredSample>?)GetValue(SamplesProperty);
            set => SetValue(SamplesProperty, value);
        }

        public double DeadbandAbsolute
        {
            get => (double)GetValue(DeadbandAbsoluteProperty);
            set => SetValue(DeadbandAbsoluteProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 240 || h < 120)
            {
                return;
            }

            const double padLeft = 52;
            const double padRight = 14;
            const double padTop = 26;
            const double padBottom = 18;
            var plotW = w - padLeft - padRight;
            var plotH = h - padTop - padBottom;

            // Header & Legend
            DofText.Draw(dc, this, "Subscription 采样与死区过滤 (Deadband) 示波", 10.2, DofPalette.White, 12, 16, bold: true);
            DrawLegend(dc, w - 260, 12, DofPalette.Fov, "PLC原值");
            DrawLegend(dc, w - 196, 12, DofPalette.Dof, "已推送");
            DrawLegend(dc, w - 140, 12, DofPalette.Subtle, "死区过滤");
            DrawLegend(dc, w - 72, 12, DofPalette.Ray, "KeepAlive");

            var samples = Samples;
            if (samples is null || samples.Count < 2)
            {
                DofText.Draw(dc, this, "点击“单步采样”或“启动实时订阅”观测 Deadband 省流效果",
                    10.5, DofPalette.Muted, padLeft + plotW * 0.5, padTop + plotH * 0.5 + 4, TextAlign.Center);
                return;
            }

            var minVal = samples.Min(s => s.RawPlcValue);
            var maxVal = samples.Max(s => s.RawPlcValue);
            var lastRep = samples[^1].LastReportedValue;
            minVal = Math.Min(minVal, lastRep - DeadbandAbsolute);
            maxVal = Math.Max(maxVal, lastRep + DeadbandAbsolute);
            var mid = (minVal + maxVal) * 0.5;
            var halfSpan = Math.Max(2.5, (maxVal - minVal) * 0.62);
            var yLow = mid - halfSpan;
            var yHigh = mid + halfSpan;

            double MapY(double v) => padTop + plotH - Math.Clamp((v - yLow) / (yHigh - yLow), 0, 1) * plotH;

            // Horizontal gridlines & labels
            double[] yTicks = [yHigh, mid, yLow];
            foreach (var yt in yTicks)
            {
                var py = MapY(yt);
                dc.DrawLine(GridPen, new Point(padLeft, py), new Point(padLeft + plotW, py));
                DofText.Draw(dc, this, $"{yt:F1}°C", 8.5, DofPalette.Muted, padLeft - 6, py + 3, TextAlign.Right);
            }

            // Active ±Deadband shaded corridor around the latest reported value
            if (DeadbandAbsolute > 0.01)
            {
                var topY = MapY(lastRep + DeadbandAbsolute);
                var botY = MapY(lastRep - DeadbandAbsolute);
                dc.DrawRectangle(DeadbandFill, null, new Rect(padLeft, topY, plotW, Math.Max(1, botY - topY)));
                dc.DrawLine(DeadbandPen, new Point(padLeft, topY), new Point(padLeft + plotW, topY));
                dc.DrawLine(DeadbandPen, new Point(padLeft, botY), new Point(padLeft + plotW, botY));
                DofText.Draw(dc, this, $"死区 ±{DeadbandAbsolute:F1}°C", 8.2, DofPalette.Aperture, padLeft + 6, topY + 10, bold: true);
            }

            var dx = plotW / Math.Max(1, samples.Count - 1);

            // Continuous PLC signal polyline
            var prev = new Point(padLeft, MapY(samples[0].RawPlcValue));
            for (var i = 1; i < samples.Count; i++)
            {
                var next = new Point(padLeft + i * dx, MapY(samples[i].RawPlcValue));
                dc.DrawLine(SignalPen, prev, next);
                prev = next;
            }

            // Sample markers: Published DataChange vs Deadband-filtered vs KeepAlive vs BadStatus
            for (var i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                var px = padLeft + i * dx;
                var py = MapY(s.RawPlcValue);
                var isBad = !OpcUaStatusCodes.IsGood(s.StatusCode);

                if (s.PublishedToClient)
                {
                    dc.DrawLine(PublishStemPen, new Point(px, py), new Point(px, padTop + plotH));
                    dc.DrawEllipse(isBad ? BadStatusDot : PublishedDot, null, new Point(px, py), 4.0, 4.0);
                }
                else if (s.IsKeepAlive)
                {
                    dc.DrawEllipse(KeepAliveDot, null, new Point(px, padTop + plotH - 5), 3.2, 3.2);
                    DofText.Draw(dc, this, "KA", 7.5, DofPalette.Ray, px, padTop + plotH - 10, TextAlign.Center, bold: true);
                }
                else if (!s.PassedDeadband)
                {
                    dc.DrawEllipse(FilteredDot, null, new Point(px, py), 2.1, 2.1);
                }
            }
        }

        private void DrawLegend(DrawingContext dc, double x, double y, Brush brush, string text)
        {
            dc.DrawEllipse(brush, null, new Point(x, y), 3.2, 3.2);
            DofText.Draw(dc, this, text, 8.5, DofPalette.Muted, x + 6, y + 3.2);
        }
    }
}
