using LearnSmartManufacturing.Models.Vision;
using LearnSmartManufacturing.ViewModels;
using Xunit;

namespace LearnSmartManufacturing.Tests.ViewModels
{
    public class LightingViewModelTests
    {
        [Fact]
        public void Constructor_InitializesWithOptimalDefaultPresetAndValidOutputs()
        {
            var vm = new LightingViewModel();

            Assert.NotNull(vm.SimulationResult);
            Assert.True(vm.IsOptimalSetup);
            Assert.Equal(64, vm.SimulationResult.ScanlineProfile.Count);
            Assert.Equal(6, vm.ColorPatches.Count);
            Assert.NotNull(vm.StrobeResult);
        }

        [Fact]
        public void AllScenarioPresets_ProduceOptimalLightingConfigurations()
        {
            var vm = new LightingViewModel();

            foreach (var preset in vm.ScenarioPresets)
            {
                vm.ApplyScenarioPresetCommand.Execute(preset);
                Assert.True(vm.IsOptimalSetup, $"Preset '{preset.Name}' expected IsOptimalSetup=true, got contrast {vm.ContrastText} / glare {vm.GlareRiskText}");
                Assert.Equal(preset.Workpiece, vm.SelectedWorkpiece.Type);
                Assert.Equal(preset.RecommendedLight, vm.SelectedLightGeometry.Type);
            }
        }

        [Fact]
        public void SwitchingLightGeometry_UpdatesAngleAdjustability()
        {
            var vm = new LightingViewModel();

            vm.SelectedLightGeometry = vm.LightGeometryOptions.Single(l => l.Type == LightGeometryType.Coaxial);
            Assert.False(vm.IsAngleAdjustable);

            vm.SelectedLightGeometry = vm.LightGeometryOptions.Single(l => l.Type == LightGeometryType.DualBar);
            Assert.True(vm.IsAngleAdjustable);
        }

        [Fact]
        public void ApplyRecommendedWavelengthCommand_AppliesBestSpectralMatch()
        {
            var vm = new LightingViewModel();

            vm.SelectedTargetColor = vm.SurfaceColorOptions.Single(c => c.Color == TargetSurfaceColor.White);
            vm.SelectedBackgroundColor = vm.SurfaceColorOptions.Single(c => c.Color == TargetSurfaceColor.Green);
            vm.ApplyRecommendedWavelengthCommand.Execute(null);

            Assert.Equal(LightWavelength.Red630, vm.ActiveWavelength);
        }

        [Fact]
        public void ChangingStrobeParameters_RecalculatesMaxPulseWidthImmediately()
        {
            var vm = new LightingViewModel();

            vm.LineSpeedMmPerS = 1000;
            vm.PixelResUmPerPx = 40;
            vm.MaxBlurPx = 0.5;

            Assert.Equal(20.0, vm.StrobeResult.MaxExposureUs);
            Assert.True(vm.StrobeResult.NeedsStrobeOverdrive);
        }
    }
}
