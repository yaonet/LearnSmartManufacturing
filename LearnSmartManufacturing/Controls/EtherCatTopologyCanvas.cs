using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.EtherCat;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Renders the EtherCAT logical ring ("Processing on the Fly") topology with per-slave ESM/WKC/DC badges,
    /// daisy-chain cable-break loopback visualization, and the live 0x88A4 LRW frame dissector strip.
    /// </summary>
    public class EtherCatTopologyCanvas : FrameworkElement
    {
        public static readonly DependencyProperty SlavesProperty = DependencyProperty.Register(
            nameof(Slaves), typeof(IReadOnlyList<EtherCatSlaveSnapshot>), typeof(EtherCatTopologyCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LatestFrameProperty = DependencyProperty.Register(
            nameof(LatestFrame), typeof(EtherCatFrameSnapshot), typeof(EtherCatTopologyCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MasterStateProperty = DependencyProperty.Register(
            nameof(MasterState), typeof(EsmState), typeof(EtherCatTopologyCanvas),
            new FrameworkPropertyMetadata(EsmState.Init, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ActiveFaultProperty = DependencyProperty.Register(
            nameof(ActiveFault), typeof(EtherCatFaultKind), typeof(EtherCatTopologyCanvas),
            new FrameworkPropertyMetadata(EtherCatFaultKind.None, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FramePulsePhaseProperty = DependencyProperty.Register(
            nameof(FramePulsePhase), typeof(double), typeof(EtherCatTopologyCanvas),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush MasterFill = DofPalette.Solid("#89B4FA", 0.20);
        private static readonly Brush OpFill = DofPalette.Solid("#A6E3A1", 0.16);
        private static readonly Brush SafeOpFill = DofPalette.Solid("#89DCEB", 0.16);
        private static readonly Brush PreOpFill = DofPalette.Solid("#F9E2AF", 0.16);
        private static readonly Brush InitFill = DofPalette.Solid("#6C7086", 0.22);
        private static readonly Brush ErrorFill = DofPalette.Solid("#F38BA8", 0.26);

        private static readonly Brush EthFill = DofPalette.Solid("#89B4FA", 0.22);
        private static readonly Brush EcatFill = DofPalette.Solid("#F9E2AF", 0.20);
        private static readonly Brush CmdFill = DofPalette.Solid("#CBA6F7", 0.22);
        private static readonly Brush PdoFill = DofPalette.Solid("#89DCEB", 0.18);
        private static readonly Brush WkcOkFill = DofPalette.Solid("#A6E3A1", 0.24);
        private static readonly Brush WkcErrFill = DofPalette.Solid("#F38BA8", 0.30);

        private static readonly Pen MasterPen = DofPalette.Pen(DofPalette.Accent, 1.4);
        private static readonly Pen OpPen = DofPalette.Pen(DofPalette.Dof, 1.4);
        private static readonly Pen SafeOpPen = DofPalette.Pen(DofPalette.Fov, 1.4);
        private static readonly Pen PreOpPen = DofPalette.Pen(DofPalette.Ray, 1.4);
        private static readonly Pen InitPen = DofPalette.Pen(DofPalette.Subtle, 1.2);
        private static readonly Pen ErrorPen = DofPalette.Pen(DofPalette.Warn, 1.7);

        private static readonly Pen TxLinePen = DofPalette.Pen(DofPalette.Fov, 1.6);
        private static readonly Pen RxLinePen = DofPalette.Pen(DofPalette.Dof, 1.5);
        private static readonly Pen BrokenLinePen = DofPalette.Pen(DofPalette.Warn, 1.6, dash: 4, gap: 3);
        private static readonly Pen DividerPen = DofPalette.Pen(DofPalette.Surface, 1.0);

        private static readonly Pen EthPen = DofPalette.Pen(DofPalette.Accent, 1.2);
        private static readonly Pen EcatPen = DofPalette.Pen(DofPalette.Ray, 1.2);
        private static readonly Pen CmdPen = DofPalette.Pen(DofPalette.Aperture, 1.3);
        private static readonly Pen PdoPen = DofPalette.Pen(DofPalette.Fov, 1.2);
        private static readonly Pen WkcOkPen = DofPalette.Pen(DofPalette.Dof, 1.4);
        private static readonly Pen WkcErrPen = DofPalette.Pen(DofPalette.Warn, 1.6);

        public EtherCatTopologyCanvas()
        {
            ClipToBounds = true;
        }

        public IReadOnlyList<EtherCatSlaveSnapshot>? Slaves
        {
            get => (IReadOnlyList<EtherCatSlaveSnapshot>?)GetValue(SlavesProperty);
            set => SetValue(SlavesProperty, value);
        }

        public EtherCatFrameSnapshot? LatestFrame
        {
            get => (EtherCatFrameSnapshot?)GetValue(LatestFrameProperty);
            set => SetValue(LatestFrameProperty, value);
        }

        public EsmState MasterState
        {
            get => (EsmState)GetValue(MasterStateProperty);
            set => SetValue(MasterStateProperty, value);
        }

        public EtherCatFaultKind ActiveFault
        {
            get => (EtherCatFaultKind)GetValue(ActiveFaultProperty);
            set => SetValue(ActiveFaultProperty, value);
        }

        public double FramePulsePhase
        {
            get => (double)GetValue(FramePulsePhaseProperty);
            set => SetValue(FramePulsePhaseProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 320 || h < 110)
            {
                return;
            }

            const double padX = 12;
            var splitY = h - 48;

            DrawRingTopology(dc, padX, 8, w - padX * 2, splitY - 12);
            dc.DrawLine(DividerPen, new Point(padX, splitY), new Point(w - padX, splitY));
            DrawFrameDissector(dc, padX, splitY + 6, w - padX * 2, h - splitY - 12);
        }

        private void DrawRingTopology(DrawingContext dc, double x, double y, double width, double height)
        {
            var slaves = Slaves;
            var totalNodes = 1 + (slaves?.Count ?? 0);
            const double gap = 22;
            var nodeW = Math.Max(105, Math.Min(195, (width - gap * (totalNodes - 1)) / totalNodes));
            var totalRowW = nodeW * totalNodes + gap * (totalNodes - 1);
            var startX = x + Math.Max(0, (width - totalRowW) * 0.5);
            var boxH = Math.Min(60, Math.Max(46, height - 16));
            var boxY = y + 4;
            var txY = boxY + boxH * 0.34;
            var rxY = boxY + boxH * 0.72;
            var returnBusY = boxY + boxH + 8;

            // 1. Draw Master NIC node
            var masterRect = new Rect(startX, boxY, nodeW, boxH);
            dc.DrawRoundedRectangle(MasterFill, MasterPen, masterRect, 6, 6);
            var mcx = masterRect.Left + masterRect.Width * 0.5;
            DofText.Draw(dc, this, "EtherCAT Master NIC", 9.5, DofPalette.Accent, mcx, masterRect.Top + 14, TextAlign.Center, bold: true);
            DofText.Draw(dc, this, $"ESM: {EtherCatFrameCodec.FormatEsmName(MasterState)}", 10.5, DofPalette.White, mcx, masterRect.Top + boxH * 0.5 + 4, TextAlign.Center, bold: true);
            DofText.Draw(dc, this, "飞速处理 · 0x88A4 环网", 8.5, DofPalette.Muted, mcx, masterRect.Bottom - 6, TextAlign.Center);

            if (slaves is null || slaves.Count == 0)
            {
                return;
            }

            // Track rightmost online slave for ESC automatic return-loopback rendering
            var lastOnlineX = masterRect.Right;

            for (var i = 0; i < slaves.Count; i++)
            {
                var slave = slaves[i];
                var sx = startX + (i + 1) * (nodeW + gap);
                var prevRight = startX + i * (nodeW + gap) + nodeW;
                var rect = new Rect(sx, boxY, nodeW, boxH);

                var isBrokenLink = !slave.IsOnline && (i == 0 || slaves[i - 1].IsOnline);
                var linkPen = slave.IsOnline ? TxLinePen : BrokenLinePen;

                // Outbound TX wire (upper)
                dc.DrawLine(linkPen, new Point(prevRight, txY), new Point(rect.Left, txY));
                if (slave.IsOnline)
                {
                    DrawArrowHead(dc, TxLinePen, rect.Left, txY, pointingRight: true);
                    // Return RX wire (lower)
                    dc.DrawLine(RxLinePen, new Point(rect.Left, rxY), new Point(prevRight, rxY));
                    DrawArrowHead(dc, RxLinePen, prevRight, rxY, pointingRight: false);
                    lastOnlineX = rect.Right;
                }
                else if (isBrokenLink)
                {
                    var midLink = (prevRight + rect.Left) * 0.5;
                    DofText.Draw(dc, this, "✕", 12, DofPalette.Warn, midLink, txY + 4, TextAlign.Center, bold: true);
                    DofText.Draw(dc, this, "断链", 8, DofPalette.Warn, midLink, rxY + 5, TextAlign.Center, bold: true);
                }

                // Slave card body
                var (fill, pen, stateBrush) = SelectSlaveStyle(slave);
                dc.DrawRoundedRectangle(fill, pen, rect, 6, 6);
                var cx = rect.Left + rect.Width * 0.5;
                var innerW = rect.Width - 10;

                DofText.Draw(dc, this, Truncate($"{slave.AddressHex} {slave.Name}", 9.5, innerW, true),
                    9.5, DofPalette.White, cx, rect.Top + 14, TextAlign.Center, bold: true);

                var midLine = slave.IsReferenceClock
                    ? $"{slave.StateTag} · RefClk"
                    : $"{slave.StateTag} · WKC {slave.WkcTag}";
                DofText.Draw(dc, this, Truncate(midLine, 9.5, innerW, true),
                    9.5, stateBrush, cx, rect.Top + boxH * 0.5 + 4, TextAlign.Center, bold: true);

                var bottomLine = !slave.IsOnline
                    ? "离线 (ESC 端口环回)"
                    : slave.HasError
                        ? $"AL 0x{slave.AlStatusCode:X4}"
                        : slave.IsReferenceClock
                            ? "Delay 0ns · 时钟源"
                            : $"Delay {slave.PropagationDelayNs}ns · {slave.DcOffsetNs:+0;-0;0}ns";
                DofText.Draw(dc, this, Truncate(bottomLine, 8.5, innerW, false),
                    8.5, slave.HasError || !slave.IsOnline ? DofPalette.Warn : DofPalette.Muted, cx, rect.Bottom - 6, TextAlign.Center);
            }

            // Draw right-end ESC automatic loopback closure cap on last online slave
            if (lastOnlineX > masterRect.Right)
            {
                var capX = lastOnlineX + 8;
                dc.DrawLine(TxLinePen, new Point(lastOnlineX, txY), new Point(capX, txY));
                dc.DrawLine(RxLinePen, new Point(capX, txY), new Point(capX, rxY));
                dc.DrawLine(RxLinePen, new Point(capX, rxY), new Point(lastOnlineX, rxY));
            }

            // Bottom subtle flow legend + animated flying frame pulse
            var pulsePhase = FramePulsePhase % 1.0;
            if (pulsePhase < 0) pulsePhase += 1.0;
            if (LatestFrame is not null && lastOnlineX > masterRect.Right)
            {
                var spanStart = masterRect.Right;
                var spanEnd = lastOnlineX + 6;
                double dotX;
                double dotY;
                Brush dotBrush;
                if (pulsePhase < 0.5)
                {
                    var t = pulsePhase * 2.0;
                    dotX = spanStart + (spanEnd - spanStart) * t;
                    dotY = txY;
                    dotBrush = DofPalette.Fov;
                }
                else
                {
                    var t = (pulsePhase - 0.5) * 2.0;
                    dotX = spanEnd - (spanEnd - spanStart) * t;
                    dotY = rxY;
                    dotBrush = LatestFrame.IsWkcValid ? DofPalette.Dof : DofPalette.Warn;
                }

                dc.DrawEllipse(dotBrush, null, new Point(dotX, dotY), 3.6, 3.6);
            }

            DofText.Draw(dc, this, "→ 去程 (TX): 提取 RxPDO / WKC+2    ← 返程 (RX): 插入 TxPDO / WKC+1（末级从站硬件自动环回）",
                8.5, DofPalette.Muted, x + width * 0.5, returnBusY + 5, TextAlign.Center);
        }

        private void DrawFrameDissector(DrawingContext dc, double x, double y, double width, double height)
        {
            if (LatestFrame is not { } frame || frame.Segments.Count == 0)
            {
                DofText.Draw(dc, this, "等待周期性 EtherCAT LRW 帧交换...", 10.5, DofPalette.Muted, x + width * 0.5, y + height * 0.5 + 4, TextAlign.Center);
                return;
            }

            var rightBadge = $"Cycle #{frame.CycleNumber} · {frame.RawFrameBytes.Length}B · {frame.FrameLatencyUs:F1} µs";
            var rightW = DofText.Measure(this, rightBadge, 9.5, true);
            DofText.Draw(dc, this, rightBadge, 9.5, frame.IsWkcValid ? DofPalette.Dof : DofPalette.Warn,
                x + width, y + height * 0.5 + 4, TextAlign.Right, bold: true);

            var curX = x;
            var maxX = x + width - rightW - 12;
            foreach (var seg in frame.Segments)
            {
                var lw = DofText.Measure(this, seg.Label, 8.5, true);
                var hw = DofText.Measure(this, seg.HexText, 9.5, true);
                var boxW = Math.Max(lw, hw) + 14;
                if (curX + boxW > maxX)
                {
                    var rem = maxX - curX;
                    if (rem < 52) break;
                    boxW = rem;
                }

                var (fill, pen, textBrush) = seg.Kind switch
                {
                    "Eth" => (EthFill, EthPen, DofPalette.Accent),
                    "Ecat" => (EcatFill, EcatPen, DofPalette.Ray),
                    "Cmd" => (CmdFill, CmdPen, DofPalette.Aperture),
                    "WkcOk" => (WkcOkFill, WkcOkPen, DofPalette.Dof),
                    "WkcErr" => (WkcErrFill, WkcErrPen, DofPalette.Warn),
                    _ => (PdoFill, PdoPen, DofPalette.Fov),
                };

                var rect = new Rect(curX, y, boxW, height);
                dc.DrawRoundedRectangle(fill, pen, rect, 5, 5);
                var cx = rect.Left + rect.Width * 0.5;
                var innerW = rect.Width - 8;
                DofText.Draw(dc, this, Truncate(seg.Label, 8.5, innerW, true), 8.5, textBrush, cx, rect.Top + 12, TextAlign.Center, bold: true);
                DofText.Draw(dc, this, Truncate(seg.HexText, 9.5, innerW, true), 9.5, DofPalette.White, cx, rect.Bottom - 6, TextAlign.Center, bold: true);
                curX += boxW + 6;
            }
        }

        private static (Brush Fill, Pen Pen, Brush StateBrush) SelectSlaveStyle(EtherCatSlaveSnapshot slave)
        {
            if (!slave.IsOnline || slave.HasError)
            {
                return (ErrorFill, ErrorPen, DofPalette.Warn);
            }

            return slave.State switch
            {
                EsmState.Op => (OpFill, OpPen, DofPalette.Dof),
                EsmState.SafeOp => (SafeOpFill, SafeOpPen, DofPalette.Fov),
                EsmState.PreOp => (PreOpFill, PreOpPen, DofPalette.Ray),
                _ => (InitFill, InitPen, DofPalette.Muted),
            };
        }

        private static void DrawArrowHead(DrawingContext dc, Pen pen, double tipX, double y, bool pointingRight)
        {
            var dir = pointingRight ? -1.0 : 1.0;
            dc.DrawLine(pen, new Point(tipX + dir * 5, y - 3.5), new Point(tipX, y));
            dc.DrawLine(pen, new Point(tipX + dir * 5, y + 3.5), new Point(tipX, y));
        }

        private string Truncate(string text, double fontSize, double maxWidth, bool bold)
        {
            if (DofText.Measure(this, text, fontSize, bold) <= maxWidth)
            {
                return text;
            }

            var len = text.Length;
            while (len > 2 && DofText.Measure(this, text[..len] + "…", fontSize, bold) > maxWidth)
            {
                len--;
            }

            return text[..Math.Max(1, len)] + "…";
        }
    }
}
