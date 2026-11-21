using LearnSmartManufacturing.ViewModels;

namespace LearnSmartManufacturing.Tests.ViewModels
{
    public class CameraViewModelTests
    {
        [Fact]
        public void Defaults_ComputeExpectedResolutionAndRecommendation()
        {
            var vm = new CameraViewModel();

            Assert.True(vm.HasResult);
            Assert.Equal("2,000 × 1,600 px", vm.ResolutionText);
            Assert.Contains("3.20 MP", vm.PixelCountText);
            Assert.Contains("500 万", vm.RecommendationText);
            Assert.Equal(string.Empty, vm.Hint);
        }

        [Fact]
        public void Recommendation_AcceptsRotatedSensorOrientation()
        {
            var vm = new CameraViewModel
            {
                PixelAccuracyText = "0.1",
                TargetLengthText = "100",
                TargetWidthText = "128",
            };

            Assert.True(vm.HasResult);
            Assert.Contains("130 万", vm.RecommendationText);
        }

        [Fact]
        public void Recommendation_ExceedingStandardAreaScan_SuggestsLineScanOrMultiCamera()
        {
            var vm = new CameraViewModel
            {
                PixelAccuracyText = "0.01",
                TargetLengthText = "200",
                TargetWidthText = "100",
            };

            Assert.True(vm.HasResult);
            Assert.Contains("线扫描", vm.RecommendationText);
        }

        [Theory]
        [InlineData("", "100", "80")]
        [InlineData("0", "100", "80")]
        [InlineData("-0.05", "100", "80")]
        [InlineData("abc", "100", "80")]
        public void InvalidInputs_HideResultAndShowPositiveHint(string accuracy, string length, string width)
        {
            var vm = new CameraViewModel
            {
                PixelAccuracyText = accuracy,
                TargetLengthText = length,
                TargetWidthText = width,
            };

            Assert.False(vm.HasResult);
            Assert.Equal("—", vm.ResolutionText);
            Assert.Equal("—", vm.PixelCountText);
            Assert.Equal(string.Empty, vm.Substitution);
            Assert.Equal(string.Empty, vm.RecommendationText);
            Assert.Equal("请输入大于 0 的数值", vm.Hint);
        }

        [Theory]
        [InlineData("0.00000001", "100000", "80")]
        [InlineData("1e-300", "1e20", "80")]
        public void OverflowingInputs_HideResultAndShowRangeHint(string accuracy, string length, string width)
        {
            var vm = new CameraViewModel
            {
                PixelAccuracyText = accuracy,
                TargetLengthText = length,
                TargetWidthText = width,
            };

            Assert.False(vm.HasResult);
            Assert.Equal("—", vm.ResolutionText);
            Assert.Contains("超出", vm.Hint);
        }
    }
}
