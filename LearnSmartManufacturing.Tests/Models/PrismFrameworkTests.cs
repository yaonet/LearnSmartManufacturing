using LearnSmartManufacturing.Models.Prism;

namespace LearnSmartManufacturing.Tests.Models
{
    public class PrismFrameworkTests
    {
        [Fact]
        public void RegionManager_BlocksNavigationWhenDirtyAndConfirmDeclines()
        {
            var rm = new SimulatedPrismRegionManager();
            var blocked = rm.RequestNavigate(
                "RecipeEditorView",
                new PrismNavigationParameters("ST-02", "RCP-99"),
                isCurrentDirty: true,
                confirmAllowsLeave: false,
                reuseExistingTarget: true,
                currentKeepAlive: true);

            Assert.False(blocked.Succeeded);
            Assert.Equal("StationOverviewView", rm.ActiveViewKey);
            Assert.Contains(blocked.Steps, s => s.HookName == "ConfirmNavigationRequest" && s.Status == "Blocked");
        }

        [Fact]
        public void RegionManager_HonorsIsNavigationTargetKeepAliveAndJournalBackForward()
        {
            var rm = new SimulatedPrismRegionManager();

            // Navigate ViewA -> ViewB with ViewA KeepAlive=true
            var toB = rm.RequestNavigate("RecipeEditorView", new("ST-02", "RCP-2"), false, true, true, currentKeepAlive: true);
            Assert.True(toB.Succeeded);
            Assert.Equal(2, rm.RegionViewCount);
            Assert.True(rm.CanGoBack);

            // Navigate ViewB -> ViewC with ViewB KeepAlive=false -> ViewB evicted!
            var toC = rm.RequestNavigate("AlarmDiagnosticsView", new("ST-03", "RCP-3"), false, true, true, currentKeepAlive: false);
            Assert.True(toC.Succeeded);
            Assert.DoesNotContain(toC.RegionViews, v => v.ViewKey == "RecipeEditorView");

            // Journal GoBack returns to ViewB (fresh instance because ViewB was evicted!)
            var back = rm.GoBack(false, true, reuseExistingTarget: true, currentKeepAlive: true);
            Assert.True(back.Succeeded);
            Assert.Equal("RecipeEditorView", back.ActiveViewKey);
            Assert.True(back.ActiveInstanceId > toB.ActiveInstanceId);
            Assert.True(rm.CanGoForward);

            // Journal GoForward reuses ViewC (KeepAlive=true + reuseExistingTarget=true)
            var fwd = rm.GoForward(false, true, reuseExistingTarget: true, currentKeepAlive: true);
            Assert.True(fwd.Succeeded);
            Assert.Equal(toC.ActiveInstanceId, fwd.ActiveInstanceId);
        }

        [Fact]
        public void ModuleCatalog_AutoResolvesTransitiveDependenciesOnDemand()
        {
            var catalog = new SimulatedModuleCatalog();

            // Initially only CoreAuthModule (WhenAvailable) is loaded
            Assert.Single(catalog.GetSnapshots(), m => m.Status == PrismModuleStatus.Initialized);

            // Loading MesReportModule must auto-resolve PlcCommModule & VisionModule first
            var logs = catalog.LoadModule("MesReportModule");
            Assert.Equal(3, logs.Count);
            Assert.EndsWith("OnInitialized(IContainerProvider)", logs[^1], StringComparison.Ordinal);

            var snaps = catalog.GetSnapshots().ToDictionary(m => m.ModuleName);
            Assert.Equal(PrismModuleStatus.AutoResolvedDependency, snaps["PlcCommModule"].Status);
            Assert.Equal(PrismModuleStatus.AutoResolvedDependency, snaps["VisionModule"].Status);
            Assert.Equal(PrismModuleStatus.Initialized, snaps["MesReportModule"].Status);
            Assert.Equal(PrismModuleStatus.NotLoaded, snaps["MotionModule"].Status);
        }

        [Fact]
        public void EventAggregator_AppliesFilterThreadOptionAndFlagsStrongReferenceRisk()
        {
            var filtered = SimulatedEventAggregator.PublishAlarm(
                PrismAlarmSeverity.Info,
                PrismThreadOption.UIThread,
                filterCriticalOnly: true,
                keepStrongReference: true);

            Assert.False(filtered[0].Delivered);
            Assert.True(filtered[0].IsMemoryLeakRisk);
            Assert.False(filtered[2].Delivered);

            var critical = SimulatedEventAggregator.PublishAlarm(
                PrismAlarmSeverity.Critical,
                PrismThreadOption.UIThread,
                filterCriticalOnly: true,
                keepStrongReference: false);

            Assert.All(critical, d => Assert.True(d.Delivered));
            Assert.False(critical[0].IsMemoryLeakRisk);
        }

        [Fact]
        public void CompositeCommand_DisablesAggregateExecutionWhenAnyChildCannotExecute()
        {
            var blocked = SimulatedCompositeCommand.Evaluate(plcValid: true, visionValid: false, motionValid: true, executeNow: true);
            Assert.False(blocked.CanExecuteAll);
            Assert.Empty(blocked.ExecutedModules);

            var ok = SimulatedCompositeCommand.Evaluate(plcValid: true, visionValid: true, motionValid: true, executeNow: true);
            Assert.True(ok.CanExecuteAll);
            Assert.Equal(3, ok.ExecutedModules.Count);
        }

        [Fact]
        public void ReferenceCatalog_ContainsAllCoreConceptsAndComparisonRows()
        {
            Assert.Equal(8, PrismReferenceCatalog.Concepts.Count);
            Assert.Equal(6, PrismReferenceCatalog.ComparisonRows.Count);
            Assert.All(PrismReferenceCatalog.Concepts, c =>
            {
                Assert.False(string.IsNullOrWhiteSpace(c.CodeSkeleton));
                Assert.False(string.IsNullOrWhiteSpace(c.InterviewPitfall));
            });
        }
    }
}
