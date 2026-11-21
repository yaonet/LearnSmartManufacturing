using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Modbus;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Color-coded Modbus TCP (MBAP) / RTU (CRC-16) frame dissector rendering the latest TX and RX frames byte-by-byte.
    /// </summary>
    public class ModbusFrameCanvas : FrameworkElement
    {
        public static readonly DependencyProperty TransactionProperty = DependencyProperty.Register(
            nameof(Transaction), typeof(ModbusTransaction), typeof(ModbusFrameCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush HeaderFill = DofPalette.Solid("#89B4FA", 0.22);
        private static readonly Brush UnitFill = DofPalette.Solid("#F9E2AF", 0.22);
        private static readonly Brush FuncFill = DofPalette.Solid("#CBA6F7", 0.24);
        private static readonly Brush DataFill = DofPalette.Solid("#89DCEB", 0.18);
        private static readonly Brush CrcOkFill = DofPalette.Solid("#A6E3A1", 0.24);
        private static readonly Brush ErrorFill = DofPalette.Solid("#F38BA8", 0.28);

        private static readonly Pen HeaderPen = DofPalette.Pen(DofPalette.Accent, 1.2);
        private static readonly Pen UnitPen = DofPalette.Pen(DofPalette.Ray, 1.2);
        private static readonly Pen FuncPen = DofPalette.Pen(DofPalette.Aperture, 1.4);
        private static readonly Pen DataPen = DofPalette.Pen(DofPalette.Fov, 1.2);
        private static readonly Pen CrcOkPen = DofPalette.Pen(DofPalette.Dof, 1.4);
        private static readonly Pen ErrorPen = DofPalette.Pen(DofPalette.Warn, 1.6);

        public ModbusFrameCanvas()
        {
            ClipToBounds = true;
        }

        public ModbusTransaction? Transaction
        {
            get => (ModbusTransaction?)GetValue(TransactionProperty);
            set => SetValue(TransactionProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 220 || h < 70)
            {
                return;
            }

            if (Transaction is not { } tx)
            {
                DofText.Draw(dc, this, "点击“发送请求”或“开始轮询”，在此实时解剖 Modbus TCP (MBAP) / RTU (CRC-16) 字节帧",
                    12, DofPalette.Muted, w * 0.5, h * 0.5 + 4, TextAlign.Center);
                return;
            }

            const double padX = 12;
            const double padY = 8;
            const double rowGap = 6;
            var rowH = (h - padY * 2 - rowGap) * 0.5;

            var txBlocks = ModbusCodec.DissectFrame(tx.Format, tx.RequestBytes);
            var rxBlocks = ModbusCodec.DissectFrame(tx.Format, tx.ResponseBytes);

            DrawFrameRow(dc, padX, padY, w - padX * 2, rowH, "TX →", DofPalette.Fov, txBlocks,
                $"{(tx.Format == ModbusFrameFormat.Tcp ? "Modbus TCP (MBAP)" : "Modbus RTU (CRC-16)")} · {tx.RequestBytes.Length} B");

            var rxStatus = tx.IsSuccess
                ? $"OK · {tx.RttMs:F1} ms · {tx.ResponseBytes.Length} B"
                : $"{tx.StatusTag} · {tx.RttMs:F1} ms";
            DrawFrameRow(dc, padX, padY + rowH + rowGap, w - padX * 2, rowH, "RX ←",
                tx.IsSuccess ? DofPalette.Dof : DofPalette.Warn, rxBlocks, rxStatus);
        }

        private void DrawFrameRow(
            DrawingContext dc,
            double x,
            double y,
            double width,
            double height,
            string prefix,
            Brush prefixBrush,
            IReadOnlyList<ModbusFrameBlock> blocks,
            string rightBadge)
        {
            DofText.Draw(dc, this, prefix, 11, prefixBrush, x, y + height * 0.5 + 4, bold: true);
            var rightW = DofText.Measure(this, rightBadge, 10, true);
            DofText.Draw(dc, this, rightBadge, 10, prefixBrush, x + width, y + height * 0.5 + 4, TextAlign.Right, bold: true);

            var curX = x + 44;
            var maxX = x + width - rightW - 14;
            if (blocks.Count == 0)
            {
                DofText.Draw(dc, this, "无响应帧（超时或链路断开）", 10.5, DofPalette.Warn, curX, y + height * 0.5 + 4);
                return;
            }

            foreach (var b in blocks)
            {
                var labelW = DofText.Measure(this, b.Label, 9, true);
                var hexW = DofText.Measure(this, b.HexText, 10.5, true);
                var boxW = Math.Max(labelW, hexW) + 14;
                if (curX + boxW > maxX)
                {
                    var rem = maxX - curX;
                    if (rem < 42)
                    {
                        break;
                    }

                    boxW = rem;
                }

                var (fill, pen, textBrush) = b.Kind switch
                {
                    "Header" => (HeaderFill, HeaderPen, DofPalette.Accent),
                    "Unit" => (UnitFill, UnitPen, DofPalette.Ray),
                    "Func" => (FuncFill, FuncPen, DofPalette.Aperture),
                    "CrcOk" => (CrcOkFill, CrcOkPen, DofPalette.Dof),
                    "Error" => (ErrorFill, ErrorPen, DofPalette.Warn),
                    _ => (DataFill, DataPen, DofPalette.Fov),
                };

                var rect = new Rect(curX, y + 1, boxW, height - 2);
                dc.DrawRoundedRectangle(fill, pen, rect, 5, 5);
                var cx = rect.Left + rect.Width * 0.5;
                DofText.Draw(dc, this, b.Label, 8.5, textBrush, cx, rect.Top + 11, TextAlign.Center, bold: true);

                var hexDisplay = b.HexText;
                if (DofText.Measure(this, hexDisplay, 10, true) > boxW - 8 && hexDisplay.Length > 12)
                {
                    hexDisplay = hexDisplay[..Math.Max(6, (int)((boxW - 18) / 6.5))] + "…";
                }

                DofText.Draw(dc, this, hexDisplay, 10, DofPalette.White, cx, rect.Bottom - 6, TextAlign.Center, bold: true);
                curX += boxW + 5;
            }
        }
    }
}
