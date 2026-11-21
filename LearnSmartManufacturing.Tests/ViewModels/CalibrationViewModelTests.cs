using LearnSmartManufacturing.ViewModels;
using Xunit;

namespace LearnSmartManufacturing.Tests.ViewModels
{
    public class CalibrationViewModelTests
    {
        [Fact]
        public void Constructor_InitializesHealthyNinePointAndRotationFits()
        {
            var vm = new CalibrationViewModel();

            Assert.True(vm.AffineResult.IsValid);
            Assert.True(vm.IsRmseHealthy);
            Assert.Equal(9, vm.CalibrationPoints.Count);

            Assert.True(vm.CircleResult.IsValid);
            Assert.True(vm.IsCenterErrorHealthy);
            Assert.Equal(5, vm.RotationSamples.Count);
            Assert.NotNull(vm.AlignmentResult);
        }

        [Fact]
        public void OutlierInjectionAndRejection_ToggleAccuracyAsExpected()
        {
            var vm = new CalibrationViewModel();

            vm.DemoOutlierInjectionCommand.Execute(null);
            Assert.False(vm.IsRmseHealthy);
            Assert.Equal(9, vm.AffineResult.UsedPointCount);

            vm.DemoOutlierRejectionFixCommand.Execute(null);
            Assert.True(vm.IsRmseHealthy);
            Assert.Equal(8, vm.AffineResult.UsedPointCount);
        }

        [Fact]
        public void SmallSpanTrap_DegradesCenterAccuracyUntilRestored()
        {
            var vm = new CalibrationViewModel();

            vm.DemoSmallSpanTrapCommand.Execute(null);
            var badErr = vm.CircleResult.TrueCenterErrorMm;

            vm.RestoreOptimalRotationCalibrationCommand.Execute(null);
            var goodErr = vm.CircleResult.TrueCenterErrorMm;

            Assert.True(badErr > goodErr * 4.0);
            Assert.True(vm.IsCenterErrorHealthy);
        }
    }
}
