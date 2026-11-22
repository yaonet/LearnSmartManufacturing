using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Prism;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Visualizes Prism's Shell ContentRegion live view instances (Active vs KeepAlive cached)
    /// alongside the 5-hook INavigationAware / IConfirmNavigationRequest execution chain.
    /// </summary>
    public class PrismRegionLifecycleCanvas : FrameworkElement
    {
        public static readonly DependencyProperty OutcomeProperty = DependencyProperty.Register(
            nameof(Outcome), typeof(PrismNavigationOutcome), typeof(PrismRegionLifecycleCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush RegionBg = DofPalette.Solid("#1E1E2E", 0.85);
        private static readonly Brush ActiveFill = DofPalette.Solid("#A6E3A1", 0.22);
        private static readonly Brush CachedFill = DofPalette.Solid("#89B4FA", 0.16);
        private static readonly Brush WarnFill = DofPalette.Solid("#F9E2AF", 0.22);
        private static readonly Brush BlockFill = DofPalette.Solid("#F38BA8", 0.28);
        private static readonly Brush InfoFill = DofPalette.Solid("#89DCEB", 0.16);

        private static readonly Pen DividerPen = DofPalette.Pen(DofPalette.Surface, 1.0);
        private static readonly Pen ActivePen = DofPalette.Pen(DofPalette.Dof, 1.8);
        private static readonly Pen CachedPen = DofPalette.Pen(DofPalette.Accent, 1.2);
        private static readonly Pen WarnPen = DofPalette.Pen(DofPalette.Ray, 1.4);
        private static readonly Pen BlockPen = DofPalette.Pen(DofPalette.Warn, 1.8);
        private static readonly Pen InfoPen = DofPalette.Pen(DofPalette.Fov, 1.2);
        private static readonly Pen ArrowPen = DofPalette.Pen(DofPalette.Muted, 1.2);

        public PrismRegionLifecycleCanvas()
        {
            ClipToBounds = true;
        }

        public PrismNavigationOutcome? Outcome
        {
            get => (PrismNavigationOutcome?)GetValue(OutcomeProperty);
            set => SetValue(OutcomeProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 420 || h < 100 || Outcome is not { } res) return;

            var splitX = w * 0.43;
            dc.DrawLine(DividerPen, new Point(splitX, 10), new Point(splitX, h - 10));

            DrawRegionHost(dc, 12, 8, splitX - 24, h - 16, res);
            DrawLifecyclePipeline(dc, splitX + 12, 8, w - splitX - 24, h - 16, res);
        }

        private void DrawRegionHost(DrawingContext dc, double x, double y, double width, double height, PrismNavigationOutcome res)
        {
            DofText.Draw(dc, this, "Shell · ContentRegion 驻留视图实例", 10.2, DofPalette.White, x, y + 10, bold: true);
            DofText.Draw(dc, this, $"驻留 {res.RegionViews.Count} 页 · Journal {res.JournalIndex + 1}/{res.JournalEntries.Count}",
                9.0, DofPalette.Fov, x + width, y + 10, TextAlign.Right, bold: true);

            var shellRect = new Rect(x, y + 20, width, height - 22);
            dc.DrawRoundedRectangle(RegionBg, DividerPen, shellRect, 6, 6);

            var views = res.RegionViews;
            if (views.Count == 0) return;

            const double pad = 8;
            const double gap = 6;
            var cardW = (shellRect.Width - pad * 2 - gap * Math.Max(0, views.Count - 1)) / views.Count;
            var cardH = shellRect.Height - pad * 2;

            for (var i = 0; i < views.Count; i++)
            {
                var v = views[i];
                var rect = new Rect(shellRect.Left + pad + i * (cardW + gap), shellRect.Top + pad, cardW, cardH);
                dc.DrawRoundedRectangle(v.IsActive ? ActiveFill : CachedFill, v.IsActive ? ActivePen : CachedPen, rect, 5, 5);

                var cx = rect.Left + rect.Width * 0.5;
                var innerW = Math.Max(20, rect.Width - 8);
                DofText.Draw(dc, this, v.IsActive ? "● ACTIVE" : "○ CACHED", 8.2,
                    v.IsActive ? DofPalette.Dof : DofPalette.Accent, cx, rect.Top + 13, TextAlign.Center, bold: true);
                DofText.Draw(dc, this, Truncate($"{v.DisplayName}", 9.4, innerW, true), 9.4,
                    DofPalette.White, cx, rect.Top + rect.Height * 0.5 + 2, TextAlign.Center, bold: true);
                DofText.Draw(dc, this, Truncate($"实例 #{v.InstanceId} · KeepAlive={(v.KeepAlive ? "T" : "F")}", 8.2, innerW, false), 8.2,
                    DofPalette.Muted, cx, rect.Bottom - 7, TextAlign.Center);
            }
        }

        private void DrawLifecyclePipeline(DrawingContext dc, double x, double y, double width, double height, PrismNavigationOutcome res)
        {
            DofText.Draw(dc, this, "Prism 导航生命周期钩子调用链", 10.2, DofPalette.White, x, y + 10, bold: true);
            DofText.Draw(dc, this, Truncate(res.Summary, 9.2, width * 0.62, true), 9.2,
                res.Succeeded ? DofPalette.Dof : DofPalette.Warn, x + width, y + 10, TextAlign.Right, bold: true);

            var steps = res.Steps;
            if (steps.Count == 0) return;

            const double gap = 10;
            var count = steps.Count;
            var boxW = (width - gap * (count - 1)) / count;
            var boxY = y + 24;
            var boxH = height - 28;

            for (var i = 0; i < count; i++)
            {
                var s = steps[i];
                var rect = new Rect(x + i * (boxW + gap), boxY, boxW, boxH);
                var (fill, pen, labelBrush) = s.Status switch
                {
                    "Pass" => (ActiveFill, ActivePen, DofPalette.Dof),
                    "Warn" => (WarnFill, WarnPen, DofPalette.Ray),
                    "Blocked" => (BlockFill, BlockPen, DofPalette.Warn),
                    _ => (InfoFill, InfoPen, DofPalette.Fov),
                };

                dc.DrawRoundedRectangle(fill, pen, rect, 5, 5);
                var cx = rect.Left + rect.Width * 0.5;
                var innerW = Math.Max(20, rect.Width - 8);
                DofText.Draw(dc, this, $"STEP {i + 1}", 8.0, labelBrush, cx, rect.Top + 13, TextAlign.Center, bold: true);
                DofText.Draw(dc, this, Truncate(s.HookName, 9.2, innerW, true), 9.2, DofPalette.White, cx, rect.Top + rect.Height * 0.5 + 1, TextAlign.Center, bold: true);
                DofText.Draw(dc, this, Truncate(s.Target, 8.2, innerW, false), 8.2, labelBrush, cx, rect.Bottom - 8, TextAlign.Center);

                if (i < count - 1)
                {
                    var ay = rect.Top + rect.Height * 0.5;
                    var ax1 = rect.Right + 1;
                    var ax2 = rect.Right + gap - 1;
                    dc.DrawLine(ArrowPen, new Point(ax1, ay), new Point(ax2, ay));
                    dc.DrawLine(ArrowPen, new Point(ax2 - 3.5, ay - 3), new Point(ax2, ay));
                    dc.DrawLine(ArrowPen, new Point(ax2 - 3.5, ay + 3), new Point(ax2, ay));
                }
            }
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
