using System.Threading.Channels;
using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Async;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Real-time visualization of Producer -> Bounded Channel&lt;T&gt; slots -> Consumer Worker pool
    /// plus a live queue-depth sparkline.
    /// </summary>
    public class ChannelPipelineCanvas : FrameworkElement
    {
        public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(
            nameof(Snapshot), typeof(PipelineSnapshot), typeof(ChannelPipelineCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush NodeBg = DofPalette.Solid("#1E1E2E");
        private static readonly Brush SlotEmptyBg = DofPalette.Solid("#232436");
        private static readonly Brush SlotOkFill = DofPalette.Solid("#89DCEB", 0.35);
        private static readonly Brush SlotWarnFill = DofPalette.Solid("#F9E2AF", 0.40);
        private static readonly Brush SlotFullFill = DofPalette.Solid("#F38BA8", 0.45);
        private static readonly Brush BackpressureFill = DofPalette.Solid("#F38BA8", 0.22);
        private static readonly Brush WorkerBarBg = DofPalette.Solid("#141420");
        private static readonly Brush WorkerBarFill = DofPalette.Solid("#A6E3A1", 0.78);
        private static readonly Brush SparkFill = DofPalette.Solid("#89DCEB", 0.14);

        private static readonly Pen NodeBorderPen = DofPalette.Pen(DofPalette.Surface, 1.2);
        private static readonly Pen ProducerActivePen = DofPalette.Pen(DofPalette.Fov, 1.8);
        private static readonly Pen ProducerStallPen = DofPalette.Pen(DofPalette.Warn, 2.0);
        private static readonly Pen SlotEmptyPen = DofPalette.Pen(DofPalette.Solid("#3B3D54"), 1);
        private static readonly Pen SlotOkPen = DofPalette.Pen(DofPalette.Fov, 1.3);
        private static readonly Pen SlotWarnPen = DofPalette.Pen(DofPalette.Ray, 1.3);
        private static readonly Pen SlotFullPen = DofPalette.Pen(DofPalette.Warn, 1.4);
        private static readonly Pen ArrowPen = DofPalette.Pen(DofPalette.Solid("#6C7086"), 1.6);
        private static readonly Pen SparkPen = DofPalette.Pen(DofPalette.Fov, 1.8);
        private static readonly Pen GridPen = DofPalette.Pen(DofPalette.Solid("#313244", 0.5), 1, 3, 3);

        public ChannelPipelineCanvas()
        {
            ClipToBounds = true;
        }

        public PipelineSnapshot? Snapshot
        {
            get => (PipelineSnapshot?)GetValue(SnapshotProperty);
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

            if (Snapshot is not { } snap)
            {
                DofText.Draw(dc, this, "点击“启动流水线”观察 Channel<T> 背压与消费过程", 13, DofPalette.Muted, w / 2, h / 2, TextAlign.Center);
                return;
            }

            const double pad = 14;
            const double sparkH = 74;
            var mainH = Math.Max(120, h - sparkH - pad * 3);
            var mainTop = pad;

            // 3 horizontal stages: Producer (22%) -> Channel Queue (46%) -> Workers (32%)
            var usableW = w - pad * 2;
            const double arrowGap = 24;
            var contentW = usableW - arrowGap * 2;
            var prodW = Math.Max(110, contentW * 0.22);
            var queueW = Math.Max(160, contentW * 0.45);
            var workW = Math.Max(130, contentW - prodW - queueW);

            var prodRect = new Rect(pad, mainTop, prodW, mainH);
            var queueRect = new Rect(prodRect.Right + arrowGap, mainTop, queueW, mainH);
            var workRect = new Rect(queueRect.Right + arrowGap, mainTop, workW, mainH);

            DrawProducerNode(dc, prodRect, snap);
            DrawArrow(dc, prodRect.Right + 3, queueRect.Left - 3, mainTop + mainH * 0.5);
            DrawChannelQueueNode(dc, queueRect, snap);
            DrawArrow(dc, queueRect.Right + 3, workRect.Left - 3, mainTop + mainH * 0.5);
            DrawWorkerPoolNode(dc, workRect, snap);

            var sparkRect = new Rect(pad, mainTop + mainH + pad, usableW, sparkH);
            DrawDepthSparkline(dc, sparkRect, snap);
        }

        private void DrawProducerNode(DrawingContext dc, Rect rect, PipelineSnapshot snap)
        {
            var borderPen = snap.IsBackpressured ? ProducerStallPen : (snap.IsRunning ? ProducerActivePen : NodeBorderPen);
            dc.DrawRoundedRectangle(snap.IsBackpressured ? BackpressureFill : NodeBg, borderPen, rect, 8, 8);

            var cx = rect.Left + rect.Width * 0.5;
            DofText.Draw(dc, this, "Producer", 12, DofPalette.Fov, cx, rect.Top + 20, TextAlign.Center, bold: true);
            DofText.Draw(dc, this, "工业采图 / 采集端", 10, DofPalette.Muted, cx, rect.Top + 36, TextAlign.Center);

            var midY = rect.Top + rect.Height * 0.52;
            DofText.Draw(dc, this, $"{snap.ProducedCount:N0}", 20, DofPalette.White, cx, midY, TextAlign.Center, bold: true);
            DofText.Draw(dc, this, "累计生产帧数", 10, DofPalette.Muted, cx, midY + 16, TextAlign.Center);

            var badgeText = !snap.IsRunning
                ? "已停止"
                : (snap.IsBackpressured ? "⏸ 背压等待 (Wait)" : "● 高频写入中");
            var badgeBrush = !snap.IsRunning ? DofPalette.Subtle : (snap.IsBackpressured ? DofPalette.Warn : DofPalette.Dof);
            DofText.Draw(dc, this, badgeText, 11, badgeBrush, cx, rect.Bottom - 16, TextAlign.Center, bold: true);
        }

        private void DrawChannelQueueNode(DrawingContext dc, Rect rect, PipelineSnapshot snap)
        {
            dc.DrawRoundedRectangle(NodeBg, NodeBorderPen, rect, 8, 8);

            var modeText = snap.FullMode switch
            {
                BoundedChannelFullMode.Wait => "FullMode.Wait (背压不丢帧)",
                BoundedChannelFullMode.DropOldest => "FullMode.DropOldest (丢最旧保实时)",
                BoundedChannelFullMode.DropWrite => "FullMode.DropWrite (满载拒收新帧)",
                _ => snap.FullMode.ToString(),
            };

            DofText.Draw(dc, this, $"BoundedChannel<FrameItem>  ({snap.CurrentDepth}/{snap.Capacity})",
                12, DofPalette.Ray, rect.Left + 12, rect.Top + 20, bold: true);
            DofText.Draw(dc, this, modeText, 10, DofPalette.Muted, rect.Left + 12, rect.Top + 36);

            // Render visual queue slots grid
            var cap = Math.Max(1, snap.Capacity);
            var cols = cap <= 8 ? 4 : (cap <= 16 ? 8 : 8);
            var rows = (int)Math.Ceiling(cap / (double)cols);

            var gridL = rect.Left + 12;
            var gridT = rect.Top + 48;
            var gridW = rect.Width - 24;
            var gridH = Math.Max(36, rect.Height - 78);
            const double gap = 5;
            var cellW = (gridW - gap * (cols - 1)) / cols;
            var cellH = Math.Min(34, (gridH - gap * (rows - 1)) / rows);

            var fillRatio = snap.CurrentDepth / (double)cap;
            var fillBrush = fillRatio >= 0.95 ? SlotFullFill : (fillRatio >= 0.65 ? SlotWarnFill : SlotOkFill);
            var fillPen = fillRatio >= 0.95 ? SlotFullPen : (fillRatio >= 0.65 ? SlotWarnPen : SlotOkPen);

            for (var i = 0; i < cap; i++)
            {
                var r = i / cols;
                var c = i % cols;
                var x = gridL + c * (cellW + gap);
                var y = gridT + r * (cellH + gap);
                var filled = i < snap.QueuedFrameIds.Count;
                dc.DrawRoundedRectangle(filled ? fillBrush : SlotEmptyBg, filled ? fillPen : SlotEmptyPen,
                    new Rect(x, y, cellW, cellH), 4, 4);

                if (filled && cellW >= 26)
                {
                    DofText.Draw(dc, this, $"#{snap.QueuedFrameIds[i]}", 9, DofPalette.White,
                        x + cellW * 0.5, y + cellH * 0.5 + 3, TextAlign.Center);
                }
            }

            // Bottom drop & latency bar
            var dropColor = snap.DroppedCount > 0 ? DofPalette.Warn : DofPalette.Dof;
            DofText.Draw(dc, this, $"丢弃帧: {snap.DroppedCount:N0}", 10, dropColor, rect.Left + 12, rect.Bottom - 10, bold: snap.DroppedCount > 0);
            DofText.Draw(dc, this, $"平均驻留延迟: {snap.AverageLatencyMs:F0} ms", 10, DofPalette.Muted, rect.Right - 12, rect.Bottom - 10, TextAlign.Right);
        }

        private void DrawWorkerPoolNode(DrawingContext dc, Rect rect, PipelineSnapshot snap)
        {
            dc.DrawRoundedRectangle(NodeBg, NodeBorderPen, rect, 8, 8);

            DofText.Draw(dc, this, $"Consumer 池 ({snap.Workers.Count} Workers)", 12, DofPalette.Dof, rect.Left + 12, rect.Top + 20, bold: true);
            DofText.Draw(dc, this, $"吞吐 {snap.MeasuredThroughputFps:F0} fps · 消费 {snap.ConsumedCount:N0}", 10, DofPalette.Muted, rect.Left + 12, rect.Top + 36);

            var count = Math.Max(1, snap.Workers.Count);
            var listT = rect.Top + 48;
            var availH = Math.Max(40, rect.Height - 58);
            var rowH = Math.Min(36, (availH - 6 * (count - 1)) / count);

            for (var i = 0; i < snap.Workers.Count; i++)
            {
                var w = snap.Workers[i];
                var y = listT + i * (rowH + 6);
                var barRect = new Rect(rect.Left + 12, y, rect.Width - 24, rowH);
                dc.DrawRoundedRectangle(WorkerBarBg, SlotEmptyPen, barRect, 4, 4);

                if (w.IsBusy && w.Progress01 > 0)
                {
                    var fillW = Math.Max(4, (barRect.Width - 2) * Math.Clamp(w.Progress01, 0, 1));
                    dc.DrawRoundedRectangle(WorkerBarFill, null, new Rect(barRect.Left + 1, barRect.Top + 1, fillW, barRect.Height - 2), 3, 3);
                }

                var label = w.IsBusy
                    ? $"W{w.WorkerId} [T{w.ThreadId:D2}] 处理 #{w.CurrentFrameSeq}"
                    : $"W{w.WorkerId} 空闲等待 ReadAllAsync";
                DofText.Draw(dc, this, label, 10, w.IsBusy ? DofPalette.White : DofPalette.Subtle,
                    barRect.Left + 8, barRect.Top + barRect.Height * 0.5 + 3, bold: w.IsBusy);
            }
        }

        private void DrawDepthSparkline(DrawingContext dc, Rect rect, PipelineSnapshot snap)
        {
            dc.DrawRoundedRectangle(NodeBg, NodeBorderPen, rect, 8, 8);
            DofText.Draw(dc, this, "队列水位历史趋势 (Depth)", 10, DofPalette.Muted, rect.Left + 10, rect.Top + 14, bold: true);
            DofText.Draw(dc, this, $"满载阈值 {snap.Capacity}", 10, DofPalette.Subtle, rect.Right - 10, rect.Top + 14, TextAlign.Right);

            var plotL = rect.Left + 10;
            var plotR = rect.Right - 10;
            var plotT = rect.Top + 22;
            var plotB = rect.Bottom - 8;
            var plotW = Math.Max(20, plotR - plotL);
            var plotH = Math.Max(12, plotB - plotT);

            dc.DrawLine(GridPen, new Point(plotL, plotT + plotH * 0.5), new Point(plotR, plotT + plotH * 0.5));

            var hist = snap.DepthHistory;
            if (hist.Count < 2)
            {
                return;
            }

            var cap = Math.Max(1.0, snap.Capacity);
            var line = new StreamGeometry();
            var area = new StreamGeometry();
            using (var lg = line.Open())
            using (var ag = area.Open())
            {
                for (var i = 0; i < hist.Count; i++)
                {
                    var x = plotL + plotW * i / (hist.Count - 1);
                    var y = plotB - Math.Clamp(hist[i] / cap, 0, 1) * plotH;
                    var pt = new Point(x, y);
                    if (i == 0)
                    {
                        lg.BeginFigure(pt, false, false);
                        ag.BeginFigure(new Point(x, plotB), true, true);
                        ag.LineTo(pt, false, false);
                    }
                    else
                    {
                        lg.LineTo(pt, true, false);
                        ag.LineTo(pt, false, false);
                    }
                }

                ag.LineTo(new Point(plotR, plotB), false, false);
            }

            line.Freeze();
            area.Freeze();
            dc.DrawGeometry(SparkFill, null, area);
            dc.DrawGeometry(null, SparkPen, line);
        }

        private static void DrawArrow(DrawingContext dc, double x1, double x2, double y)
        {
            dc.DrawLine(ArrowPen, new Point(x1, y), new Point(x2, y));
            dc.DrawLine(ArrowPen, new Point(x2 - 5, y - 4), new Point(x2, y));
            dc.DrawLine(ArrowPen, new Point(x2 - 5, y + 4), new Point(x2, y));
        }
    }
}
