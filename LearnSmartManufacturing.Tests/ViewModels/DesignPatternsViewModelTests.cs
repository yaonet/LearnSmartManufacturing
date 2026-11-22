using LearnSmartManufacturing.Models.Patterns;
using LearnSmartManufacturing.ViewModels;

namespace LearnSmartManufacturing.Tests.ViewModels
{
    public class DesignPatternsViewModelTests
    {
        [Fact]
        public void Constructor_InitializesAllThreeTabsWithLiveState()
        {
            var vm = new DesignPatternsViewModel();

            Assert.NotNull(vm.PipelineResult);
            Assert.True(vm.IsOverallPassed);
            Assert.NotEmpty(vm.PipelineTrace);

            Assert.Equal(EquipmentStateKind.Idle, vm.CurrentEquipmentState);
            Assert.Equal(4, vm.ObserverDispatches.Count);
            Assert.NotNull(vm.StrategyReport);

            Assert.Equal(10, vm.FilteredPatterns.Count);
            Assert.NotNull(vm.SelectedPattern);
        }

        [Fact]
        public void Tab1Presets_ExerciseRetryRecoveryCacheFallbackAndChainShortCircuit()
        {
            var vm = new DesignPatternsViewModel();

            vm.ApplyPipelinePresetCommand.Execute("DimensionNg");
            Assert.False(vm.IsOverallPassed);
            Assert.Contains("NG", vm.OverallVerdictBadge, StringComparison.Ordinal);

            vm.ApplyPipelinePresetCommand.Execute("CacheDegrade");
            Assert.True(vm.IsOverallPassed);
            Assert.Contains("缓存降级", vm.PipelineResult!.PlcOutcome.ValueText, StringComparison.Ordinal);

            vm.ApplyPipelinePresetCommand.Execute("TransientRetry");
            Assert.True(vm.IsOverallPassed);
            Assert.Equal(3, vm.PipelineResult!.PlcOutcome.Attempts);
        }

        [Fact]
        public void Tab2BehavioralControls_UpdateFsmObserversStrategyAndUndoRedoCommands()
        {
            var vm = new DesignPatternsViewModel();

            vm.FireEquipmentEventCommand.Execute(EquipmentEventKind.Start);
            Assert.Equal(EquipmentStateKind.Running, vm.CurrentEquipmentState);

            vm.SubMesAlarm = false;
            vm.FireEquipmentEventCommand.Execute(EquipmentEventKind.InjectFault);
            Assert.Equal(EquipmentStateKind.Fault, vm.CurrentEquipmentState);
            Assert.Equal(3, vm.ObserverDispatches.Count);

            // Switch strategy
            vm.SelectedStrategy = vm.StrategyOptions.Single(s => s.Kind == InspectionStrategyKind.StrictAutomotive);
            Assert.Equal(InspectionStrategyKind.StrictAutomotive, vm.StrategyReport!.Kind);

            // Command Undo/Redo
            Assert.False(vm.UndoRecipeCommand.CanExecute(null));
            vm.AdjustTempUpCommand.Execute(null);
            Assert.Equal("195 °C", vm.RecipeTemperatureText);
            Assert.True(vm.UndoRecipeCommand.CanExecute(null));

            vm.UndoRecipeCommand.Execute(null);
            Assert.Equal("185 °C", vm.RecipeTemperatureText);
            Assert.True(vm.RedoRecipeCommand.CanExecute(null));

            vm.RedoRecipeCommand.Execute(null);
            Assert.Equal("195 °C", vm.RecipeTemperatureText);
        }

        [Fact]
        public void Tab3CategoryFilterAndJumpToLab_WorkCorrectly()
        {
            var vm = new DesignPatternsViewModel();
            vm.SelectedTabIndex = 2;

            vm.SelectedCategory = vm.CategoryFilters.Single(c => c.Category == PatternCategory.Structural);
            Assert.Equal(3, vm.FilteredPatterns.Count);

            vm.SelectedPattern = vm.FilteredPatterns.Single(p => p.Id == "decorator");
            Assert.True(vm.HasInteractiveLab);
            vm.JumpToPatternLabCommand.Execute(null);
            Assert.Equal(0, vm.SelectedTabIndex);
        }
    }
}
