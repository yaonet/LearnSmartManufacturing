using LearnSmartManufacturing.Models.Vision;
using Xunit;

namespace LearnSmartManufacturing.Tests.Models
{
    public class LensEngineeringEngineTests
    {
        [Fact]
        public void MountCompatibility_EnforcesExactFlangeFocalDistancePhysics()
        {
            // C-mount lens (17.526 mm) on CS-mount camera (12.526 mm) => needs 5.00 mm adapter ring
            var cOnCs = LensEngineeringEngine.EvaluateMountCompatibility(LensMountKind.CsMount, LensMountKind.CMount);
            Assert.True(cOnCs.IsCompatible);
            Assert.True(cOnCs.NeedsAdapter);
            Assert.Equal(5.0, cOnCs.AdapterThicknessMm, precision: 3);

            // CS-mount lens (12.526 mm) on C-mount camera (17.526 mm) => impossible to focus at infinity!
            var csOnC = LensEngineeringEngine.EvaluateMountCompatibility(LensMountKind.CMount, LensMountKind.CsMount);
            Assert.False(csOnC.IsCompatible);

            // Same mount => direct attach
            var cOnC = LensEngineeringEngine.EvaluateMountCompatibility(LensMountKind.CMount, LensMountKind.CMount);
            Assert.True(cOnC.IsCompatible);
            Assert.False(cOnC.NeedsAdapter);
        }

        [Fact]
        public void ExtensionTube_ComputesThinLensExtensionAndDarkenedEffectiveFNumber()
        {
            // f = 16 mm, WD = 80 mm => deltaV = 16^2 / (80 - 16) = 256 / 64 = 4.0 mm, beta = 0.25, F_eff = 4 * 1.25 = 5.0
            var res = LensEngineeringEngine.EvaluateExtensionTube(focalLengthMm: 16, targetWdMm: 80, nominalFNumber: 4.0);

            Assert.True(res.IsValid);
            Assert.Equal(4.0, res.ExtensionMm);
            Assert.Equal(0.25, res.Magnification);
            Assert.Equal(5.0, res.EffectiveFNumber);
        }

        [Fact]
        public void TelecentricVsFa_DemonstratesZeroPerspectiveErrorAdvantage()
        {
            // D = 50 mm, WD = 200 mm, deltaZ = +10 mm (closer to lens) => FA measures 50 * 200/190 = 52.632 mm (+5.26%)
            var res = LensEngineeringEngine.EvaluateTelecentricVsFa(
                nominalDiameterMm: 50, deltaZMm: 10, workingDistanceMm: 200, toleranceMm: 0.20, telecentricityDeg: 0.08);

            Assert.False(res.FaWithinTolerance);
            Assert.True(res.TcWithinTolerance);
            Assert.InRange(res.FaMeasuredMm, 52.6, 52.7);
            Assert.True(Math.Abs(res.TcErrorMm) < 0.05);
        }

        [Fact]
        public void RadialDistortion_UndistortReducesEdgeShiftDramatically()
        {
            var raw = LensEngineeringEngine.EvaluateDistortion(distortionPercent: -4.0, sensorResX: 2448, undistortApplied: false);
            var calibrated = LensEngineeringEngine.EvaluateDistortion(distortionPercent: -4.0, sensorResX: 2448, undistortApplied: true);

            Assert.True(raw.EdgeShiftPx > 45.0);
            Assert.True(calibrated.EdgeShiftPx < 2.5);
            Assert.Contains("桶形", raw.DistortionKindText);
        }
    }
}
