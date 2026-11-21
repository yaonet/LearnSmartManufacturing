using LearnSmartManufacturing.ViewModels;

namespace LearnSmartManufacturing.Tests.ViewModels
{
    public class SimulationViewModelTests
    {
        [Fact]
        public void ResetCommand_RestoresDefaultsInSingleRecompute()
        {
            var vm = new SimulationViewModel
            {
                SelectedSensor = null,
            };
            vm.SelectedSensor = vm.Sensors[0];
            vm.FocalMm = 35;
            vm.WdMm = 200;
            vm.FNumber = 2.8;
            vm.CocMultiplier = 1;
            vm.DeltaZMm = 20;

            var resultNotifications = 0;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SimulationViewModel.Result))
                {
                    resultNotifications++;
                }
            };

            vm.ResetCommand.Execute(null);

            Assert.Equal(1, resultNotifications);
            Assert.Equal(vm.Sensors[1], vm.SelectedSensor);
            Assert.Equal(16, vm.FocalMm);
            Assert.Equal(300, vm.WdMm);
            Assert.Equal(5.6, vm.FNumber);
            Assert.Equal(2, vm.CocMultiplier);
            Assert.Equal(0, vm.DeltaZMm);
        }

        [Fact]
        public void ZeroDeltaZAndToggleInsetCommands_UpdateState()
        {
            var vm = new SimulationViewModel { DeltaZMm = 25 };

            vm.ZeroDeltaZCommand.Execute(null);
            Assert.Equal(0, vm.DeltaZMm);
            Assert.True(vm.IsInDof);

            Assert.False(vm.IsInsetExpanded);
            Assert.Equal("展开", vm.InsetToggleText);
            vm.ToggleInsetCommand.Execute(null);
            Assert.True(vm.IsInsetExpanded);
            Assert.Equal("收起", vm.InsetToggleText);
        }

        [Fact]
        public void SmallAperture_TriggersDiffractionWarning()
        {
            var vm = new SimulationViewModel { FNumber = 8.0 };
            Assert.False(vm.ShowDiffractionWarning);

            vm.FNumber = 11.0;
            Assert.True(vm.ShowDiffractionWarning);
        }

        [Fact]
        public void Presets_SynchronizeWithNumericProperties()
        {
            var vm = new SimulationViewModel();

            vm.SelectedFocalPreset = vm.FocalPresets.First(p => p.Value == 25);
            Assert.Equal(25, vm.FocalMm);

            vm.SelectedFNumberPreset = vm.FNumberPresets.First(p => p.Value == 2.8);
            Assert.Equal(2.8, vm.FNumber);

            vm.SelectedCocPreset = vm.CocPresets.First(p => p.Value == 3);
            Assert.Equal(3, vm.CocMultiplier);
        }
    }
}
