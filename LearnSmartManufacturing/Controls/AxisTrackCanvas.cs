using System.Windows;
using System.Windows.Media;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>1D linear stage visualization showing hard/soft limits, origin switch, target pin and live carriage.</summary>
    public class AxisTrackCanvas : FrameworkElement
    {
        public static readonly DependencyProperty CommandPositionMmProperty = DependencyProperty.Register(
            nameof(CommandPositionMm), typeof(double), typeof(AxisTrackCanvas),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ActualPositionMmProperty = DependencyProperty.Register(
            nameof(ActualPositionMm), typeof(double), typeof(AxisTrackCanvas),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TargetPositionMmProperty = DependencyProperty.Register(
            nameof(TargetPositionMm), typeof(double), typeof(AxisTrackCanvas),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty HardNegativeLimitMmProperty = DependencyProperty.Register(
            nameof(HardNegativeLimitMm), typeof(double), typeof(AxisTrackCanvas),
            new FrameworkPropertyMetadata(-25.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty HardPositiveLimitMmProperty = DependencyProperty.Register(
            nameof(HardPositiveLimitMm), typeof(double), typeof(AxisTrackCanvas),
            new FrameworkPropertyMetadata(210.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SoftNegativeLimitMmProperty = DependencyProperty.Register(
            nameof(SoftNegativeLimitMm), typeof(double), typeof(AxisTrackCanvas),
            new FrameworkPropertyMetadata(-20.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SoftPositiveLimitMmProperty = DependencyProperty.Register(
            nameof(SoftPositiveLimitMm), typeof(double), typeof(AxisTrackCanvas),
            new FrameworkPropertyMetadata(200.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty PositiveLimitProperty = DependencyProperty.Register(
            nameof(PositiveLimit), typeof(bool), typeof(AxisTrackCanvas),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty NegativeLimitProperty = DependencyProperty.Register(
            nameof(NegativeLimit), typeof(bool), typeof(AxisTrackCanvas),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty OriginSensorProperty = DependencyProperty.Register(
            nameof(OriginSensor), typeof(bool), typeof(AxisTrackCanvas),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IsHomedProperty = DependencyProperty.Register(
            nameof(IsHomed), typeof(bool), typeof(AxisTrackCanvas),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IsAlarmProperty = DependencyProperty.Register(
            nameof(IsAlarm), typeof(bool), typeof(AxisTrackCanvas),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush RailBg = DofPalette.Solid("#232436");
        private static readonly Brush SoftZoneFill = DofPalette.Solid("#89DCEB", 0.12);
        private static readonly Brush CarriageNormalFill = DofPalette.Solid("#89B4FA", 0.30);
        private static readonly Brush CarriageHomedFill = DofPalette.Solid("#A6E3A1", 0.28);
        private static readonly Brush CarriageAlarmFill = DofPalette.Solid("#F38BA8", 0.32);

        private static readonly Pen RailBorderPen = DofPalette.Pen(DofPalette.Surface, 1);
        private static readonly Pen TickPen = DofPalette.Pen(DofPalette.Solid("#45475A"), 1);
        private static readonly Pen SoftLimitPen = DofPalette.Pen(DofPalette.Solid("#89DCEB", 0.55), 1, 3, 2);
        private static readonly Pen HardLimitIdlePen = DofPalette.Pen(DofPalette.Solid("#F38BA8", 0.45), 1.5);
        private static readonly Pen HardLimitTripPen = DofPalette.Pen(DofPalette.Warn, 2.5);
        private static readonly Pen OriginIdlePen = DofPalette.Pen(DofPalette.Solid("#F9E2AF", 0.6), 1.2, 2, 2);
        private static readonly Pen OriginActivePen = DofPalette.Pen(DofPalette.Ray, 2.2);
        private static readonly Pen TargetPinPen = DofPalette.Pen(DofPalette.Aperture, 1.5, 3, 2);
        private static readonly Pen CarriageNormalPen = DofPalette.Pen(DofPalette.Accent, 1.8);
        private static readonly Pen CarriageHomedPen = DofPalette.Pen(DofPalette.Dof, 1.8);
        private static readonly Pen CarriageAlarmPen = DofPalette.Pen(DofPalette.Warn, 2.0);
        private static readonly Pen CommandLinePen = DofPalette.Pen(DofPalette.White, 1.5);

        public AxisTrackCanvas()
        {
            ClipToBounds = true;
        }

        public double CommandPositionMm { get => (double)GetValue(CommandPositionMmProperty); set => SetValue(CommandPositionMmProperty, value); }
        public double ActualPositionMm { get => (double)GetValue(ActualPositionMmProperty); set => SetValue(ActualPositionMmProperty, value); }
        public double TargetPositionMm { get => (double)GetValue(TargetPositionMmProperty); set => SetValue(TargetPositionMmProperty, value); }
        public double HardNegativeLimitMm { get => (double)GetValue(HardNegativeLimitMmProperty); set => SetValue(HardNegativeLimitMmProperty, value); }
        public double HardPositiveLimitMm { get => (double)GetValue(HardPositiveLimitMmProperty); set => SetValue(HardPositiveLimitMmProperty, value); }
        public double SoftNegativeLimitMm { get => (double)GetValue(SoftNegativeLimitMmProperty); set => SetValue(SoftNegativeLimitMmProperty, value); }
        public double SoftPositiveLimitMm { get => (double)GetValue(SoftPositiveLimitMmProperty); set => SetValue(SoftPositiveLimitMmProperty, value); }
        public bool PositiveLimit { get => (bool)GetValue(PositiveLimitProperty); set => SetValue(PositiveLimitProperty, value); }
        public bool NegativeLimit { get => (bool)GetValue(NegativeLimitProperty); set => SetValue(NegativeLimitProperty, value); }
        public bool OriginSensor { get => (bool)GetValue(OriginSensorProperty); set => SetValue(OriginSensorProperty, value); }
        public bool IsHomed { get => (bool)GetValue(IsHomedProperty); set => SetValue(IsHomedProperty, value); }
        public bool IsAlarm { get => (bool)GetValue(IsAlarmProperty); set => SetValue(IsAlarmProperty, value); }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (w < 120 || h < 40)
            {
                return;
            }

            const double padL = 44;
            const double padR = 44;
            var trackW = w - padL - padR;
            var minMm = HardNegativeLimitMm;
            var maxMm = HardPositiveLimitMm;
            var span = Math.Max(1.0, maxMm - minMm);

            double MapX(double mm) => padL + Math.Clamp((mm - minMm) / span, 0, 1) * trackW;

            var railY = h * 0.52;
            const double railH = 14;

            // Main mechanical rail
            dc.DrawRoundedRectangle(RailBg, RailBorderPen, new Rect(padL, railY - railH / 2, trackW, railH), 4, 4);

            // Soft limit travel zone
            var softX0 = MapX(SoftNegativeLimitMm);
            var softX1 = MapX(SoftPositiveLimitMm);
            dc.DrawRectangle(SoftZoneFill, null, new Rect(softX0, railY - railH / 2 + 1, Math.Max(1, softX1 - softX0), railH - 2));
            dc.DrawLine(SoftLimitPen, new Point(softX0, railY - 11), new Point(softX0, railY + 11));
            dc.DrawLine(SoftLimitPen, new Point(softX1, railY - 11), new Point(softX1, railY + 11));

            // Ruler ticks
            for (var mm = 0; mm <= 200; mm += 50)
            {
                var x = MapX(mm);
                dc.DrawLine(TickPen, new Point(x, railY + railH / 2), new Point(x, railY + railH / 2 + 5));
                DofText.Draw(dc, this, $"{mm}", 10, DofPalette.Subtle, x, h - 4, TextAlign.Center);
            }

            // Hard limit switches (-EL / +EL)
            var melX = MapX(HardNegativeLimitMm);
            var pelX = MapX(HardPositiveLimitMm);
            dc.DrawLine(NegativeLimit ? HardLimitTripPen : HardLimitIdlePen, new Point(melX, 12), new Point(melX, h - 16));
            dc.DrawLine(PositiveLimit ? HardLimitTripPen : HardLimitIdlePen, new Point(pelX, 12), new Point(pelX, h - 16));
            DofText.Draw(dc, this, "-EL", 10, NegativeLimit ? DofPalette.Warn : DofPalette.Subtle, melX - 6, railY + 4, TextAlign.Right, bold: NegativeLimit);
            DofText.Draw(dc, this, "+EL", 10, PositiveLimit ? DofPalette.Warn : DofPalette.Subtle, pelX + 6, railY + 4, TextAlign.Left, bold: PositiveLimit);

            // Origin switch (ORG @ 0 mm)
            var orgX = MapX(0);
            dc.DrawLine(OriginSensor ? OriginActivePen : OriginIdlePen, new Point(orgX, 14), new Point(orgX, railY + railH / 2));
            DofText.Draw(dc, this, "ORG", 10, OriginSensor ? DofPalette.Ray : DofPalette.Muted, orgX, 12, TextAlign.Center, bold: OriginSensor);

            // Target position marker
            var tgtX = MapX(TargetPositionMm);
            dc.DrawLine(TargetPinPen, new Point(tgtX, 14), new Point(tgtX, railY + 11));
            dc.DrawEllipse(DofPalette.Aperture, null, new Point(tgtX, 14), 3, 3);

            // Live carriage block (ActualPosition) + Command centerline
            var actX = MapX(ActualPositionMm);
            var cmdX = MapX(CommandPositionMm);
            const double carW = 24;
            const double carH = 22;
            var carFill = IsAlarm ? CarriageAlarmFill : (IsHomed ? CarriageHomedFill : CarriageNormalFill);
            var carPen = IsAlarm ? CarriageAlarmPen : (IsHomed ? CarriageHomedPen : CarriageNormalPen);

            dc.DrawRoundedRectangle(carFill, carPen, new Rect(actX - carW / 2, railY - carH / 2, carW, carH), 4, 4);
            dc.DrawLine(CommandLinePen, new Point(cmdX, railY - carH / 2 + 3), new Point(cmdX, railY + carH / 2 - 3));

            var posLabel = $"{ActualPositionMm:F2} mm";
            var labelAlign = actX > w - 100 ? TextAlign.Right : TextAlign.Left;
            var labelX = labelAlign == TextAlign.Right ? actX - carW / 2 - 6 : actX + carW / 2 + 6;
            DofText.Draw(dc, this, posLabel, 10, DofPalette.TextMain, labelX, 12, labelAlign, bold: true);
        }
    }
}
