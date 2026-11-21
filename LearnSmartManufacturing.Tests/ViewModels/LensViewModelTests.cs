using LearnSmartManufacturing.ViewModels;

namespace LearnSmartManufacturing.Tests.ViewModels
{
    public class LensViewModelTests
    {
        [Fact]
        public void Defaults_UseConsistentTwoThirdsInchSensorWidth()
        {
            var vm = new LensViewModel();

            Assert.Equal(8.45, vm.SensorWidth);
            Assert.NotNull(vm.SelectedSensorPreset);
            Assert.Equal(8.45, vm.SelectedSensorPreset.Width);
            Assert.Equal("8.45", vm.SensorWidthText);
            Assert.True(vm.HasResult);
            Assert.Contains("16 mm", vm.NearestStandardText);
        }

        [Fact]
        public void FocalLengthAndPreset_SynchronizeBidirectionally()
        {
            var vm = new LensViewModel();

            vm.SelectedPreset = vm.Presets[3]; // 50 mm
            Assert.Equal(50, vm.FocalLength);

            vm.FocalLength = 21; // non-preset focal length
            Assert.Null(vm.SelectedPreset);

            vm.FocalLength = 25;
            Assert.Equal(vm.Presets[2], vm.SelectedPreset);
        }

        [Fact]
        public void SensorWidthTextAndPreset_SynchronizeBidirectionally()
        {
            var vm = new LensViewModel();

            vm.SelectedSensorPreset = vm.SensorPresets[3]; // 1" (12.8 mm)
            Assert.Equal("12.8", vm.SensorWidthText);

            vm.SensorWidthText = "10.5";
            Assert.Null(vm.SelectedSensorPreset);

            vm.SensorWidthText = "6.4";
            Assert.Equal(vm.SensorPresets[1], vm.SelectedSensorPreset);
        }

        [Fact]
        public void InvalidCalculatorInput_ClearsResultAndShowsHint()
        {
            var vm = new LensViewModel
            {
                FovWidthText = "0",
            };

            Assert.False(vm.HasResult);
            Assert.Equal("—", vm.ResultText);
            Assert.Equal(string.Empty, vm.NearestStandardText);
            Assert.Equal("请输入大于 0 的数值", vm.Hint);
        }

        [Fact]
        public void NewEngineeringTools_ComputeCatalogMountTelecentricAndDistortionLive()
        {
            var vm = new LensViewModel();

            Assert.Equal(9, vm.LensCatalogRows.Count);
            Assert.Single(vm.LensCatalogRows, r => r.IsNearestRecommended && r.FocalLengthMm == 16);

            // Default C lens on CS camera => compatible with 5 mm adapter ring
            Assert.True(vm.MountMatchResult.IsCompatible);
            Assert.True(vm.MountMatchResult.NeedsAdapter);

            // Trigger classic CS lens on C camera trap
            vm.DemoCsLensOnCMountTrapCommand.Execute(null);
            Assert.False(vm.MountMatchResult.IsCompatible);

            // Telecentric holds tolerance under ΔZ = 8 mm while FA fails
            Assert.False(vm.TelecentricResult.FaWithinTolerance);
            Assert.True(vm.TelecentricResult.TcWithinTolerance);
        }
    }
}
