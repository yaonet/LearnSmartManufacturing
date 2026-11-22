using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Plc;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Color-coded wire frame dissector for Siemens S7 (TPKT+COTP+S7Comm), Mitsubishi MC 3E Binary,
    /// and Omron FINS/TCP displaying both TX Request and RX Response segments byte-by-byte.
    /// </summary>
    public class PlcFrameCanvas : FrameworkElement
    {
        public static readonly DependencyProperty TransactionProperty = DependencyProperty.Register(
            nameof(Transaction), typeof(PlcTransactionRecord), typeof(PlcFrameCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush HdrFill = DofPalette.Solid("#89B4FA", 0.22);
        private static readonly Brush RouteFill = DofPalette.Solid("#F9E2AF", 0.20);
        private static readonly Brush CmdFill = DofPalette.Solid("#CBA6F7", 0.24);
        private static readonly Brush AreaFill = DofPalette.Solid("#89DCEB", 0.20);
        private static readonly Brush OkFill = DofPalette.Solid("#A6E3A1", 0.24);
        private static readonly Brush ErrFill = DofPalette.Solid("#F38BA8", 0.30);

        private static readonly Pen HdrPen = DofPalette.Pen(DofPalette.Accent, 1.2);
        private static readonly Pen RoutePen = DofPalette.Pen(DofPalette.Ray, 1.2);
        private static readonly Pen CmdPen = DofPalette.Pen(DofPalette.Aperture, 1.3);
        private static readonly Pen AreaPen = DofPalette.Pen(DofPalette.Fov, 1.2);
        private static readonly Pen OkPen = DofPalette.Pen(DofPalette.Dof, 1.4);
        private static readonly Pen ErrPen = DofPalette.Pen(DofPalette.Warn, 1.6);

        public PlcFrameCanvas()
        {
            ClipToBounds = true;
        }

        public PlcTransactionRecord? Transaction
        {
            get => (PlcTransactionRecord?)GetValue(TransactionProperty);
            set => SetValue(TransactionProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 240 || h < 74) return;

            if (Transaction is not { } tx)
            {
                DofText.Draw(dc, this, "切换品牌并点击“读取/写入/建连握手”，在此实时解剖 S7 / MC 3E / FINS 二进制报文",
                    11.5, DofPalette.Muted, w * 0.5, h * 0.5 + 4, TextAlign.Center);
                return;
            }

            const double padX = 12;
            const double padY = 8;
            const double rowGap = 6;
            var rowH = (h - padY * 2 - rowGap) * 0.5;

            var brandTag = tx.Brand switch
            {
                PlcBrandKind.SiemensS7 => "Siemens S7",
                PlcBrandKind.MitsubishiMc3E => "MC SLMP 3E",
                _ => "Omron FINS/TCP",
            };

            DrawRow(dc, padX, padY, w - padX * 2, rowH, "TX →", DofPalette.Fov, tx.RequestSegments,
                $"{brandTag} · {tx.Operation} · {tx.RequestBytes.Length}B");

            DrawRow(dc, padX, padY + rowH + rowGap, w - padX * 2, rowH, "RX ←",
                tx.IsSuccess ? DofPalette.Dof : DofPalette.Warn, tx.ResponseSegments,
                $"{tx.StatusCodeText} · {tx.ResponseBytes.Length}B");
        }

        private void DrawRow(
            DrawingContext dc,
            double x, double y, double width, double height,
            string prefix, Brush prefixBrush,
            IReadOnlyList<PlcFrameSegment> segs,
            string rightBadge)
        {
            DofText.Draw(dc, this, prefix, 10.5, prefixBrush, x, y + height * 0.5 + 4, bold: true);
            var rightW = DofText.Measure(this, rightBadge, 9.5, true);
            DofText.Draw(dc, this, rightBadge, 9.5, prefixBrush, x + width, y + height * 0.5 + 4, TextAlign.Right, bold: true);

            var curX = x + 42;
            var maxX = x + width - rightW - 12;

            foreach (var seg in segs)
            {
                var lw = DofText.Measure(this, seg.Label, 8.5, true);
                var hw = DofText.Measure(this, seg.HexText, 9.5, true);
                var boxW = Math.Max(lw, hw) + 14;
                if (curX + boxW > maxX)
                {
                    var rem = maxX - curX;
                    if (rem < 48) break;
                    boxW = rem;
                }

                var (fill, pen, labelBrush) = seg.Kind switch
                {
                    "Hdr" => (HdrFill, HdrPen, DofPalette.Accent),
                    "Route" => (RouteFill, RoutePen, DofPalette.Ray),
                    "Cmd" => (CmdFill, CmdPen, DofPalette.Aperture),
                    "Ok" => (OkFill, OkPen, DofPalette.Dof),
                    "Err" => (ErrFill, ErrPen, DofPalette.Warn),
                    _ => (AreaFill, AreaPen, DofPalette.Fov),
                };

                var rect = new Rect(curX, y + 1, boxW, height - 2);
                dc.DrawRoundedRectangle(fill, pen, rect, 5, 5);
                var cx = rect.Left + rect.Width * 0.5;
                var innerW = rect.Width - 8;
                DofText.Draw(dc, this, Truncate(seg.Label, 8.5, innerW, true), 8.5, labelBrush, cx, rect.Top + 12, TextAlign.Center, bold: true);
                DofText.Draw(dc, this, Truncate(seg.HexText, 9.5, innerW, true), 9.5, DofPalette.White, cx, rect.Bottom - 6, TextAlign.Center, bold: true);
                curX += boxW + 5;
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
