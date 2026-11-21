using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.SerialTcp;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Visualizes RS485 two-wire half-duplex bus transactions (TX / RX / BUS COLLISION / Heartbeat Reconnect)
    /// along with the exponential-backoff link supervision state.
    /// </summary>
    public class Rs485BusCanvas : FrameworkElement
    {
        public static readonly DependencyProperty EventsProperty = DependencyProperty.Register(
            nameof(Events), typeof(IReadOnlyList<Rs485BusEvent>), typeof(Rs485BusCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LinkStateProperty = DependencyProperty.Register(
            nameof(LinkState), typeof(ReconnectLinkState), typeof(Rs485BusCanvas),
            new FrameworkPropertyMetadata(ReconnectLinkState.Connected, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty HalfDuplexLockEnabledProperty = DependencyProperty.Register(
            nameof(HalfDuplexLockEnabled), typeof(bool), typeof(Rs485BusCanvas),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ReconnectStatusTextProperty = DependencyProperty.Register(
            nameof(ReconnectStatusText), typeof(string), typeof(Rs485BusCanvas),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush TxFill = DofPalette.Solid("#89DCEB", 0.22);
        private static readonly Brush RxFill = DofPalette.Solid("#A6E3A1", 0.22);
        private static readonly Brush CollisionFill = DofPalette.Solid("#F38BA8", 0.30);
        private static readonly Brush HeartbeatFill = DofPalette.Solid("#CBA6F7", 0.22);
        private static readonly Brush ReconnectFill = DofPalette.Solid("#F9E2AF", 0.24);

        private static readonly Pen TxPen = DofPalette.Pen(DofPalette.Fov, 1.3);
        private static readonly Pen RxPen = DofPalette.Pen(DofPalette.Dof, 1.3);
        private static readonly Pen CollisionPen = DofPalette.Pen(DofPalette.Warn, 1.7);
        private static readonly Pen HeartbeatPen = DofPalette.Pen(DofPalette.Aperture, 1.3);
        private static readonly Pen ReconnectPen = DofPalette.Pen(DofPalette.Ray, 1.4, dash: 4, gap: 3);
        private static readonly Pen BusWirePen = DofPalette.Pen(DofPalette.Surface, 1.4);

        public Rs485BusCanvas()
        {
            ClipToBounds = true;
        }

        public IReadOnlyList<Rs485BusEvent>? Events
        {
            get => (IReadOnlyList<Rs485BusEvent>?)GetValue(EventsProperty);
            set => SetValue(EventsProperty, value);
        }

        public ReconnectLinkState LinkState
        {
            get => (ReconnectLinkState)GetValue(LinkStateProperty);
            set => SetValue(LinkStateProperty, value);
        }

        public bool HalfDuplexLockEnabled
        {
            get => (bool)GetValue(HalfDuplexLockEnabledProperty);
            set => SetValue(HalfDuplexLockEnabledProperty, value);
        }

        public string ReconnectStatusText
        {
            get => (string)GetValue(ReconnectStatusTextProperty);
            set => SetValue(ReconnectStatusTextProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 260 || h < 100)
            {
                return;
            }

            const double padX = 12;
            DofText.Draw(dc, this, "RS485 半双工两线总线 (A+ / B-) 帧时序与心跳退避状态", 10.2, DofPalette.White, padX, 16, bold: true);

            var stateBrush = LinkState switch
            {
                ReconnectLinkState.Connected => DofPalette.Dof,
                ReconnectLinkState.BackoffReconnecting => DofPalette.Ray,
                _ => DofPalette.Warn,
            };
            DofText.Draw(dc, this, ReconnectStatusText, 9.5, stateBrush, w - padX, 16, TextAlign.Right, bold: true);

            // Shared differential A+/B- wire line
            var wireY = h * 0.55;
            dc.DrawLine(BusWirePen, new Point(padX, wireY), new Point(w - padX, wireY));

            var events = Events;
            if (events is null || events.Count == 0)
            {
                DofText.Draw(dc, this, "点击“单步串行轮询”或“模拟并发冲突”观测 RS485 半双工帧时序",
                    10, DofPalette.Muted, w * 0.5, wireY - 8, TextAlign.Center);
            }
            else
            {
                var shown = events.TakeLast(8).ToArray();
                const double gap = 6;
                var usableW = w - padX * 2;
                var boxW = Math.Max(66, (usableW - gap * (shown.Length - 1)) / shown.Length);
                var boxH = Math.Min(64, Math.Max(46, h - 54));
                var boxY = wireY - boxH * 0.5;

                for (var i = 0; i < shown.Length; i++)
                {
                    var ev = shown[i];
                    var rect = new Rect(padX + i * (boxW + gap), boxY, boxW, boxH);
                    var (fill, pen, tagBrush) = ev.Direction switch
                    {
                        "TX" => (TxFill, TxPen, DofPalette.Fov),
                        "RX" => (RxFill, RxPen, DofPalette.Dof),
                        "COLLISION" => (CollisionFill, CollisionPen, DofPalette.Warn),
                        "RECONNECT" => (ReconnectFill, ReconnectPen, DofPalette.Ray),
                        _ => (HeartbeatFill, HeartbeatPen, DofPalette.Aperture),
                    };

                    dc.DrawRoundedRectangle(fill, pen, rect, 5, 5);
                    var cx = rect.Left + rect.Width * 0.5;
                    var innerW = rect.Width - 8;
                    DofText.Draw(dc, this, Truncate(ev.Direction, 8.8, innerW, true), 8.8, tagBrush, cx, rect.Top + 13, TextAlign.Center, bold: true);
                    DofText.Draw(dc, this, Truncate(ev.DeviceName, 9.2, innerW, true), 9.2, DofPalette.White, cx, rect.Top + boxH * 0.5 + 4, TextAlign.Center, bold: true);
                    DofText.Draw(dc, this, Truncate($"{ev.LatencyMs:F1}ms", 8.2, innerW, false), 8.2, DofPalette.Muted, cx, rect.Bottom - 5, TextAlign.Center);
                }
            }

            var footer = HalfDuplexLockEnabled
                ? "✓ SemaphoreSlim(1,1) 半双工互斥锁开启：严格遵循 TX → T3.5 转向 → RX 串行时序，零总线冲突"
                : "⚠ 互斥锁已关闭：多线程并发写 RS485 差分总线将导致电信号叠加碰撞（帧乱码 / CRC 全错）！";
            DofText.Draw(dc, this, footer, 8.8, HalfDuplexLockEnabled ? DofPalette.Muted : DofPalette.Warn, w * 0.5, h - 8, TextAlign.Center);
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
