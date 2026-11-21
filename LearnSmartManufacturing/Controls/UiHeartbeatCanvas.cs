using System.Windows;
using System.Windows.Media;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Live UI-thread responsiveness monitor driven by <see cref="CompositionTarget.Rendering"/>.
    /// Freezes visibly whenever the WPF UI thread is synchronously blocked or deadlocked,
    /// and highlights the detected stall duration.
    /// </summary>
    public class UiHeartbeatCanvas : FrameworkElement
    {
        private static readonly Brush TrackBg = DofPalette.Solid("#1E1E2E");
        private static readonly Brush PulseHaloOk = DofPalette.Solid("#A6E3A1", 0.28);
        private static readonly Brush PulseHaloStall = DofPalette.Solid("#F38BA8", 0.35);
        private static readonly Pen TrackBorderPen = DofPalette.Pen(DofPalette.Surface, 1);

        private TimeSpan _lastRenderTime;
        private double _phase;
        private double _fps = 60;
        private double _lastStallMs;
        private double _stallHighlightSec;
        private bool _subscribed;

        public UiHeartbeatCanvas()
        {
            ClipToBounds = true;
            Loaded += (_, _) => Attach();
            Unloaded += (_, _) => Detach();
        }

        private void Attach()
        {
            if (!_subscribed)
            {
                _subscribed = true;
                _lastRenderTime = TimeSpan.Zero;
                CompositionTarget.Rendering += OnRendering;
            }
        }

        private void Detach()
        {
            if (_subscribed)
            {
                CompositionTarget.Rendering -= OnRendering;
                _subscribed = false;
                _lastRenderTime = TimeSpan.Zero;
            }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (e is not RenderingEventArgs re)
            {
                return;
            }

            if (_lastRenderTime == TimeSpan.Zero)
            {
                _lastRenderTime = re.RenderingTime;
                InvalidateVisual();
                return;
            }

            var dt = (re.RenderingTime - _lastRenderTime).TotalSeconds;
            if (dt <= 0)
            {
                return;
            }

            _lastRenderTime = re.RenderingTime;
            var dtMs = dt * 1000.0;
            if (dtMs > 120.0)
            {
                _lastStallMs = dtMs;
                _stallHighlightSec = 3.5;
            }
            else
            {
                var instFps = Math.Clamp(1.0 / dt, 1, 240);
                _fps = _fps * 0.85 + instFps * 0.15;
                if (_stallHighlightSec > 0)
                {
                    _stallHighlightSec = Math.Max(0, _stallHighlightSec - dt);
                }
            }

            _phase = (_phase + Math.Min(dt, 0.05) * 1.4) % 2.0;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 120 || h < 20)
            {
                return;
            }

            dc.DrawRoundedRectangle(TrackBg, TrackBorderPen, new Rect(0, 0, w, h), 6, 6);

            var isStalled = _stallHighlightSec > 0;
            var accent = isStalled ? DofPalette.Warn : DofPalette.Dof;

            // Left label
            var leftEnd = 10 + DofText.Draw(dc, this, "UI 主线程心跳", 11, DofPalette.TextMain, 10, h * 0.5 + 4, bold: true) + 12;

            // Right FPS / stall telemetry
            var statusText = isStalled
                ? $"⚠ 检测到阻塞卡顿 {_lastStallMs:F0} ms"
                : $"流畅 {_fps:F0} FPS · T{Environment.CurrentManagedThreadId:D2}";
            var statusW = DofText.Measure(this, statusText, 11, true);
            DofText.Draw(dc, this, statusText, 11, accent, w - 10, h * 0.5 + 4, TextAlign.Right, bold: true);

            // Smooth ping-pong pulse bar in the middle
            var barL = leftEnd;
            var barR = w - statusW - 22;
            if (barR - barL < 40)
            {
                return;
            }

            var barY = h * 0.5;
            dc.DrawRoundedRectangle(DofPalette.Canvas, null, new Rect(barL, barY - 4, barR - barL, 8), 4, 4);

            var pos01 = _phase <= 1.0 ? _phase : 2.0 - _phase;
            var dotX = barL + 8 + pos01 * (barR - barL - 16);
            dc.DrawEllipse(isStalled ? PulseHaloStall : PulseHaloOk, null, new Point(dotX, barY), 10, 10);
            dc.DrawEllipse(accent, null, new Point(dotX, barY), 4.5, 4.5);
        }
    }
}
