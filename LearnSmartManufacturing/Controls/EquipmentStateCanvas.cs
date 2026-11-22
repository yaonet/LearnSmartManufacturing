using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Patterns;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Renders the 5-state Equipment Finite State Machine (State Pattern) on the left and
    /// the live decoupled Observer fan-out broadcast status on the right.
    /// </summary>
    public class EquipmentStateCanvas : FrameworkElement
    {
        public static readonly DependencyProperty CurrentStateProperty = DependencyProperty.Register(
            nameof(CurrentState), typeof(EquipmentStateKind), typeof(EquipmentStateCanvas),
            new FrameworkPropertyMetadata(EquipmentStateKind.Idle, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LastTransitionProperty = DependencyProperty.Register(
            nameof(LastTransition), typeof(StateTransitionResult), typeof(EquipmentStateCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty DispatchesProperty = DependencyProperty.Register(
            nameof(Dispatches), typeof(IReadOnlyList<ObserverDispatchRecord>), typeof(EquipmentStateCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush IdleFill = DofPalette.Solid("#89B4FA", 0.16);
        private static readonly Brush RunFill = DofPalette.Solid("#A6E3A1", 0.22);
        private static readonly Brush PauseFill = DofPalette.Solid("#F9E2AF", 0.20);
        private static readonly Brush FaultFill = DofPalette.Solid("#F38BA8", 0.26);
        private static readonly Brush MaintFill = DofPalette.Solid("#CBA6F7", 0.20);
        private static readonly Brush InactiveFill = DofPalette.Solid("#1E1E2E", 0.90);
        private static readonly Brush PanelFill = DofPalette.Solid("#1E1E2E", 0.72);

        private static readonly Pen EdgePen = DofPalette.Pen(DofPalette.Surface, 1.3);
        private static readonly Pen ActiveEdgeOkPen = DofPalette.Pen(DofPalette.Dof, 2.2);
        private static readonly Pen ActiveEdgeWarnPen = DofPalette.Pen(DofPalette.Warn, 2.2);
        private static readonly Pen InactiveNodePen = DofPalette.Pen(DofPalette.Surface, 1.1);
        private static readonly Pen DividerPen = DofPalette.Pen(DofPalette.Surface, 1.0);
        private static readonly Pen ObserverInfoPen = DofPalette.Pen(DofPalette.Fov, 1.0);
        private static readonly Pen NodeAccentPen = DofPalette.Pen(DofPalette.Accent, 2.0);
        private static readonly Pen NodeDofPen = DofPalette.Pen(DofPalette.Dof, 2.0);
        private static readonly Pen NodeRayPen = DofPalette.Pen(DofPalette.Ray, 2.0);
        private static readonly Pen NodeWarnPen = DofPalette.Pen(DofPalette.Warn, 2.0);
        private static readonly Pen NodeAperturePen = DofPalette.Pen(DofPalette.Aperture, 2.0);

        public EquipmentStateCanvas()
        {
            ClipToBounds = true;
        }

        public EquipmentStateKind CurrentState
        {
            get => (EquipmentStateKind)GetValue(CurrentStateProperty);
            set => SetValue(CurrentStateProperty, value);
        }

        public StateTransitionResult? LastTransition
        {
            get => (StateTransitionResult?)GetValue(LastTransitionProperty);
            set => SetValue(LastTransitionProperty, value);
        }

        public IReadOnlyList<ObserverDispatchRecord>? Dispatches
        {
            get => (IReadOnlyList<ObserverDispatchRecord>?)GetValue(DispatchesProperty);
            set => SetValue(DispatchesProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 400 || h < 110) return;

            var splitX = w * 0.67;
            dc.DrawLine(DividerPen, new Point(splitX, 10), new Point(splitX, h - 10));

            DrawStateMachine(dc, 12, 8, splitX - 24, h - 16);
            DrawObserverFanout(dc, splitX + 12, 8, w - splitX - 24, h - 16);
        }

        private void DrawStateMachine(DrawingContext dc, double x, double y, double width, double height)
        {
            DofText.Draw(dc, this, "State 状态模式 · 设备五态安全联锁状态机", 10.2, DofPalette.White, x, y + 10, bold: true);

            if (LastTransition is { } last)
            {
                var badgeBrush = last.IsAllowed ? DofPalette.Dof : DofPalette.Warn;
                var prefix = last.IsAllowed ? "✓ " : "✕ ";
                DofText.Draw(dc, this, Truncate(prefix + last.Message, 9.2, width * 0.64, true),
                    9.2, badgeBrush, x + width, y + 10, TextAlign.Right, bold: true);
            }

            var nw = Math.Min(116, width * 0.21);
            var nh = 34.0;
            var topY = y + 32;
            var botY = y + height - nh - 8;
            var midX = x + width * 0.5;

            var centers = new Dictionary<EquipmentStateKind, Rect>
            {
                [EquipmentStateKind.Idle] = new(x + width * 0.04, topY, nw, nh),
                [EquipmentStateKind.Running] = new(midX - nw * 0.5, topY, nw, nh),
                [EquipmentStateKind.Paused] = new(x + width * 0.96 - nw, topY, nw, nh),
                [EquipmentStateKind.Maintenance] = new(x + width * 0.16, botY, nw, nh),
                [EquipmentStateKind.Fault] = new(x + width * 0.84 - nw, botY, nw, nh),
            };

            // Background allowable transition lines
            DrawConnector(dc, centers[EquipmentStateKind.Idle], centers[EquipmentStateKind.Running], EdgePen);
            DrawConnector(dc, centers[EquipmentStateKind.Running], centers[EquipmentStateKind.Paused], EdgePen);
            DrawConnector(dc, centers[EquipmentStateKind.Idle], centers[EquipmentStateKind.Maintenance], EdgePen);
            DrawConnector(dc, centers[EquipmentStateKind.Running], centers[EquipmentStateKind.Fault], EdgePen);
            DrawConnector(dc, centers[EquipmentStateKind.Fault], centers[EquipmentStateKind.Idle], EdgePen);

            // Highlight last transition if state changed
            if (LastTransition is { IsAllowed: true } tr && tr.FromState != tr.ToState)
            {
                var p = tr.ToState == EquipmentStateKind.Fault ? ActiveEdgeWarnPen : ActiveEdgeOkPen;
                DrawConnector(dc, centers[tr.FromState], centers[tr.ToState], p, withArrow: true);
            }

            // Draw state nodes
            foreach (var (kind, rect) in centers)
            {
                var isActive = kind == CurrentState;
                var isRejectedHere = LastTransition is { IsAllowed: false } rej && rej.FromState == kind;

                var (activeFill, activePen, label) = kind switch
                {
                    EquipmentStateKind.Idle => (IdleFill, NodeAccentPen, "Idle 待机"),
                    EquipmentStateKind.Running => (RunFill, NodeDofPen, "Running 运行"),
                    EquipmentStateKind.Paused => (PauseFill, NodeRayPen, "Paused 暂停"),
                    EquipmentStateKind.Fault => (FaultFill, NodeWarnPen, "Fault 报警"),
                    _ => (MaintFill, NodeAperturePen, "Maint 维保"),
                };

                var fill = isActive ? activeFill : InactiveFill;
                var pen = isRejectedHere ? NodeWarnPen : (isActive ? activePen : InactiveNodePen);

                dc.DrawRoundedRectangle(fill, pen, rect, 7, 7);
                var cx = rect.Left + rect.Width * 0.5;
                DofText.Draw(dc, this, label, 9.6, isActive ? DofPalette.White : DofPalette.Muted,
                    cx, rect.Top + rect.Height * 0.5 + 3.5, TextAlign.Center, bold: isActive);
            }
        }

        private void DrawObserverFanout(DrawingContext dc, double x, double y, double width, double height)
        {
            DofText.Draw(dc, this, "Observer 观察者一对多广播", 10.2, DofPalette.Fov, x, y + 10, bold: true);
            var list = Dispatches;
            if (list is null || list.Count == 0)
            {
                DofText.Draw(dc, this, "暂无活跃订阅者（请在下方勾选）", 9.5, DofPalette.Muted, x, y + height * 0.5 + 4);
                return;
            }

            var top = y + 24;
            var rowH = Math.Min(24, (height - 26) / Math.Max(1, list.Count));
            for (var i = 0; i < list.Count; i++)
            {
                var item = list[i];
                var ry = top + i * rowH;
                var rect = new Rect(x, ry, width, rowH - 3);
                var pen = item.IsAlert ? ActiveEdgeWarnPen : ObserverInfoPen;
                dc.DrawRoundedRectangle(PanelFill, pen, rect, 4, 4);

                var leftText = $"● {item.SubscriberName}";
                var subBrush = item.IsAlert ? DofPalette.Warn : DofPalette.Dof;
                DofText.Draw(dc, this, leftText, 8.8, subBrush, rect.Left + 6, rect.Top + rect.Height * 0.5 + 3, bold: true);
                DofText.Draw(dc, this, Truncate(item.ActionTaken, 8.4, width - 98, false),
                    8.4, DofPalette.White, rect.Right - 6, rect.Top + rect.Height * 0.5 + 3, TextAlign.Right);
            }
        }

        private static void DrawConnector(DrawingContext dc, Rect from, Rect to, Pen pen, bool withArrow = false)
        {
            var c1 = new Point(from.Left + from.Width * 0.5, from.Top + from.Height * 0.5);
            var c2 = new Point(to.Left + to.Width * 0.5, to.Top + to.Height * 0.5);
            var v = c2 - c1;
            var len = v.Length;
            if (len < 1) return;
            v /= len;

            var p1 = ClipToRectBorder(c1, from, v);
            var p2 = ClipToRectBorder(c2, to, -v);
            dc.DrawLine(pen, p1, p2);

            if (withArrow)
            {
                var perp = new Vector(-v.Y, v.X);
                dc.DrawLine(pen, p2, p2 - v * 7 + perp * 4);
                dc.DrawLine(pen, p2, p2 - v * 7 - perp * 4);
            }
        }

        private static Point ClipToRectBorder(Point center, Rect rect, Vector dir)
        {
            var hw = rect.Width * 0.5 + 2;
            var hh = rect.Height * 0.5 + 2;
            var tx = Math.Abs(dir.X) > 1e-6 ? hw / Math.Abs(dir.X) : double.PositiveInfinity;
            var ty = Math.Abs(dir.Y) > 1e-6 ? hh / Math.Abs(dir.Y) : double.PositiveInfinity;
            return center + dir * Math.Min(tx, ty);
        }

        private string Truncate(string text, double size, double maxW, bool bold)
        {
            if (maxW <= 12) return "…";
            if (DofText.Measure(this, text, size, bold) <= maxW) return text;
            var len = text.Length;
            while (len > 2 && DofText.Measure(this, text[..len] + "…", size, bold) > maxW) len--;
            return text[..Math.Max(1, len)] + "…";
        }
    }
}
