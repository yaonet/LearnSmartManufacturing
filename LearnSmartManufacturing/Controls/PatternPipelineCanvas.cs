using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Patterns;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Visualizes the live Factory -> Decorator Stack -> Chain of Responsibility pipeline
    /// execution with per-stage Pass / Recovered / Blocked / Skipped status coloring.
    /// </summary>
    public class PatternPipelineCanvas : FrameworkElement
    {
        public static readonly DependencyProperty ResultProperty = DependencyProperty.Register(
            nameof(Result), typeof(PipelineExecutionResult), typeof(PatternPipelineCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush PassFill = DofPalette.Solid("#A6E3A1", 0.20);
        private static readonly Brush RecoverFill = DofPalette.Solid("#F9E2AF", 0.22);
        private static readonly Brush BlockFill = DofPalette.Solid("#F38BA8", 0.28);
        private static readonly Brush SkipFill = DofPalette.Solid("#6C7086", 0.16);
        private static readonly Brush NeutralFill = DofPalette.Solid("#89B4FA", 0.18);

        private static readonly Pen PassPen = DofPalette.Pen(DofPalette.Dof, 1.4);
        private static readonly Pen RecoverPen = DofPalette.Pen(DofPalette.Ray, 1.5);
        private static readonly Pen BlockPen = DofPalette.Pen(DofPalette.Warn, 1.7);
        private static readonly Pen NeutralPen = DofPalette.Pen(DofPalette.Accent, 1.2);
        private static readonly Pen ArrowOkPen = DofPalette.Pen(DofPalette.Dof, 1.5);
        private static readonly Pen ArrowBlockedPen = DofPalette.Pen(DofPalette.Warn, 1.5);
        private static readonly Pen SkipPen = CreateDashedPen(DofPalette.Muted, 1.1);

        public PatternPipelineCanvas()
        {
            ClipToBounds = true;
        }

        public PipelineExecutionResult? Result
        {
            get => (PipelineExecutionResult?)GetValue(ResultProperty);
            set => SetValue(ResultProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 320 || h < 90) return;

            if (Result is not { } res || res.VisualNodes.Count == 0)
            {
                DofText.Draw(dc, this, "点击“执行工站过站流水线”，实时追踪 Factory → Decorator → Chain of Responsibility 执行链路",
                    11.5, DofPalette.Muted, w * 0.5, h * 0.5 + 4, TextAlign.Center);
                return;
            }

            const double padX = 14;
            DofText.Draw(dc, this, "Factory 驱动创建  →  Decorator 透明增强  →  Chain of Responsibility 四级过站拦截链",
                10.5, DofPalette.White, padX, 18, bold: true);
            DofText.Draw(dc, this, res.FinalVerdict, 10.2, res.OverallPassed ? DofPalette.Dof : DofPalette.Warn,
                w - padX, 18, TextAlign.Right, bold: true);

            var nodes = res.VisualNodes;
            var count = nodes.Count;
            const double gap = 16;
            var totalW = w - padX * 2;
            var boxW = (totalW - gap * (count - 1)) / count;
            var boxY = 32.0;
            var boxH = Math.Max(56, h - boxY - 14);

            for (var i = 0; i < count; i++)
            {
                var node = nodes[i];
                var x = padX + i * (boxW + gap);
                var rect = new Rect(x, boxY, boxW, boxH);

                var (fill, pen, badgeBrush) = node.State switch
                {
                    PipelineNodeState.Passed => (PassFill, PassPen, DofPalette.Dof),
                    PipelineNodeState.Recovered => (RecoverFill, RecoverPen, DofPalette.Ray),
                    PipelineNodeState.Blocked => (BlockFill, BlockPen, DofPalette.Warn),
                    PipelineNodeState.Skipped => (SkipFill, SkipPen, DofPalette.Muted),
                    _ => (NeutralFill, NeutralPen, DofPalette.Accent),
                };

                dc.DrawRoundedRectangle(fill, pen, rect, 6, 6);

                var cx = rect.Left + rect.Width * 0.5;
                var innerW = Math.Max(20, rect.Width - 10);
                var groupBrush = node.Group switch
                {
                    "Factory" => DofPalette.Accent,
                    "Decorator" => DofPalette.Aperture,
                    _ => DofPalette.Fov,
                };

                DofText.Draw(dc, this, node.Group.ToUpperInvariant(), 8.2, groupBrush, cx, rect.Top + 14, TextAlign.Center, bold: true);
                DofText.Draw(dc, this, Truncate(node.Title, 10.0, innerW, true), 10.0,
                    node.State == PipelineNodeState.Skipped ? DofPalette.Muted : DofPalette.White,
                    cx, rect.Top + rect.Height * 0.5 + 3, TextAlign.Center, bold: true);
                DofText.Draw(dc, this, Truncate(node.Subtitle, 8.6, innerW, false), 8.6,
                    badgeBrush, cx, rect.Bottom - 8, TextAlign.Center);

                if (i < count - 1)
                {
                    var nextState = nodes[i + 1].State;
                    var arrowPen = node.State == PipelineNodeState.Blocked
                        ? ArrowBlockedPen
                        : nextState == PipelineNodeState.Skipped ? SkipPen : ArrowOkPen;
                    var ay = rect.Top + rect.Height * 0.5;
                    var ax1 = rect.Right + 2;
                    var ax2 = rect.Right + gap - 2;
                    dc.DrawLine(arrowPen, new Point(ax1, ay), new Point(ax2, ay));
                    dc.DrawLine(arrowPen, new Point(ax2 - 4, ay - 3.5), new Point(ax2, ay));
                    dc.DrawLine(arrowPen, new Point(ax2 - 4, ay + 3.5), new Point(ax2, ay));
                }
            }
        }

        private string Truncate(string text, double size, double maxW, bool bold)
        {
            if (DofText.Measure(this, text, size, bold) <= maxW) return text;
            var len = text.Length;
            while (len > 2 && DofText.Measure(this, text[..len] + "…", size, bold) > maxW) len--;
            return text[..Math.Max(1, len)] + "…";
        }

        private static Pen CreateDashedPen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness) { DashStyle = new DashStyle([3, 3], 0) };
            pen.Freeze();
            return pen;
        }
    }
}
