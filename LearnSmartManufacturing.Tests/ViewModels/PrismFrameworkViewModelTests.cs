using LearnSmartManufacturing.Models.Prism;
using LearnSmartManufacturing.ViewModels;

namespace LearnSmartManufacturing.Tests.ViewModels
{
    public class PrismFrameworkViewModelTests
    {
        [Fact]
        public void Constructor_InitializesRegionNavigationModulesEventsAndCatalog()
        {
            var vm = new PrismFrameworkViewModel();

            Assert.NotNull(vm.NavigationOutcome);
            Assert.True(vm.IsLastNavSucceeded);
            Assert.True(vm.CanGoBack);
            Assert.Equal(5, vm.ModuleSnapshots.Count);
            Assert.Equal(3, vm.EventDeliveries.Count);
            Assert.True(vm.CanExecuteCompositeSave);
            Assert.Equal(8, vm.Concepts.Count);
        }

        [Fact]
        public void Tab1NavigationPresetsAndJournal_WorkAsExpected()
        {
            var vm = new PrismFrameworkViewModel();

            vm.ApplyNavPresetCommand.Execute("DirtyBlock");
            Assert.False(vm.IsLastNavSucceeded);
            Assert.Contains("Canceled", vm.LastNavStatusText, StringComparison.Ordinal);

            vm.ApplyNavPresetCommand.Execute("EvictKeepAlive");
            Assert.True(vm.IsLastNavSucceeded);
            Assert.True(vm.GoBackCommand.CanExecute(null));
            vm.GoBackCommand.Execute(null);
            Assert.True(vm.GoForwardCommand.CanExecute(null));
        }

        [Fact]
        public void Tab2ModulesCompositeCommandAndDialogGuard_WorkReactively()
        {
            var vm = new PrismFrameworkViewModel();

            // Modularity
            vm.LoadModuleCommand.Execute("MesReportModule");
            Assert.Equal(4, vm.ModuleSnapshots.Count(m => m.Status != PrismModuleStatus.NotLoaded));

            // CompositeCommand CanExecute updates when any child becomes invalid
            vm.VisionModuleCanSave = false;
            Assert.False(vm.ExecuteCompositeSaveCommand.CanExecute(null));
            vm.VisionModuleCanSave = true;
            Assert.True(vm.ExecuteCompositeSaveCommand.CanExecute(null));
            vm.ExecuteCompositeSaveCommand.Execute(null);
            Assert.Equal(3, vm.CompositeResult!.ExecutedModules.Count);

            // IDialogService CanCloseDialog guard
            vm.OpenApproveDialogCommand.Execute(null);
            Assert.True(vm.IsDialogOpen);
            vm.ConfirmApproveDialogCommand.Execute(null); // blocked because password not verified
            Assert.True(vm.IsDialogOpen);
            Assert.False(vm.IsDialogResultOk);

            vm.DialogOperatorVerified = true;
            vm.ConfirmApproveDialogCommand.Execute(null);
            Assert.False(vm.IsDialogOpen);
            Assert.True(vm.IsDialogResultOk);
        }

        [Fact]
        public void Tab3JumpToLab_SwitchesTabIndex()
        {
            var vm = new PrismFrameworkViewModel();
            vm.SelectedTabIndex = 2;
            vm.SelectedConcept = vm.Concepts.Single(c => c.Id == "modularity");

            Assert.True(vm.HasConceptLab);
            vm.JumpToConceptLabCommand.Execute(null);
            Assert.Equal(1, vm.SelectedTabIndex);
        }
    }
}
