using LearnSmartManufacturing.Models.Vision;
using Xunit;

namespace LearnSmartManufacturing.Tests.Models
{
    public class CalibrationEngineTests
    {
        [Fact]
        public void SolveAffine_NoiseFreeNinePoints_RecoversExactScaleAngleAndInverse()
        {
            var pts = CalibrationEngine.GenerateNinePointDataset(
                HandEyeMountMode.EyeToHand, scaleMmPerPx: 0.05, installAngleDeg: 12.5,
                flipImageY: true, pixelNoiseSigmaPx: 0.0, injectOutlierAtPoint5: false);

            var res = CalibrationEngine.SolveAffine(pts);

            Assert.True(res.IsValid);
            Assert.Equal(9, res.UsedPointCount);
            Assert.Equal(0.05, res.ScaleXMmPerPx, precision: 4);
            Assert.Equal(0.05, res.ScaleYMmPerPx, precision: 4);
            Assert.Equal(12.5, res.RotationDeg, precision: 2);
            Assert.True(res.IsMirroredHandedness);
            Assert.True(res.RmseMm < 0.001);

            var (wx, wy) = res.TransformPixelToWorld(1500, 900);
            Assert.True(res.TryTransformWorldToPixel(wx, wy, out var iu, out var iv));
            Assert.Equal(1500, iu, precision: 3);
            Assert.Equal(900, iv, precision: 3);
        }

        [Fact]
        public void SolveAffine_CollinearPoints_ReturnsInvalidSingularError()
        {
            CalibrationPointPair[] collinear =
            [
                new(1, 100, 100, 10, 10),
                new(2, 200, 200, 20, 20),
                new(3, 300, 300, 30, 30),
            ];

            var res = CalibrationEngine.SolveAffine(collinear);
            Assert.False(res.IsValid);
            Assert.Contains("共线", res.ErrorMessage);
        }

        [Fact]
        public void SolveAffine_OutlierRejection_RemovesPoint5AndRestoresLowRmse()
        {
            var pts = CalibrationEngine.GenerateNinePointDataset(
                HandEyeMountMode.EyeToHand, 0.05, 10.0, flipImageY: true,
                pixelNoiseSigmaPx: 0.1, injectOutlierAtPoint5: true);

            var raw = CalibrationEngine.SolveAffine(pts, rejectOutliers: false);
            var trimmed = CalibrationEngine.SolveAffine(pts, rejectOutliers: true, outlierThresholdMm: 0.25);

            Assert.True(raw.RmseMm > 0.3);
            Assert.Equal(8, trimmed.UsedPointCount);
            Assert.True(trimmed.RmseMm < 0.02);
            Assert.True(trimmed.EvaluatedPoints.Single(p => p.Index == 5).IsOutlier);
        }

        [Fact]
        public void FitRotationCenter_NoiseFreeSamples_RecoversExactCenterAndRadius()
        {
            var samples = CalibrationEngine.GenerateRotationSamples(
                trueCenterX: 100.0, trueCenterY: 60.0, armRadiusMm: 22.0,
                baseAngleDeg: 30.0, spanDeg: 40.0, sampleCount: 5, measurementNoiseMm: 0.0);

            var fit = CalibrationEngine.FitRotationCenter(samples, groundTruthCx: 100.0, groundTruthCy: 60.0);

            Assert.True(fit.IsValid);
            Assert.Equal(100.0, fit.CenterX, precision: 3);
            Assert.Equal(60.0, fit.CenterY, precision: 3);
            Assert.Equal(22.0, fit.RadiusMm, precision: 3);
            Assert.True(fit.TrueCenterErrorMm < 0.001);
        }

        [Fact]
        public void FitRotationCenter_SmallRotationSpanAmplifiesNoiseErrorComparedToWideSpan()
        {
            var narrow = CalibrationEngine.GenerateRotationSamples(100, 60, 22, 30, spanDeg: 8, sampleCount: 3, measurementNoiseMm: 0.05);
            var wide = CalibrationEngine.GenerateRotationSamples(100, 60, 22, 30, spanDeg: 60, sampleCount: 5, measurementNoiseMm: 0.05);

            var fitNarrow = CalibrationEngine.FitRotationCenter(narrow, 100, 60);
            var fitWide = CalibrationEngine.FitRotationCenter(wide, 100, 60);

            Assert.True(fitNarrow.IsValid && fitWide.IsValid);
            Assert.True(fitNarrow.TrueCenterErrorMm > fitWide.TrueCenterErrorMm * 5.0);
        }

        [Fact]
        public void ComputeAlignmentCompensation_LandsExactlyOnTeachTargetWhileNaiveFails()
        {
            // Center (100, 60), Teach at R=20, 0 deg => (120, 60), Current rotated -15 deg & shifted (+3, -2)
            var res = CalibrationEngine.ComputeAlignmentCompensation(
                teachX: 120, teachY: 60, teachAngleDeg: 0,
                currentX: 123, currentY: 58, currentAngleDeg: -15,
                centerX: 100, centerY: 60);

            // Verify applying exact rotation + CorrectDelta lands dead-on Teach (120, 60)
            var finalX = res.RotatedX + res.CorrectDeltaX;
            var finalY = res.RotatedY + res.CorrectDeltaY;
            Assert.Equal(120.0, finalX, precision: 9);
            Assert.Equal(60.0, finalY, precision: 9);

            // Naive compensation ignoring eccentric rotation produces significant millimeter-level residual!
            Assert.True(res.NaiveResidualErrorMm > 4.0);
        }
    }
}
