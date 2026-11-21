using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.SerialTcp;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// 3-stage byte-stream framing pipeline visualizer:
    /// Row 1: Raw TCP/Serial Read() chunks arriving from OS layer
    /// Row 2: RxBuffer sliding window (highlighting unconsumed half-packet tail bytes)
    /// Row 3: Decoded clean business frames (and CRC errors / discarded EMI noise)
    /// </summary>
    public class StreamFramingCanvas : FrameworkElement
    {
        public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(
            nameof(Snapshot), typeof(FramingPipelineSnapshot), typeof(StreamFramingCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush NormalFill = DofPalette.Solid("#89DCEB", 0.18);
        private static readonly Brush StickyFill = DofPalette.Solid("#CBA6F7", 0.22);
        private static readonly Brush HalfFill = DofPalette.Solid("#F9E2AF", 0.22);
        private static readonly Brush ChaosFill = DofPalette.Solid("#F38BA8", 0.24);
        private static readonly Brush OkFrameFill = DofPalette.Solid("#A6E3A1", 0.20);
        private static readonly Brush EmptyBufFill = DofPalette.Solid("#1E1E2E", 0.90);

        private static readonly Pen NormalPen = DofPalette.Pen(DofPalette.Fov, 1.3);
        private static readonly Pen StickyPen = DofPalette.Pen(DofPalette.Aperture, 1.4);
        private static readonly Pen HalfPen = DofPalette.Pen(DofPalette.Ray, 1.4, dash: 4, gap: 3);
        private static readonly Pen ChaosPen = DofPalette.Pen(DofPalette.Warn, 1.5);
        private static readonly Pen OkFramePen = DofPalette.Pen(DofPalette.Dof, 1.4);
        private static readonly Pen EmptyBufPen = DofPalette.Pen(DofPalette.Surface, 1.1);

        public StreamFramingCanvas()
        {
            ClipToBounds = true;
        }

        public FramingPipelineSnapshot? Snapshot
        {
            get => (FramingPipelineSnapshot?)GetValue(SnapshotProperty);
            set => SetValue(SnapshotProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 300 || h < 120)
            {
                return;
            }

            if (Snapshot is not { } snap)
            {
                DofText.Draw(dc, this, "点击“演示粘包”、“演示半包”或“混合压测”，在此观测字节流拆包全流程",
                    11.5, DofPalette.Muted, w * 0.5, h * 0.5 + 4, TextAlign.Center);
                return;
            }

            const double padX = 12;
            const double labelW = 114;
            const double padY = 8;
            const double rowGap = 7;

            var row1H = 44.0;
            var row2H = 34.0;
            var row3H = Math.Max(42, h - padY * 2 - rowGap * 2 - row1H - row2H);

            var contentX = padX + labelW;
            var contentW = w - contentX - padX;

            // Row 1: Raw arrival chunks
            var y1 = padY;
            DofText.Draw(dc, this, "① 物理到达块", 9.5, DofPalette.Fov, padX, y1 + 17, bold: true);
            DofText.Draw(dc, this, "Socket / COM Read()", 8.2, DofPalette.Muted, padX, y1 + 30);
            DrawChunksRow(dc, contentX, y1, contentW, row1H, snap.RecentChunks);

            // Row 2: RxBuffer sliding cache
            var y2 = y1 + row1H + rowGap;
            DofText.Draw(dc, this, "② 接收滑动缓存", 9.5, DofPalette.Ray, padX, y2 + 14, bold: true);
            DofText.Draw(dc, this, "RxBuffer 残余半包", 8.2, DofPalette.Muted, padX, y2 + 26);
            DrawBufferRow(dc, contentX, y2, contentW, row2H, snap.PendingBufferBytes);

            // Row 3: Decoded frames
            var y3 = y2 + row2H + rowGap;
            DofText.Draw(dc, this, "③ 状态机拆出帧", 9.5, DofPalette.Dof, padX, y3 + 17, bold: true);
            DofText.Draw(dc, this, $"弃噪 {snap.GarbageBytesDiscarded}B · 错 {snap.CrcErrorCount}", 8.2,
                snap.CrcErrorCount > 0 || snap.GarbageBytesDiscarded > 0 ? DofPalette.Warn : DofPalette.Muted, padX, y3 + 30);
            DrawFramesRow(dc, contentX, y3, contentW, row3H, snap.RecentFrames);
        }

        private void DrawChunksRow(DrawingContext dc, double x, double y, double width, double height, IReadOnlyList<StreamChunkRecord> chunks)
        {
            if (chunks.Count == 0)
            {
                dc.DrawRoundedRectangle(EmptyBufFill, EmptyBufPen, new Rect(x, y, width, height), 5, 5);
                DofText.Draw(dc, this, "尚未收到字节流块", 9.5, DofPalette.Muted, x + width * 0.5, y + height * 0.5 + 3, TextAlign.Center);
                return;
            }

            var shown = chunks.TakeLast(4).ToArray();
            const double gap = 6;
            var boxW = Math.Max(90, (width - gap * (shown.Length - 1)) / shown.Length);
            for (var i = 0; i < shown.Length; i++)
            {
                var c = shown[i];
                var rect = new Rect(x + i * (boxW + gap), y, boxW, height);
                var (fill, pen, badgeBrush) = c.Kind switch
                {
                    "Sticky" => (StickyFill, StickyPen, DofPalette.Aperture),
                    "Half" => (HalfFill, HalfPen, DofPalette.Ray),
                    "Chaos" => (ChaosFill, ChaosPen, DofPalette.Warn),
                    _ => (NormalFill, NormalPen, DofPalette.Fov),
                };

                dc.DrawRoundedRectangle(fill, pen, rect, 5, 5);
                var innerW = rect.Width - 10;
                DofText.Draw(dc, this, Truncate($"#{c.ChunkId} {c.Label} ({c.Bytes.Length}B)", 9, innerW, true),
                    9, badgeBrush, rect.Left + 6, rect.Top + 14, bold: true);
                DofText.Draw(dc, this, Truncate(c.HexText, 8.8, innerW, false),
                    8.8, DofPalette.White, rect.Left + 6, rect.Bottom - 7);
            }
        }

        private void DrawBufferRow(DrawingContext dc, double x, double y, double width, double height, byte[] pending)
        {
            var rect = new Rect(x, y, width, height);
            if (pending.Length == 0)
            {
                dc.DrawRoundedRectangle(EmptyBufFill, EmptyBufPen, rect, 5, 5);
                DofText.Draw(dc, this, "✓ RxBuffer 已消费清空（无未闭合半包残余字节）",
                    9.5, DofPalette.Dof, rect.Left + 10, rect.Top + height * 0.5 + 3.5, bold: true);
            }
            else
            {
                dc.DrawRoundedRectangle(HalfFill, HalfPen, rect, 5, 5);
                var hex = CustomProtocolCodec.ToHex(pending, 28);
                var text = $"⏳ 半包挂起等待后续字节 ({pending.Length} B):  {hex}";
                DofText.Draw(dc, this, Truncate(text, 9.5, width - 16, true),
                    9.5, DofPalette.Ray, rect.Left + 10, rect.Top + height * 0.5 + 3.5, bold: true);
            }
        }

        private void DrawFramesRow(DrawingContext dc, double x, double y, double width, double height, IReadOnlyList<DecodedFrameRecord> frames)
        {
            if (frames.Count == 0)
            {
                dc.DrawRoundedRectangle(EmptyBufFill, EmptyBufPen, new Rect(x, y, width, height), 5, 5);
                DofText.Draw(dc, this, "暂无完整拆出帧（若处于半包演示，请点击“投递下一碎片”凑齐整帧）",
                    9.5, DofPalette.Muted, x + width * 0.5, y + height * 0.5 + 3, TextAlign.Center);
                return;
            }

            var shown = frames.TakeLast(4).ToArray();
            const double gap = 6;
            var boxW = Math.Max(90, (width - gap * (shown.Length - 1)) / shown.Length);
            for (var i = 0; i < shown.Length; i++)
            {
                var f = shown[i];
                var rect = new Rect(x + i * (boxW + gap), y, boxW, height);
                var fill = f.IsValid ? OkFrameFill : ChaosFill;
                var pen = f.IsValid ? OkFramePen : ChaosPen;
                var headerBrush = f.IsValid ? DofPalette.Dof : DofPalette.Warn;

                dc.DrawRoundedRectangle(fill, pen, rect, 5, 5);
                var innerW = rect.Width - 10;
                DofText.Draw(dc, this, Truncate($"Frame #{f.FrameId} · {f.CommandTag} [{f.StatusText}]", 9, innerW, true),
                    9, headerBrush, rect.Left + 6, rect.Top + 14, bold: true);
                DofText.Draw(dc, this, Truncate(f.PayloadText, 8.8, innerW, false),
                    8.8, DofPalette.White, rect.Left + 6, rect.Bottom - 7);
            }
        }

        private string Truncate(string text, double size, double maxW, bool bold)
        {
            if (DofText.Measure(this, text, size, bold) <= maxW) return text;
            var len = text.Length;
            while (len > 2 && DofText.Measure(this, text[..len] + "…", size, bold) > maxW) len--;
            return text[..Math.Max(1, len)] + "…";
        }
    }
}
