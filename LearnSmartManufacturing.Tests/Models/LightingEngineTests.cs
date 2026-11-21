using LearnSmartManufacturing.Models.Vision;
using Xunit;

namespace LearnSmartManufacturing.Tests.Models
{
    public class LightingEngineTests
    {
        [Fact]
        public void MetalMirrorScratch_LowAngleDarkField_OutperformsHighAngleBrightField()
        {
            var darkField = LightingEngine.Simulate(new LightingParameters(
                LightGeometryType.LowAngleDarkField, WorkpieceType.MetalMirrorScratch, LightWavelength.Blue470,
                IncidenceAngleDeg: 20, WorkingDistanceMm: 120, UseCrossPolarizer: false, UseStrobeOverdrive: false, ExposureGainPercent: 100));

            var brightField = LightingEngine.Simulate(new LightingParameters(
                LightGeometryType.HighAngleRing, WorkpieceType.MetalMirrorScratch, LightWavelength.White,
                IncidenceAngleDeg: 70, WorkingDistanceMm: 120, UseCrossPolarizer: false, UseStrobeOverdrive: false, ExposureGainPercent: 100));

            Assert.True(darkField.IsDarkFieldMode);
            Assert.False(brightField.IsDarkFieldMode);
            Assert.True(darkField.FeatureGray > darkField.BackgroundGray);
            Assert.True(darkField.ContrastPercent > brightField.ContrastPercent + 35);
            Assert.True(brightField.GlareRiskPercent > darkField.GlareRiskPercent + 40);
        }

        [Fact]
        public void CoaxialAndCrossPolarizer_AccountForOpticalEfficiencyLosses()
        {
            var coaxial = LightingEngine.Simulate(new LightingParameters(
                LightGeometryType.Coaxial, WorkpieceType.WaferMicroPit, LightWavelength.Blue470,
                90, 120, UseCrossPolarizer: false, UseStrobeOverdrive: false, ExposureGainPercent: 100));

            var coaxialPol = LightingEngine.Simulate(new LightingParameters(
                LightGeometryType.Coaxial, WorkpieceType.WaferMicroPit, LightWavelength.Blue470,
                90, 120, UseCrossPolarizer: true, UseStrobeOverdrive: false, ExposureGainPercent: 100));

            Assert.Equal(25.0, coaxial.OpticalEfficiencyPercent);
            Assert.True(coaxialPol.OpticalEfficiencyPercent < 10.0); // 25% * 0.35 = 8.8%
            Assert.True(coaxial.IsOptimalSetup);
        }

        [Fact]
        public void DomeLight_EliminatesGlareOnCurvedSpecularMetal()
        {
            var ring = LightingEngine.Simulate(new LightingParameters(
                LightGeometryType.HighAngleRing, WorkpieceType.CurvedSpecularMetal, LightWavelength.White,
                70, 120, false, false, 100));

            var dome = LightingEngine.Simulate(new LightingParameters(
                LightGeometryType.Dome, WorkpieceType.CurvedSpecularMetal, LightWavelength.White,
                75, 120, false, false, 100));

            Assert.True(ring.GlareRiskPercent >= 80);
            Assert.True(dome.GlareRiskPercent <= 10);
            Assert.True(dome.IsOptimalSetup);
        }

        [Fact]
        public void Backlight_ExcelsForContourMeasurement_AndWarnsForSurfaceScratch()
        {
            var contour = LightingEngine.Simulate(new LightingParameters(
                LightGeometryType.Backlight, WorkpieceType.StampedContour, LightWavelength.Green525,
                90, 120, false, false, 100));

            var scratch = LightingEngine.Simulate(new LightingParameters(
                LightGeometryType.Backlight, WorkpieceType.MetalMirrorScratch, LightWavelength.Green525,
                90, 120, false, false, 100));

            Assert.True(contour.IsOptimalSetup);
            Assert.True(contour.ContrastPercent >= 85);
            Assert.False(scratch.IsOptimalSetup);
            Assert.Contains("剪影", scratch.EngineeringAdvice);
        }

        [Fact]
        public void ComplementaryWavelengths_SuppressOppositeColorAndBoostSameColor()
        {
            var underRed = LightingEngine.ComputeColorPatches(LightWavelength.Red630);
            var redUnderRed = underRed.Single(p => p.SurfaceColor == TargetSurfaceColor.Red).MonochromeGray;
            var greenUnderRed = underRed.Single(p => p.SurfaceColor == TargetSurfaceColor.Green).MonochromeGray;

            Assert.True(redUnderRed > 200);
            Assert.True(greenUnderRed < 40);

            var underBlue = LightingEngine.ComputeColorPatches(LightWavelength.Blue470);
            var yellowUnderBlue = underBlue.Single(p => p.SurfaceColor == TargetSurfaceColor.Yellow).MonochromeGray;
            Assert.True(yellowUnderBlue < 40);

            var (bestWave, _, delta) = LightingEngine.RecommendWavelengthForColorPair(
                TargetSurfaceColor.White, TargetSurfaceColor.Green);
            Assert.Equal(LightWavelength.Red630, bestWave);
            Assert.True(delta >= 180);
        }

        [Fact]
        public void CalculateStrobe_ComputesExactMaxPulseWidthAndDutyCycle()
        {
            // v = 500 mm/s, pixelRes = 50 um/px, maxBlur = 0.5 px => allowed dist = 25 um => 50 us
            var res = LightingEngine.CalculateStrobe(lineSpeedMmPerS: 500, pixelResUmPerPx: 50, maxBlurPx: 0.5, triggerFps: 20);

            Assert.Equal(50.0, res.MaxExposureUs);
            Assert.Equal(10.0, res.ActualBlurAt1msPx);
            Assert.True(res.NeedsStrobeOverdrive);
            Assert.Equal(0.10, res.DutyCyclePercent);
            Assert.True(res.IsDutyCycleSafe);
        }
    }
}
