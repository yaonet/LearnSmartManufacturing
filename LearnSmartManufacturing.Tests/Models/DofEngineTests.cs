using LearnSmartManufacturing.Models;

namespace LearnSmartManufacturing.Tests.Models
{
    public class DofEngineTests
    {
        private static DofParameters DefaultParams(
            double focalMm = 16,
            double wdMm = 300,
            double fNumber = 5.6,
            double cocMultiplier = 2,
            double deltaZMm = 0) =>
            new(8.45, 3.45, 2448, focalMm, wdMm, fNumber, cocMultiplier, deltaZMm);

        [Fact]
        public void Compute_DefaultParameters_SatisfiesThinLensRelations()
        {
            var r = DofEngine.Compute(DefaultParams());

            var expectedV0 = 16.0 * 300.0 / (300.0 - 16.0);
            var expectedBeta = 16.0 / (300.0 - 16.0);
            var expectedFov = 8.45 / expectedBeta;
            var expectedAngle = 2.0 * Math.Atan(expectedFov / (2.0 * 300.0)) * 180.0 / Math.PI;
            var expectedAiry = 2.44 * DofEngine.WavelengthUm * 5.6 * (1.0 + expectedBeta);

            Assert.Equal(expectedV0, r.BestImageDistanceMm, precision: 9);
            Assert.Equal(expectedBeta, r.Magnification, precision: 9);
            Assert.Equal(expectedFov, r.FovMm, precision: 9);
            Assert.Equal(expectedAngle, r.FovAngleDeg, precision: 9);
            Assert.Equal(expectedAiry, r.AiryDiskUm, precision: 9);
            Assert.Equal(0.0, r.ActualCocUm, precision: 9);
            Assert.True(r.IsInDof);
            Assert.False(r.IsVirtualImage);
            Assert.False(r.IsHyperfocal);
        }

        [Fact]
        public void Compute_AtNearAndFarDofLimits_ActualCocMatchesCocLimit()
        {
            var baseline = DofEngine.Compute(DefaultParams());

            var atNear = DofEngine.Compute(DefaultParams(deltaZMm: baseline.NearLimitMm - baseline.WdMm));
            var atFar = DofEngine.Compute(DefaultParams(deltaZMm: baseline.FarLimitMm - baseline.WdMm));

            Assert.Equal(baseline.CocLimitUm, atNear.ActualCocUm, precision: 5);
            Assert.Equal(baseline.CocLimitUm, atFar.ActualCocUm, precision: 5);
            Assert.True(atNear.IsInDof);
            Assert.True(atFar.IsInDof);
        }

        [Fact]
        public void Compute_MacroOneToOne_DoublesEffectiveAiryDisk()
        {
            var r = DofEngine.Compute(DefaultParams(focalMm: 50, wdMm: 100, fNumber: 8.0));

            Assert.Equal(1.0, r.Magnification, precision: 9);
            Assert.Equal(8.45, r.FovMm, precision: 9);
            Assert.Equal(100.0, r.BestImageDistanceMm, precision: 9);
            Assert.Equal(2.44 * DofEngine.WavelengthUm * 8.0 * 2.0, r.AiryDiskUm, precision: 9);
        }

        [Fact]
        public void Compute_BeyondHyperfocal_ReturnsInfiniteRearAndTotalDof()
        {
            var r = DofEngine.Compute(DefaultParams(focalMm: 6, wdMm: 500, fNumber: 16));

            Assert.True(r.IsHyperfocal);
            Assert.True(double.IsPositiveInfinity(r.FarLimitMm));
            Assert.True(double.IsPositiveInfinity(r.RearDofMm));
            Assert.True(double.IsPositiveInfinity(r.TotalDofMm));
            Assert.True(double.IsFinite(r.FrontDofMm));
            Assert.True(r.ProfileHalfSpanMm >= 2.0);
        }

        [Fact]
        public void Compute_WorkpieceInsideFocalLength_MarksVirtualImage()
        {
            var r = DofEngine.Compute(DefaultParams(focalMm: 50, wdMm: 100, deltaZMm: -60));

            Assert.True(r.IsVirtualImage);
            Assert.False(r.IsInDof);
            Assert.True(double.IsPositiveInfinity(r.ActualCocUm));
            Assert.True(double.IsPositiveInfinity(r.ImageDistanceMm));
        }

        [Theory]
        [InlineData(0, 3.45, 2448, 16, 300, 5.6, 2, 0)]
        [InlineData(8.45, 0, 2448, 16, 300, 5.6, 2, 0)]
        [InlineData(8.45, 3.45, 0, 16, 300, 5.6, 2, 0)]
        [InlineData(8.45, 3.45, 2448, 0, 300, 5.6, 2, 0)]
        [InlineData(8.45, 3.45, 2448, 50, 50, 5.6, 2, 0)]
        [InlineData(8.45, 3.45, 2448, 50, 40, 5.6, 2, 0)]
        [InlineData(8.45, 3.45, 2448, 16, 300, 0, 2, 0)]
        [InlineData(8.45, 3.45, 2448, 16, 300, 5.6, 0, 0)]
        [InlineData(8.45, 3.45, 2448, 16, 300, 5.6, 2, double.NaN)]
        public void Compute_InvalidParameters_ThrowsArgumentOutOfRange(
            double sensorW, double pixelUm, int resX, double f, double wd, double fNum, double coc, double dz)
        {
            var p = new DofParameters(sensorW, pixelUm, resX, f, wd, fNum, coc, dz);
            Assert.Throws<ArgumentOutOfRangeException>(() => DofEngine.Compute(p));
        }
    }
}
