namespace LearnSmartManufacturing.Models
{
    public readonly record struct DofParameters(
        double SensorWidthMm,
        double PixelSizeUm,
        int ResX,
        double FocalMm,
        double WdMm,
        double FNumber,
        double CocMultiplier,
        double DeltaZMm);

    /// <summary>Thin-lens imaging and depth-of-field results. Distances are measured from the optical center.</summary>
    public sealed record DofResult
    {
        public double SensorWidthMm { get; init; }
        public double PixelSizeUm { get; init; }
        public double FocalMm { get; init; }
        public double WdMm { get; init; }
        public double FNumber { get; init; }
        public double DeltaZMm { get; init; }

        /// <summary>Permitted circle of confusion c (μm).</summary>
        public double CocLimitUm { get; init; }

        public double FovMm { get; init; }
        public double FovAngleDeg { get; init; }
        public double Magnification { get; init; }
        public double PixelResUm { get; init; }
        public double ApertureDiameterMm { get; init; }
        public double AiryDiskUm { get; init; }

        public double HyperfocalMm { get; init; }
        public bool IsHyperfocal { get; init; }
        public double NearLimitMm { get; init; }
        public double FarLimitMm { get; init; }
        public double FrontDofMm { get; init; }
        public double RearDofMm { get; init; }
        public double TotalDofMm { get; init; }

        /// <summary>Workpiece object distance s = WD + ΔZ.</summary>
        public double ObjectDistanceMm { get; init; }

        public double BestImageDistanceMm { get; init; }
        public double ImageDistanceMm { get; init; }
        public double ActualCocUm { get; init; }
        public bool IsVirtualImage { get; init; }
        public bool IsInDof { get; init; }

        /// <summary>Half of the Z range (mm) shown by the zoomed DOF profile, chosen so both the band and the workpiece stay visible.</summary>
        public double ProfileHalfSpanMm
        {
            get
            {
                var refDof = double.IsFinite(TotalDofMm) ? TotalDofMm : FrontDofMm * 3;
                return Math.Max(2.0, Math.Min(80, Math.Max(refDof * 1.15, Math.Abs(DeltaZMm) * 1.35 + 1.5)));
            }
        }
    }

    public static class DofEngine
    {
        public const double WavelengthUm = 0.55;

        public static DofResult Compute(DofParameters p)
        {
            var h = p.SensorWidthMm;
            var f = p.FocalMm;
            var wd = p.WdMm;
            var n = p.FNumber;
            var cUm = p.PixelSizeUm * p.CocMultiplier;
            var cMm = cUm / 1000.0;

            var fovMm = h * wd / f;
            var fovAngleDeg = 2 * Math.Atan(h / 2.0 / f) * 180.0 / Math.PI;
            var magnification = f / (wd - f);
            var pixelResUm = fovMm / p.ResX * 1000.0;

            var apertureMm = f / n;
            var airyUm = 2.44 * WavelengthUm * n;

            var hyperfocal = f * f / (n * cMm) + f;
            var near = wd * (hyperfocal - f) / (hyperfocal + wd - 2 * f);
            var isHyperfocal = wd >= hyperfocal;
            var far = isHyperfocal ? double.PositiveInfinity : wd * (hyperfocal - f) / (hyperfocal - wd);

            var front = wd - near;
            var rear = isHyperfocal ? double.PositiveInfinity : far - wd;
            var total = isHyperfocal ? double.PositiveInfinity : far - near;

            // Blur circle of a workpiece at s = WD + ΔZ while the sensor sits at the image distance of WD.
            var s = wd + p.DeltaZMm;
            var v0 = f * wd / (wd - f);
            var v = double.PositiveInfinity;
            var actualCocUm = double.PositiveInfinity;
            var isVirtual = false;

            if (s > f + 0.01)
            {
                v = f * s / (s - f);
                actualCocUm = apertureMm * Math.Abs(v - v0) / v * 1000.0;
            }
            else
            {
                isVirtual = true;
            }

            return new DofResult
            {
                SensorWidthMm = h,
                PixelSizeUm = p.PixelSizeUm,
                FocalMm = f,
                WdMm = wd,
                FNumber = n,
                DeltaZMm = p.DeltaZMm,
                CocLimitUm = cUm,
                FovMm = fovMm,
                FovAngleDeg = fovAngleDeg,
                Magnification = magnification,
                PixelResUm = pixelResUm,
                ApertureDiameterMm = apertureMm,
                AiryDiskUm = airyUm,
                HyperfocalMm = hyperfocal,
                IsHyperfocal = isHyperfocal,
                NearLimitMm = near,
                FarLimitMm = far,
                FrontDofMm = front,
                RearDofMm = rear,
                TotalDofMm = total,
                ObjectDistanceMm = s,
                BestImageDistanceMm = v0,
                ImageDistanceMm = v,
                ActualCocUm = actualCocUm,
                IsVirtualImage = isVirtual,
                IsInDof = !isVirtual && actualCocUm <= cUm + 1e-4,
            };
        }
    }
}
