using LearnSmartManufacturing.Models;

namespace LearnSmartManufacturing.Tests.Models
{
    public class OpticsCalculatorTests
    {
        [Fact]
        public void Compute_ReturnsSimilarTriangleFovAngleAndMagnification()
        {
            var result = OpticsCalculator.Compute(16, 300, 8.45);

            Assert.Equal(8.45 * 300.0 / 16.0, result.FovWidth, precision: 9);
            Assert.Equal(2.0 * Math.Atan(8.45 / 32.0) * 180.0 / Math.PI, result.FovAngleDeg, precision: 9);
            Assert.Equal(16.0 / 300.0, result.Magnification, precision: 9);
        }

        [Fact]
        public void SolveFocalLength_InvertsComputeFov()
        {
            var optics = OpticsCalculator.Compute(25, 400, 8.45);
            var solved = OpticsCalculator.SolveFocalLength(400, optics.FovWidth, 8.45);

            Assert.Equal(25.0, solved, precision: 9);
        }

        [Theory]
        [InlineData(0, 300, 8.45)]
        [InlineData(16, -10, 8.45)]
        [InlineData(16, 300, double.NaN)]
        [InlineData(double.PositiveInfinity, 300, 8.45)]
        public void Compute_InvalidInputs_ThrowsArgumentOutOfRange(double f, double wd, double sw)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => OpticsCalculator.Compute(f, wd, sw));
        }

        [Theory]
        [InlineData(0, 160, 8.45)]
        [InlineData(300, 0, 8.45)]
        [InlineData(300, 160, -1)]
        public void SolveFocalLength_InvalidInputs_ThrowsArgumentOutOfRange(double wd, double fov, double sw)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => OpticsCalculator.SolveFocalLength(wd, fov, sw));
        }
    }
}
