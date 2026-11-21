using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Mqtt;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Visualizes the latest MQTT 3.1.1 control-packet handshake sequence (QoS 0/1/2, DUP retransmit, LWT)
    /// along with the raw binary packet HEX preview.
    /// </summary>
    public class MqttHandshakeCanvas : FrameworkElement
    {
        public static readonly DependencyProperty TraceProperty = DependencyProperty.Register(
            nameof(Trace), typeof(MqttHandshakeTrace), typeof(MqttHandshakeCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush ClientFill = DofPalette.Solid("#89DCEB", 0.16);
        private static readonly Brush BrokerFill = DofPalette.Solid("#A6E3A1", 0.16);
        private static readonly Brush LostFill = DofPalette.Solid("#F38BA8", 0.24);
        private static readonly Brush DupFill = DofPalette.Solid("#F9E2AF", 0.22);
        private static readonly Brush LwtFill = DofPalette.Solid("#F38BA8", 0.28);
        private static readonly Brush Qos0Fill = DofPalette.Solid("#89DCEB", 0.20);
        private static readonly Brush Qos1Fill = DofPalette.Solid("#F9E2AF", 0.20);
        private static readonly Brush Qos2Fill = DofPalette.Solid("#CBA6F7", 0.22);

        private static readonly Pen ClientPen = DofPalette.Pen(DofPalette.Fov, 1.3);
        private static readonly Pen BrokerPen = DofPalette.Pen(DofPalette.Dof, 1.3);
        private static readonly Pen LostPen = DofPalette.Pen(DofPalette.Warn, 1.5, dash: 4, gap: 3);
        private static readonly Pen DupPen = DofPalette.Pen(DofPalette.Ray, 1.5);
        private static readonly Pen LwtPen = DofPalette.Pen(DofPalette.Warn, 1.7);
        private static readonly Pen ArrowPen = DofPalette.Pen(DofPalette.Subtle, 1.4);
        private static readonly Pen HeaderLinePen = DofPalette.Pen(DofPalette.Surface, 1.0);

        public MqttHandshakeCanvas()
        {
            ClipToBounds = true;
        }

        public MqttHandshakeTrace? Trace
        {
            get => (MqttHandshakeTrace?)GetValue(TraceProperty);
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
                DofText.Draw(dc, this, "发布消息、切换 QoS 0/1/2 或模拟异常掉线，在此观测 MQTT 3.1.1 握手时序与二进制报文",
                    12, DofPalette.Muted, w * 0.5, h * 0.5 + 4, TextAlign.Center);
                return;
            }

            const double padX = 12;
            const double headerH = 26;

            // Header bar: QoS pill + title + raw packet HEX
            var (qosFill, qosBrush, qosLabel) = trace.Qos switch
            {
                MqttQos.AtLeastOnce => (Qos1Fill, DofPalette.Ray, "QoS 1"),
                MqttQos.ExactlyOnce => (Qos2Fill, DofPalette.Aperture, "QoS 2"),
                _ => (Qos0Fill, DofPalette.Fov, "QoS 0"),
            };

            var pillRect = new Rect(padX, 6, 46, 16);
            dc.DrawRoundedRectangle(qosFill, null, pillRect, 4, 4);
            DofText.Draw(dc, this, qosLabel, 9.5, qosBrush, pillRect.Left + pillRect.Width * 0.5, 17.5, TextAlign.Center, bold: true);

            var titleX = pillRect.Right + 8;
            var titleText = $"{trace.Title}  ({trace.Timestamp:HH:mm:ss.fff})";
            var titleW = DofText.Draw(dc, this, titleText, 11, DofPalette.White, titleX, 18, bold: true);

            if (trace.RawPacketBytes.Length > 0)
            {
                var hexPrefix = $"BIN ({trace.RawPacketBytes.Length}B): ";
                var availableForHex = w - padX - (titleX + titleW + 14);
                if (availableForHex > 90)
                {
                    var fullHex = hexPrefix + MqttPacketCodec.ToHexString(trace.RawPacketBytes, maxBytes: 18);
                    fullHex = TruncateToWidth(fullHex, 9.5, availableForHex, bold: false);
                    DofText.Draw(dc, this, fullHex, 9.5, DofPalette.Muted, w - padX, 17.5, TextAlign.Right);
                }
            }

            dc.DrawLine(HeaderLinePen, new Point(padX, headerH), new Point(w - padX, headerH));

            // Step boxes row
            var steps = trace.Steps;
            var n = steps.Count;
            const double arrowGap = 14;
            var usableW = w - padX * 2;
            var boxW = Math.Max(78, Math.Min(280, (usableW - arrowGap * (n - 1)) / n));
            var totalRowW = boxW * n + arrowGap * (n - 1);
            var startX = padX + Math.Max(0, (usableW - totalRowW) * 0.5);
            var boxY = headerH + 8;
            var boxH = Math.Max(46, h - boxY - 8);
            var midY = boxY + boxH * 0.5;

            for (var i = 0; i < n; i++)
            {
                var step = steps[i];
                var x = startX + i * (boxW + arrowGap);
                var rect = new Rect(x, boxY, boxW, boxH);

                var (fill, pen, badgeBrush) = SelectStepStyle(step);
                dc.DrawRoundedRectangle(fill, pen, rect, 6, 6);

                var dirText = step.IsLwt
                    ? "Broker → 全体订阅者"
                    : step.FromClientToBroker ? "Client → Broker" : "Broker → Client";
                var cx = rect.Left + rect.Width * 0.5;
                var innerW = rect.Width - 10;

                DofText.Draw(dc, this, TruncateToWidth(dirText, 8.5, innerW, true), 8.5, badgeBrush, cx, rect.Top + 13, TextAlign.Center, bold: true);
                DofText.Draw(dc, this, TruncateToWidth(step.PacketType, 10.5, innerW, true), 10.5, DofPalette.White, cx, rect.Top + boxH * 0.5 + 4, TextAlign.Center, bold: true);
                DofText.Draw(dc, this, TruncateToWidth(step.Detail, 8.5, innerW, false), 8.5, DofPalette.Muted, cx, rect.Bottom - 6, TextAlign.Center);

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

        private static (Brush Fill, Pen Pen, Brush BadgeBrush) SelectStepStyle(MqttHandshakeStep step)
        {
            if (step.IsLwt)
            {
                return (LwtFill, LwtPen, DofPalette.Warn);
            }

            if (step.IsLost)
            {
                return (LostFill, LostPen, DofPalette.Warn);
            }

            if (step.IsRetransmit)
            {
                return (DupFill, DupPen, DofPalette.Ray);
            }

            return step.FromClientToBroker
                ? (ClientFill, ClientPen, DofPalette.Fov)
                : (BrokerFill, BrokerPen, DofPalette.Dof);
        }

        private string TruncateToWidth(string text, double fontSize, double maxWidth, bool bold)
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
