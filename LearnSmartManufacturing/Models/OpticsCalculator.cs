namespace LearnSmartManufacturing.Models
{
    public readonly record struct OpticsResult(double FovWidth, double FovAngleDeg, double Magnification);

    /// <summary>Pinhole / similar-triangle optical approximation (WD ≫ f).</summary>
    public static class OpticsCalculator
    {
        public const double MinFocalLength = 8;
        public const double MaxFocalLength = 50;
        public const double MinWorkingDistance = 150;
        public const double MaxWorkingDistance = 500;

        /// <summary>f = Ws · WD / FOV (all in mm)</summary>
        public static double SolveFocalLength(double wd, double fovWidth, double sensorWidth)
        {
            ValidateFinitePositive(wd, nameof(wd));
            ValidateFinitePositive(fovWidth, nameof(fovWidth));
            ValidateFinitePositive(sensorWidth, nameof(sensorWidth));
            return sensorWidth * wd / fovWidth;
        }

        /// <param name="f">Focal length (mm)</param>
        /// <param name="wd">Working distance (mm)</param>
        /// <param name="sensorWidth">Sensor width (mm)</param>
        public static OpticsResult Compute(double f, double wd, double sensorWidth)
        {
            ValidateFinitePositive(f, nameof(f));
            ValidateFinitePositive(wd, nameof(wd));
            ValidateFinitePositive(sensorWidth, nameof(sensorWidth));

            var fovWidth = sensorWidth * wd / f;
            var fovAngleDeg = 2 * Math.Atan(sensorWidth / (2 * f)) * 180 / Math.PI;
            return new OpticsResult(fovWidth, fovAngleDeg, f / wd);
        }

        private static void ValidateFinitePositive(double value, string paramName)
        {
            if (!double.IsFinite(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(paramName, value, "Value must be finite and greater than zero.");
            }
        }
    }
}
