using System.Windows;
using System.Windows.Media;
using LearnSmartManufacturing.Models;

namespace LearnSmartManufacturing.Controls
{
    /// <summary>Zoomed 1D profile along the optical axis: the DOF band and the workpiece offset ΔZ.</summary>
    public class DofProfileCanvas : FrameworkElement
    {
        public static readonly DependencyProperty ResultProperty = DependencyProperty.Register(
            nameof(Result), typeof(DofResult), typeof(DofProfileCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Pen TickPen = DofPalette.Pen(DofPalette.Solid("#2A2B3D"), 1);
        private static readonly Pen EdgePen = DofPalette.Pen(DofPalette.Dof, 1.5, 3, 2);
        private static readonly Pen CenterPen = DofPalette.Pen(DofPalette.Fov, 1.5);
        private static readonly Pen AxisPen = DofPalette.Pen(DofPalette.Solid("#45475A"), 1);
        private static readonly Pen WorkpieceInDofPen = DofPalette.Pen(DofPalette.Dof, 2);
        private static readonly Pen WorkpieceOutDofPen = DofPalette.Pen(DofPalette.Warn, 2);
        private static readonly Brush BandGradient = CreateBandGradient();

        public DofProfileCanvas()
        {
            ClipToBounds = true;
        }

        private static Brush CreateBandGradient()
        {
            var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(41, 166, 227, 161), 0));
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(107, 166, 227, 161), 0.5));
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(41, 166, 227, 161), 1));
            gradient.Freeze();
            return gradient;
        }

        public DofResult? Result
        {
            get => (DofResult?)GetValue(ResultProperty);
            set => SetValue(ResultProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(DofPalette.Canvas, null, new Rect(0, 0, w, h));
            if (Result is not { } r || w < 10 || h < 10)
            {
                return;
            }

            var halfSpan = r.ProfileHalfSpanMm;
            const double padX = 14;
            var trackW = w - padX * 2;
            double MapX(double dz) => padX + (dz + halfSpan) / (2 * halfSpan) * trackW;

            var centerX = MapX(0);
            var nearX = Math.Max(padX, MapX(-r.FrontDofMm));
            var rearFinite = double.IsFinite(r.RearDofMm);
            var farX = rearFinite ? Math.Min(w - padX, MapX(r.RearDofMm)) : w - padX;

            for (var i = 0; i <= 4; i++)
            {
                var x = padX + trackW * i / 4;
                dc.DrawLine(TickPen, new Point(x, 14), new Point(x, h - 18));
            }

            var bandW = Math.Max(2, farX - nearX);
            dc.DrawRectangle(BandGradient, null, new Rect(nearX, 16, bandW, h - 36));

            dc.DrawLine(EdgePen, new Point(nearX, 14), new Point(nearX, h - 18));
            if (rearFinite && MapX(r.RearDofMm) <= w - padX)
            {
                dc.DrawLine(EdgePen, new Point(farX, 14), new Point(farX, h - 18));
            }

            dc.DrawLine(CenterPen, new Point(centerX, 12), new Point(centerX, h - 16));
            dc.DrawLine(AxisPen, new Point(padX, h - 18), new Point(w - padX, h - 18));

            DofText.Draw(dc, this, $"-{halfSpan:F1}", 10, DofPalette.Muted, padX, h - 5);
            DofText.Draw(dc, this, "WD(0)", 10, DofPalette.Fov, centerX, h - 5, TextAlign.Center);
            DofText.Draw(dc, this, $"+{halfSpan:F1}mm", 10, DofPalette.Muted, w - padX, h - 5, TextAlign.Right);

            DofText.Draw(dc, this, "近界", 10, DofPalette.Dof, Math.Max(padX, Math.Min(centerX - 24, nearX)), 11);
            DofText.Draw(dc, this, rearFinite ? "远界" : "→ ∞", 10, DofPalette.Dof,
                Math.Min(w - padX, Math.Max(centerX + 24, farX)), 11, TextAlign.Right);

            var wpX = Math.Max(padX, Math.Min(w - padX, MapX(r.DeltaZMm)));
            var wpColor = r.IsInDof ? DofPalette.Dof : DofPalette.Warn;
            dc.DrawLine(r.IsInDof ? WorkpieceInDofPen : WorkpieceOutDofPen, new Point(wpX, 14), new Point(wpX, h - 18));

            var diamond = new StreamGeometry();
            using (var g = diamond.Open())
            {
                g.BeginFigure(new Point(wpX, 24), true, true);
                g.LineTo(new Point(wpX + 5, 31), false, false);
                g.LineTo(new Point(wpX, 38), false, false);
                g.LineTo(new Point(wpX - 5, 31), false, false);
            }

            diamond.Freeze();
            dc.DrawGeometry(wpColor, null, diamond);
        }
    }
}
