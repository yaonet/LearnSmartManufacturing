using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models.Prism;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Visualizes Prism's IModuleCatalog topological dependency graph (WhenAvailable vs OnDemand)
    /// on the left and EventAggregator's ThreadOption / Filter / WeakReference fan-out on the right.
    /// </summary>
    public class PrismModularityCanvas : FrameworkElement
    {
        public static readonly DependencyProperty ModulesProperty = DependencyProperty.Register(
            nameof(Modules), typeof(IReadOnlyList<PrismModuleSnapshot>), typeof(PrismModularityCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty DeliveriesProperty = DependencyProperty.Register(
            nameof(Deliveries), typeof(IReadOnlyList<PrismEventDeliveryRecord>), typeof(PrismModularityCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush LoadedFill = DofPalette.Solid("#A6E3A1", 0.22);
        private static readonly Brush AutoDepFill = DofPalette.Solid("#F9E2AF", 0.22);
        private static readonly Brush IdleFill = DofPalette.Solid("#1E1E2E", 0.88);
        private static readonly Brush RowFill = DofPalette.Solid("#1E1E2E", 0.78);
        private static readonly Brush RiskFill = DofPalette.Solid("#F38BA8", 0.20);

        private static readonly Pen DividerPen = DofPalette.Pen(DofPalette.Surface, 1.0);
        private static readonly Pen EdgePen = DofPalette.Pen(DofPalette.Surface, 1.3);
        private static readonly Pen EdgeActivePen = DofPalette.Pen(DofPalette.Dof, 1.6);
        private static readonly Pen LoadedPen = DofPalette.Pen(DofPalette.Dof, 1.6);
        private static readonly Pen AutoDepPen = DofPalette.Pen(DofPalette.Ray, 1.6);
        private static readonly Pen DashedIdlePen = CreateDashedPen(DofPalette.Muted, 1.1);
        private static readonly Pen OkRowPen = DofPalette.Pen(DofPalette.Fov, 1.1);
        private static readonly Pen WarnRowPen = DofPalette.Pen(DofPalette.Warn, 1.4);

        public PrismModularityCanvas()
        {
            ClipToBounds = true;
        }

        public IReadOnlyList<PrismModuleSnapshot>? Modules
        {
            get => (IReadOnlyList<PrismModuleSnapshot>?)GetValue(ModulesProperty);
            set => SetValue(ModulesProperty, value);
        }

        public IReadOnlyList<PrismEventDeliveryRecord>? Deliveries
        {
            get => (IReadOnlyList<PrismEventDeliveryRecord>?)GetValue(DeliveriesProperty);
            set => SetValue(DeliveriesProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 420 || h < 110) return;

            var splitX = w * 0.57;
            dc.DrawLine(DividerPen, new Point(splitX, 10), new Point(splitX, h - 10));

            DrawModuleGraph(dc, 12, 8, splitX - 24, h - 16);
            DrawEventAggregator(dc, splitX + 12, 8, w - splitX - 24, h - 16);
        }

        private void DrawModuleGraph(DrawingContext dc, double x, double y, double width, double height)
        {
            DofText.Draw(dc, this, "IModuleCatalog 拓扑依赖图 (WhenAvailable / OnDemand 懒加载)", 10.2, DofPalette.White, x, y + 10, bold: true);
            var mods = Modules;
            if (mods is null || mods.Count == 0) return;

            var nw = Math.Min(138, width * 0.27);
            var nh = 34.0;
            var col0 = x + 4;
            var col1 = x + (width - nw) * 0.5;
            var col2 = x + width - nw - 4;
            var midY = y + 24 + (height - 26 - nh) * 0.5;
            var topY = y + 28;
            var botY = y + height - nh - 6;

            var layout = new Dictionary<string, Rect>
            {
                ["CoreAuthModule"] = new(col0, midY, nw, nh),
                ["PlcCommModule"] = new(col1, topY, nw, nh),
                ["VisionModule"] = new(col1, botY, nw, nh),
                ["MotionModule"] = new(col2, topY, nw, nh),
                ["MesReportModule"] = new(col2, botY, nw, nh),
            };

            var byName = mods.ToDictionary(m => m.ModuleName, StringComparer.Ordinal);

            // Dependency edges (dependency -> dependent)
            foreach (var mod in mods)
            {
                if (!layout.TryGetValue(mod.ModuleName, out var toRect)) continue;
                foreach (var depName in mod.DependsOn)
                {
                    if (!layout.TryGetValue(depName, out var fromRect)) continue;
                    var active = mod.Status != PrismModuleStatus.NotLoaded &&
                                 byName.TryGetValue(depName, out var d) && d.Status != PrismModuleStatus.NotLoaded;
                    DrawConnector(dc, fromRect, toRect, active ? EdgeActivePen : EdgePen);
                }
            }

            // Module boxes
            foreach (var mod in mods)
            {
                if (!layout.TryGetValue(mod.ModuleName, out var rect)) continue;
                var (fill, pen, subBrush, statusTag) = mod.Status switch
                {
                    PrismModuleStatus.Initialized => (LoadedFill, LoadedPen, DofPalette.Dof, "已加载 ✓"),
                    PrismModuleStatus.AutoResolvedDependency => (AutoDepFill, AutoDepPen, DofPalette.Ray, "依赖自动拉起 ✓"),
                    _ => (IdleFill, DashedIdlePen, DofPalette.Muted, "OnDemand 未加载"),
                };

                dc.DrawRoundedRectangle(fill, pen, rect, 6, 6);
                var cx = rect.Left + rect.Width * 0.5;
                var innerW = Math.Max(20, rect.Width - 8);
                DofText.Draw(dc, this, Truncate(mod.DisplayName, 9.2, innerW, true), 9.2,
                    mod.Status == PrismModuleStatus.NotLoaded ? DofPalette.Muted : DofPalette.White,
                    cx, rect.Top + 13, TextAlign.Center, bold: mod.Status != PrismModuleStatus.NotLoaded);
                DofText.Draw(dc, this, Truncate(statusTag, 8.2, innerW, false), 8.2, subBrush, cx, rect.Bottom - 6, TextAlign.Center);
            }
        }

        private void DrawEventAggregator(DrawingContext dc, double x, double y, double width, double height)
        {
            DofText.Draw(dc, this, "EventAggregator (PubSubEvent 线程与过滤路由)", 10.2, DofPalette.Fov, x, y + 10, bold: true);
            var list = Deliveries;
            if (list is null || list.Count == 0) return;

            var top = y + 24;
            var rowH = Math.Min(34, (height - 26) / list.Count);
            for (var i = 0; i < list.Count; i++)
            {
                var d = list[i];
                var rect = new Rect(x, top + i * rowH, width, rowH - 4);
                var fill = d.IsMemoryLeakRisk ? RiskFill : RowFill;
                var pen = d.IsMemoryLeakRisk ? WarnRowPen : (d.Delivered ? OkRowPen : DashedIdlePen);
                dc.DrawRoundedRectangle(fill, pen, rect, 5, 5);

                var threadTag = d.ThreadOption switch
                {
                    PrismThreadOption.UIThread => "UIThread",
                    PrismThreadOption.BackgroundThread => "BGThread",
                    _ => "PubThread",
                };

                var leftTitle = $"{(d.Delivered ? "●" : "○")} {d.SubscriberName} [{threadTag}]";
                var titleBrush = d.IsMemoryLeakRisk ? DofPalette.Warn : (d.Delivered ? DofPalette.Dof : DofPalette.Muted);
                DofText.Draw(dc, this, Truncate(leftTitle, 8.8, width - 12, true), 8.8, titleBrush, rect.Left + 7, rect.Top + 11, bold: true);

                var detail = d.IsMemoryLeakRisk ? "⚠ 强引用未解绑将泄漏! · " + d.Reason : d.Reason;
                DofText.Draw(dc, this, Truncate(detail, 8.2, width - 14, false), 8.2,
                    d.IsMemoryLeakRisk ? DofPalette.Warn : DofPalette.White, rect.Left + 7, rect.Bottom - 5);
            }
        }

        private static void DrawConnector(DrawingContext dc, Rect from, Rect to, Pen pen)
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

            var perp = new Vector(-v.Y, v.X);
            dc.DrawLine(pen, p2, p2 - v * 6 + perp * 3.5);
            dc.DrawLine(pen, p2, p2 - v * 6 - perp * 3.5);
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

        private static Pen CreateDashedPen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness) { DashStyle = new DashStyle([3, 3], 0) };
            pen.Freeze();
            return pen;
        }
    }
}
