using LearnSmartManufacturing.Models.Vision;
using Xunit;

namespace LearnSmartManufacturing.Tests.Models
{
    public class CameraSelectionEngineTests
    {
        [Fact]
        public void EvaluateSelection_DetectsVignettingWhenLensCircleSmallerThanSensorDiagonal()
        {
            var halfInchLens = CameraSelectionEngine.LensImageCircles.Single(l => l.Name.StartsWith("1/2″"));
            var twoThirdsLens = CameraSelectionEngine.LensImageCircles.Single(l => l.Name.StartsWith("2/3″"));

            // 100x80mm @ 0.05 -> 2000x1600 -> recommends 5MP IMX250 (2/3", diag ~11.02 mm)
            var badLens = CameraSelectionEngine.EvaluateSelection(0.05, 100, 80, 0, halfInchLens);
            var goodLens = CameraSelectionEngine.EvaluateSelection(0.05, 100, 80, 0, twoThirdsLens);

            Assert.True(badLens.IsValid);
            Assert.Equal("500 万", badLens.RecommendedCamera!.Name);
            Assert.True(badLens.HasVignetting);
            Assert.False(goodLens.HasVignetting);
        }

        [Fact]
        public void EvaluateSelection_AddingFovMarginScalesRequiredPixelsUpward()
        {
            var lens = CameraSelectionEngine.LensImageCircles[^1];
            var noMargin = CameraSelectionEngine.EvaluateSelection(0.05, 100, 80, fovMarginPercent: 0, lens);
            var with20Margin = CameraSelectionEngine.EvaluateSelection(0.05, 100, 80, fovMarginPercent: 20, lens);

            Assert.Equal(2000, noMargin.RequiredNx);
            Assert.Equal(1600, noMargin.RequiredNy);
            Assert.Equal(2400, with20Margin.RequiredNx);
            Assert.Equal(1920, with20Margin.RequiredNy);
        }

        [Fact]
        public void EvaluateBandwidth_ComputesExactThroughputAndInterfacePassFail()
        {
            // 2448 x 2048 x 1 B/px (Mono8) x 60 fps = 300.81 MB/s -> GigE (115 MB/s) fails, USB3 (380 MB/s) passes!
            var bw = CameraSelectionEngine.EvaluateBandwidth(2448, 2048, PixelFormatKind.Mono8, 60);

            Assert.InRange(bw.RequiredMBps, 300.0, 301.5);
            Assert.False(bw.Interfaces.Single(i => i.Name.StartsWith("GigE")).IsSupported);
            Assert.True(bw.Interfaces.Single(i => i.Name.StartsWith("USB3")).IsSupported);
            Assert.StartsWith("USB3", bw.RecommendedInterface);

            // RGB8 triples bandwidth -> 902.4 MB/s -> needs 10GigE
            var rgbBw = CameraSelectionEngine.EvaluateBandwidth(2448, 2048, PixelFormatKind.Rgb8, 60);
            Assert.StartsWith("10GigE", rgbBw.RecommendedInterface);
        }

        [Fact]
        public void EvaluateShutter_ComputesMotionBlurAndRollingSkewAccurately()
        {
            // v = 500 mm/s, exposure = 100 us => blur = 0.05 mm => 1.0 px @ 0.05 mm/px
            // readout = 10 ms => skew = 5.0 mm => 100 px skew!
            var res = CameraSelectionEngine.EvaluateShutter(
                lineSpeedMmPerS: 500, exposureUs: 100, rollingReadoutMs: 10.0, pixelAccuracyMm: 0.05, fovHeightMm: 80);

            Assert.Equal(1.0, res.MotionBlurPx);
            Assert.Equal(100.0, res.RollingSkewPx);
            Assert.False(res.IsRollingAcceptable);
            Assert.True(res.RollingSkewAngleDeg > 3.0);
        }
    }
}
