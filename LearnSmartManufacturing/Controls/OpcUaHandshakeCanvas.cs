using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.OpcUa;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Visualizes OPC UA UACP SecureChannel (HEL/ACK/OPN) &amp; Session handshake steps,
    /// X.509 certificate rejection (BadCertificateUntrusted), and raw UACP binary packet HEX.
    /// </summary>
    public class OpcUaHandshakeCanvas : FrameworkElement
    {
        public static readonly DependencyProperty TraceProperty = DependencyProperty.Register(
            nameof(Trace), typeof(OpcUaHandshakeTrace), typeof(OpcUaHandshakeCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush ClientFill = DofPalette.Solid("#89DCEB", 0.16);
        private static readonly Brush ServerFill = DofPalette.Solid("#A6E3A1", 0.16);
        private static readonly Brush EncFill = DofPalette.Solid("#CBA6F7", 0.20);
        private static readonly Brush ErrorFill = DofPalette.Solid("#F38BA8", 0.28);
        private static readonly Brush ModeNoneFill = DofPalette.Solid("#89DCEB", 0.20);
        private static readonly Brush ModeSignFill = DofPalette.Solid("#F9E2AF", 0.22);
        private static readonly Brush ModeEncFill = DofPalette.Solid("#A6E3A1", 0.22);

        private static readonly Pen ClientPen = DofPalette.Pen(DofPalette.Fov, 1.3);
        private static readonly Pen ServerPen = DofPalette.Pen(DofPalette.Dof, 1.3);
        private static readonly Pen EncPen = DofPalette.Pen(DofPalette.Aperture, 1.4);
        private static readonly Pen ErrorPen = DofPalette.Pen(DofPalette.Warn, 1.7);
        private static readonly Pen ArrowPen = DofPalette.Pen(DofPalette.Subtle, 1.4);
        private static readonly Pen HeaderLinePen = DofPalette.Pen(DofPalette.Surface, 1.0);

        public OpcUaHandshakeCanvas()
        {
            ClipToBounds = true;
        }

        public OpcUaHandshakeTrace? Trace
        {
            get => (OpcUaHandshakeTrace?)GetValue(TraceProperty);
            set => SetValue(TraceProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 240 || h < 76)
            {
                return;
            }

            if (Trace is not { } trace || trace.Steps.Count == 0)
            {
                DofText.Draw(dc, this, "连接 OPC UA Server 或测试 X.509 证书信任策略，在此观测 UACP 安全通道与会话握手",
                    11.5, DofPalette.Muted, w * 0.5, h * 0.5 + 4, TextAlign.Center);
                return;
            }

            const double padX = 12;
            const double headerH = 26;

            var (pillFill, pillText, modeLabel) = trace.SecurityMode switch
            {
                OpcUaSecurityMode.SignAndEncrypt => (ModeEncFill, DofPalette.Dof, "Sign&Encrypt"),
                OpcUaSecurityMode.Sign => (ModeSignFill, DofPalette.Ray, "Sign"),
                _ => (ModeNoneFill, DofPalette.Fov, "None"),
            };

            var pillW = DofText.Measure(this, modeLabel, 9.2, true) + 14;
            var pillRect = new Rect(padX, 6, pillW, 16);
            dc.DrawRoundedRectangle(pillFill, null, pillRect, 4, 4);
            DofText.Draw(dc, this, modeLabel, 9.2, pillText, pillRect.Left + pillRect.Width * 0.5, 17.5, TextAlign.Center, bold: true);

            var titleX = pillRect.Right + 8;
            var titleText = $"{trace.Title}  ({trace.Timestamp:HH:mm:ss.fff})";
            var titleW = DofText.Draw(dc, this, titleText, 10.8, DofPalette.White, titleX, 18, bold: true);

            if (trace.RawUacpBytes.Length > 0)
            {
                var avail = w - padX - (titleX + titleW + 14);
                if (avail > 90)
                {
                    var hex = $"UACP ({trace.RawUacpBytes.Length}B): " + OpcUaBinaryCodec.ToHex(trace.RawUacpBytes, 16);
                    DofText.Draw(dc, this, Truncate(hex, 9.2, avail, false), 9.2, DofPalette.Muted, w - padX, 17.5, TextAlign.Right);
                }
            }

            dc.DrawLine(HeaderLinePen, new Point(padX, headerH), new Point(w - padX, headerH));

            var steps = trace.Steps;
            var n = steps.Count;
            const double arrowGap = 14;
            var usableW = w - padX * 2;
            var boxW = Math.Max(78, Math.Min(230, (usableW - arrowGap * (n - 1)) / n));
            var totalRowW = boxW * n + arrowGap * (n - 1);
            var startX = padX + Math.Max(0, (usableW - totalRowW) * 0.5);
            var boxY = headerH + 8;
            var boxH = Math.Max(46, h - boxY - 8);
            var midY = boxY + boxH * 0.5;

            for (var i = 0; i < n; i++)
            {
                var step = steps[i];
                var rect = new Rect(startX + i * (boxW + arrowGap), boxY, boxW, boxH);
                var (fill, pen, badgeBrush) = SelectStyle(step);
                dc.DrawRoundedRectangle(fill, pen, rect, 6, 6);

                var cx = rect.Left + rect.Width * 0.5;
                var innerW = rect.Width - 10;
                var dir = step.FromClientToServer ? "Client → Server" : "Server → Client";

                DofText.Draw(dc, this, Truncate(dir, 8.2, innerW, true), 8.2, badgeBrush, cx, rect.Top + 13, TextAlign.Center, bold: true);
                DofText.Draw(dc, this, Truncate(step.PacketTag, 10.2, innerW, true), 10.2, DofPalette.White, cx, rect.Top + boxH * 0.5 + 4, TextAlign.Center, bold: true);
                DofText.Draw(dc, this, Truncate(step.Detail, 8.2, innerW, false), 8.2, DofPalette.Muted, cx, rect.Bottom - 6, TextAlign.Center);

                if (i < n - 1)
                {
                    var ax1 = rect.Right + 2;
                    var ax2 = rect.Right + arrowGap - 2;
                    dc.DrawLine(ArrowPen, new Point(ax1, midY), new Point(ax2, midY));
                    dc.DrawLine(ArrowPen, new Point(ax2 - 4, midY - 3.5), new Point(ax2, midY));
                    dc.DrawLine(ArrowPen, new Point(ax2 - 4, midY + 3.5), new Point(ax2, midY));
                }
            }
        }

        private static (Brush Fill, Pen Pen, Brush BadgeBrush) SelectStyle(OpcUaHandshakeStep step)
        {
            if (step.IsError) return (ErrorFill, ErrorPen, DofPalette.Warn);
            if (step.IsEncrypted) return (EncFill, EncPen, DofPalette.Aperture);
            return step.FromClientToServer
                ? (ClientFill, ClientPen, DofPalette.Fov)
                : (ServerFill, ServerPen, DofPalette.Dof);
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
