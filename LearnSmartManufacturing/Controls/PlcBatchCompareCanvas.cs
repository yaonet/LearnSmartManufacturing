using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Plc;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Visualizes the dramatic RTT reduction achieved by PDU/gap-aware PLC batch block reads
    /// compared to naive per-tag loop polling.
    /// </summary>
    public class PlcBatchCompareCanvas : FrameworkElement
    {
        public static readonly DependencyProperty PlanProperty = DependencyProperty.Register(
            nameof(Plan), typeof(PlcBatchPlanResult), typeof(PlcBatchCompareCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush TrackFill = DofPalette.Solid("#1E1E2E");
        private static readonly Brush NaiveFill = DofPalette.Solid("#F38BA8", 0.34);
        private static readonly Brush BatchFill = DofPalette.Solid("#A6E3A1", 0.34);
        private static readonly Pen TrackPen = DofPalette.Pen(DofPalette.Surface, 1.0);
        private static readonly Pen NaivePen = DofPalette.Pen(DofPalette.Warn, 1.5);
        private static readonly Pen BatchPen = DofPalette.Pen(DofPalette.Dof, 1.6);

        public PlcBatchCompareCanvas()
        {
            ClipToBounds = true;
        }

        public PlcBatchPlanResult? Plan
        {
            get => (PlcBatchPlanResult?)GetValue(PlanProperty);
            set => SetValue(PlanProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 240 || h < 110 || Plan is not { } p) return;

            const double padX = 14;
            DofText.Draw(dc, this, "逐点单读 vs 批量打包合并耗时对比", 10.5, DofPalette.White, padX, 18, bold: true);
            DofText.Draw(dc, this, $"提速 {p.SpeedupFactor:F1}×  ({p.NaiveRoundTrips} 次 → {p.BatchedRoundTrips} 次往返)",
                10, DofPalette.Dof, w - padX, 18, TextAlign.Right, bold: true);

            var barX = padX;
            var barW = w - padX * 2;

            // Bar 1: Naive per-tag loop
            var y1 = 36.0;
            DofText.Draw(dc, this, $"方案 A · 循环逐点单读 ({p.NaiveRoundTrips} 次 TCP RTT)", 9.2, DofPalette.Warn, barX, y1, bold: true);
            DofText.Draw(dc, this, $"{p.NaiveTotalMs:F1} ms ({1000.0 / Math.Max(0.1, p.NaiveTotalMs):F1} Hz)",
                9.2, DofPalette.Warn, w - padX, y1, TextAlign.Right, bold: true);

            var track1 = new Rect(barX, y1 + 6, barW, 22);
            dc.DrawRoundedRectangle(TrackFill, TrackPen, track1, 4, 4);
            dc.DrawRoundedRectangle(NaiveFill, NaivePen, track1, 4, 4);

            // Bar 2: Batched merged reads
            var y2 = y1 + 48;
            DofText.Draw(dc, this, $"方案 B · PDU/空洞合并批量读 ({p.BatchedRoundTrips} 次 TCP RTT · PDU {p.PduLimitBytes}B)", 9.2, DofPalette.Dof, barX, y2, bold: true);
            DofText.Draw(dc, this, $"{p.BatchedTotalMs:F1} ms ({1000.0 / Math.Max(0.1, p.BatchedTotalMs):F1} Hz)",
                9.2, DofPalette.Dof, w - padX, y2, TextAlign.Right, bold: true);

            var track2 = new Rect(barX, y2 + 6, barW, 22);
            dc.DrawRoundedRectangle(TrackFill, TrackPen, track2, 4, 4);
            var frac = Math.Clamp(p.BatchedTotalMs / Math.Max(0.1, p.NaiveTotalMs), 0.03, 1.0);
            dc.DrawRoundedRectangle(BatchFill, BatchPen, new Rect(barX, y2 + 6, Math.Max(14, barW * frac), 22), 4, 4);

            DofText.Draw(dc, this, "优化核心：用一次批量读取替代 N 次串行往返，在上位机内存按结构体偏移拆包（Span<byte> 零拷贝）",
                8.8, DofPalette.Muted, w * 0.5, h - 10, TextAlign.Center);
        }
    }
}
