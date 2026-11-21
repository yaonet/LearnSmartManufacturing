using System.Windows;
using System.Windows.Media;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>
    /// Live architectural diagram highlighting active MVVM data-flow pathways
    /// (View &lt;-&gt; ObservableValidator VM &lt;-&gt; WeakReferenceMessenger &lt;-&gt; ObservableRecipient VM).
    /// </summary>
    public class MvvmFlowCanvas : FrameworkElement
    {
        public static readonly DependencyProperty ActiveCategoryProperty = DependencyProperty.Register(
            nameof(ActiveCategory), typeof(string), typeof(MvvmFlowCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LastEventTextProperty = DependencyProperty.Register(
            nameof(LastEventText), typeof(string), typeof(MvvmFlowCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty HasValidationErrorsProperty = DependencyProperty.Register(
            nameof(HasValidationErrors), typeof(bool), typeof(MvvmFlowCanvas),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IsMonitorActiveProperty = DependencyProperty.Register(
            nameof(IsMonitorActive), typeof(bool), typeof(MvvmFlowCanvas),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TracePulseCountProperty = DependencyProperty.Register(
            nameof(TracePulseCount), typeof(int), typeof(MvvmFlowCanvas),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush NodeBg = DofPalette.Solid("#1E1E2E");
        private static readonly Brush NodeActiveCyan = DofPalette.Solid("#89DCEB", 0.16);
        private static readonly Brush NodeActiveRay = DofPalette.Solid("#F9E2AF", 0.16);
        private static readonly Brush NodeActivePurple = DofPalette.Solid("#CBA6F7", 0.18);
        private static readonly Brush NodeErrorBg = DofPalette.Solid("#F38BA8", 0.18);

        private static readonly Pen IdleBorderPen = DofPalette.Pen(DofPalette.Surface, 1.2);
        private static readonly Pen CyanBorderPen = DofPalette.Pen(DofPalette.Fov, 1.8);
        private static readonly Pen RayBorderPen = DofPalette.Pen(DofPalette.Ray, 1.8);
        private static readonly Pen PurpleBorderPen = DofPalette.Pen(DofPalette.Aperture, 1.8);
        private static readonly Pen GreenBorderPen = DofPalette.Pen(DofPalette.Dof, 1.6);
        private static readonly Pen ErrorBorderPen = DofPalette.Pen(DofPalette.Warn, 1.8);

        private static readonly Pen ArrowIdlePen = DofPalette.Pen(DofPalette.Solid("#585B70"), 1.4);
        private static readonly Pen ArrowBrokenPen = DofPalette.Pen(DofPalette.Solid("#585B70", 0.5), 1.3, 3, 3);
        private static readonly Pen ArrowCyanPen = DofPalette.Pen(DofPalette.Fov, 2.2);
        private static readonly Pen ArrowRayPen = DofPalette.Pen(DofPalette.Ray, 2.2);
        private static readonly Pen ArrowPurplePen = DofPalette.Pen(DofPalette.Aperture, 2.2);

        public MvvmFlowCanvas()
        {
            ClipToBounds = true;
        }

        public string? ActiveCategory { get => (string?)GetValue(ActiveCategoryProperty); set => SetValue(ActiveCategoryProperty, value); }
        public string? LastEventText { get => (string?)GetValue(LastEventTextProperty); set => SetValue(LastEventTextProperty, value); }
        public bool HasValidationErrors { get => (bool)GetValue(HasValidationErrorsProperty); set => SetValue(HasValidationErrorsProperty, value); }
        public bool IsMonitorActive { get => (bool)GetValue(IsMonitorActiveProperty); set => SetValue(IsMonitorActiveProperty, value); }
        public int TracePulseCount { get => (int)GetValue(TracePulseCountProperty); set => SetValue(TracePulseCountProperty, value); }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 360 || h < 42)
            {
                return;
            }

            const double padX = 14;
            const double padY = 8;
            const double arrowW = 46;
            var nodeW = (w - padX * 2 - arrowW * 3) / 4.0;
            var nodeH = h - padY * 2;
            var midY = h * 0.5;

            var cat = ActiveCategory;
            var isBinding = cat == "Binding";
            var isValidator = cat == "Validator";
            var isCommand = cat == "Command";
            var isMessenger = cat == "Messenger";

            var r0 = new Rect(padX, padY, nodeW, nodeH);
            var r1 = new Rect(r0.Right + arrowW, padY, nodeW, nodeH);
            var r2 = new Rect(r1.Right + arrowW, padY, nodeW, nodeH);
            var r3 = new Rect(r2.Right + arrowW, padY, nodeW, nodeH);

            // Node 0: View (XAML)
            DrawNode(dc, r0, "View (XAML)", "零业务 Code-Behind",
                isBinding ? NodeActiveCyan : (isCommand ? NodeActiveRay : NodeBg),
                isBinding ? CyanBorderPen : (isCommand ? RayBorderPen : IdleBorderPen),
                isBinding ? DofPalette.Fov : (isCommand ? DofPalette.Ray : DofPalette.TextMain));

            // Arrow 0 <-> 1: Binding / Command
            var arrow01Pen = isBinding ? ArrowCyanPen : (isCommand ? ArrowRayPen : ArrowIdlePen);
            DrawDoubleArrow(dc, r0.Right + 4, r1.Left - 4, midY, arrow01Pen);
            DofText.Draw(dc, this, isCommand ? "Command" : "Binding", 9,
                isCommand ? DofPalette.Ray : (isBinding ? DofPalette.Fov : DofPalette.Subtle),
                (r0.Right + r1.Left) * 0.5, midY - 7, TextAlign.Center, bold: isBinding || isCommand);

            // Node 1: Editor VM (ObservableValidator)
            var editorBg = HasValidationErrors
                ? NodeErrorBg
                : (isValidator || isBinding ? NodeActiveCyan : (isCommand ? NodeActiveRay : (isMessenger ? NodeActivePurple : NodeBg)));
            var editorPen = HasValidationErrors
                ? ErrorBorderPen
                : (isValidator ? GreenBorderPen : (isCommand ? RayBorderPen : (isMessenger ? PurpleBorderPen : IdleBorderPen)));
            var editorSub = HasValidationErrors ? "✗ 存在校验错误 (禁用下发)" : "ObservableValidator ✓";
            DrawNode(dc, r1, "RecipeEditorVM", editorSub, editorBg, editorPen,
                HasValidationErrors ? DofPalette.Warn : DofPalette.Dof);

            // Arrow 1 <-> 2: Messenger Send
            var arrow12Pen = isMessenger ? ArrowPurplePen : ArrowIdlePen;
            DrawDoubleArrow(dc, r1.Right + 4, r2.Left - 4, midY, arrow12Pen);
            DofText.Draw(dc, this, "Send", 9, isMessenger ? DofPalette.Aperture : DofPalette.Subtle,
                (r1.Right + r2.Left) * 0.5, midY - 7, TextAlign.Center, bold: isMessenger);

            // Node 2: WeakReferenceMessenger
            DrawNode(dc, r2, "WeakReferenceMessenger", "弱引用解耦总线 (防泄漏)",
                isMessenger ? NodeActivePurple : NodeBg,
                isMessenger ? PurpleBorderPen : IdleBorderPen,
                DofPalette.Aperture);

            // Arrow 2 <-> 3: Messenger Receive (broken dashed line if Monitor IsActive == false)
            var arrow23Pen = !IsMonitorActive ? ArrowBrokenPen : (isMessenger ? ArrowPurplePen : ArrowIdlePen);
            DrawDoubleArrow(dc, r2.Right + 4, r3.Left - 4, midY, arrow23Pen);
            DofText.Draw(dc, this, IsMonitorActive ? "Receive" : "已退订", 9,
                !IsMonitorActive ? DofPalette.Warn : (isMessenger ? DofPalette.Aperture : DofPalette.Subtle),
                (r2.Right + r3.Left) * 0.5, midY - 7, TextAlign.Center, bold: isMessenger || !IsMonitorActive);

            // Node 3: Monitor VM (ObservableRecipient)
            var monBg = IsMonitorActive && isMessenger ? NodeActivePurple : NodeBg;
            var monPen = !IsMonitorActive ? IdleBorderPen : (isMessenger ? PurpleBorderPen : GreenBorderPen);
            var monSub = IsMonitorActive ? "ObservableRecipient · 已激活" : "IsActive=False (已退订)";
            DrawNode(dc, r3, "ProductionMonitorVM", monSub, monBg, monPen,
                IsMonitorActive ? DofPalette.Dof : DofPalette.Subtle);
        }

        private void DrawNode(DrawingContext dc, Rect rect, string title, string subtitle, Brush bg, Pen border, Brush titleBrush)
        {
            dc.DrawRoundedRectangle(bg, border, rect, 6, 6);
            var cx = rect.Left + rect.Width * 0.5;
            var cy = rect.Top + rect.Height * 0.5;
            DofText.Draw(dc, this, title, 11, titleBrush, cx, cy - 2, TextAlign.Center, bold: true);
            DofText.Draw(dc, this, subtitle, 9.5, DofPalette.Muted, cx, cy + 12, TextAlign.Center);
        }

        private static void DrawDoubleArrow(DrawingContext dc, double x1, double x2, double y, Pen pen)
        {
            dc.DrawLine(pen, new Point(x1, y), new Point(x2, y));
            dc.DrawLine(pen, new Point(x2 - 5, y - 3.5), new Point(x2, y));
            dc.DrawLine(pen, new Point(x2 - 5, y + 3.5), new Point(x2, y));
            dc.DrawLine(pen, new Point(x1 + 5, y - 3.5), new Point(x1, y));
            dc.DrawLine(pen, new Point(x1 + 5, y + 3.5), new Point(x1, y));
        }
    }
}
