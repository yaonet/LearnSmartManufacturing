using LearnSmartManufacturing.Models.Patterns;

namespace LearnSmartManufacturing.Tests.Models
{
    public class DesignPatternsTests
    {
        [Fact]
        public void Factory_CreatesBrandSpecificDriversBehindCommonInterface()
        {
            Assert.IsType<SiemensS7Driver>(PlcDriverFactory.Create(PlcVendorKind.SiemensS7));
            Assert.IsType<MitsubishiMcDriver>(PlcDriverFactory.Create(PlcVendorKind.MitsubishiMc));
            Assert.IsType<OmronFinsDriver>(PlcDriverFactory.Create(PlcVendorKind.OmronFins));
        }

        [Fact]
        public void DecoratorStack_RetryRecoversTransientFailuresAndCacheFallbackDegradesGracefully()
        {
            // 2 transient failures + Retry(3) => succeeds on attempt 3
            var recovered = StationPipelineRunner.Execute(
                PlcVendorKind.SiemensS7,
                enableLogging: true, enableRetry: true, enableCacheFallback: false,
                transientFailures: 2,
                duplicateBarcode: false, dimensionFault: false, scratchFault: false, mesOffline: false,
                batchSeq: 1);

            Assert.True(recovered.PlcOutcome.IsSuccess);
            Assert.Equal(3, recovered.PlcOutcome.Attempts);
            Assert.True(recovered.OverallPassed);
            Assert.Equal(PipelineNodeState.Recovered, recovered.VisualNodes[1].State);

            // 2 transient failures WITHOUT Retry, WITH CacheFallback => degrades gracefully
            var degraded = StationPipelineRunner.Execute(
                PlcVendorKind.MitsubishiMc,
                enableLogging: true, enableRetry: false, enableCacheFallback: true,
                transientFailures: 2,
                duplicateBarcode: false, dimensionFault: false, scratchFault: false, mesOffline: false,
                batchSeq: 2);

            Assert.True(degraded.PlcOutcome.IsSuccess);
            Assert.Contains("缓存降级", degraded.PlcOutcome.ValueText, StringComparison.Ordinal);
            Assert.True(degraded.OverallPassed);

            // 2 transient failures WITHOUT Retry and WITHOUT CacheFallback => fails & blocks MES
            var failed = StationPipelineRunner.Execute(
                PlcVendorKind.OmronFins,
                enableLogging: false, enableRetry: false, enableCacheFallback: false,
                transientFailures: 2,
                duplicateBarcode: false, dimensionFault: false, scratchFault: false, mesOffline: false,
                batchSeq: 3);

            Assert.False(failed.PlcOutcome.IsSuccess);
            Assert.False(failed.OverallPassed);
        }

        [Fact]
        public void ChainOfResponsibility_ShortCircuitsAtFirstFailingStageAndSkipsDownstream()
        {
            var res = StationPipelineRunner.Execute(
                PlcVendorKind.SiemensS7,
                enableLogging: false, enableRetry: false, enableCacheFallback: false,
                transientFailures: 0,
                duplicateBarcode: false, dimensionFault: true, scratchFault: false, mesOffline: false,
                batchSeq: 10);

            Assert.False(res.OverallPassed);
            Assert.Contains("尺寸公差", res.FinalVerdict, StringComparison.Ordinal);

            // Visual nodes: [0]=Factory, [1]=Decorator, [2]=Barcode(Passed), [3]=Dimension(Blocked), [4]=Cosmetic(Skipped), [5]=MES(Skipped)
            Assert.Equal(6, res.VisualNodes.Count);
            Assert.Equal(PipelineNodeState.Passed, res.VisualNodes[2].State);
            Assert.Equal(PipelineNodeState.Blocked, res.VisualNodes[3].State);
            Assert.Equal(PipelineNodeState.Skipped, res.VisualNodes[4].State);
            Assert.Equal(PipelineNodeState.Skipped, res.VisualNodes[5].State);
        }

        [Fact]
        public void StateMachine_EnforcesSafetyInterlocksAndValidTransitions()
        {
            var fsm = new EquipmentStateMachine();
            Assert.Equal(EquipmentStateKind.Idle, fsm.Current.Kind);

            Assert.True(fsm.Fire(EquipmentEventKind.Start).IsAllowed);
            Assert.Equal(EquipmentStateKind.Running, fsm.Current.Kind);

            // Running rejects direct entry into Maintenance
            var maintRejected = fsm.Fire(EquipmentEventKind.ToggleMaintenance);
            Assert.False(maintRejected.IsAllowed);
            Assert.Equal(EquipmentStateKind.Running, fsm.Current.Kind);

            // Fault locks out Start until ResetAlarm
            Assert.True(fsm.Fire(EquipmentEventKind.InjectFault).IsAllowed);
            Assert.Equal(EquipmentStateKind.Fault, fsm.Current.Kind);
            Assert.False(fsm.Fire(EquipmentEventKind.Start).IsAllowed);
            Assert.Equal(EquipmentStateKind.Fault, fsm.Current.Kind);

            Assert.True(fsm.Fire(EquipmentEventKind.ResetAlarm).IsAllowed);
            Assert.Equal(EquipmentStateKind.Idle, fsm.Current.Kind);
        }

        [Fact]
        public void ObserverPublisher_FansOutToActiveSubscribersAndRespectsUnsubscribe()
        {
            var pub = new EquipmentEventPublisher();
            var o1 = new DelegateEquipmentObserver("UI", tr => new("UI", tr.ToState.ToString(), false));
            var o2 = new DelegateEquipmentObserver("Lamp", tr => new("Lamp", "Red", true));

            pub.Subscribe(o1);
            pub.Subscribe(o2);
            var tr = new StateTransitionResult(EquipmentStateKind.Running, EquipmentStateKind.Fault, EquipmentEventKind.InjectFault, true, "EStop");

            Assert.Equal(2, pub.Publish(tr).Count);

            pub.Unsubscribe(o2);
            var remaining = pub.Publish(tr);
            Assert.Single(remaining);
            Assert.Equal("UI", remaining[0].SubscriberName);
        }

        [Fact]
        public void InspectionStrategies_TradeOffYieldEscapeRiskAndCycleTimeOnSameBatch()
        {
            var batch = InspectionStrategyFactory.GenerateDeterministicBatch(200);
            var strict = InspectionStrategyFactory.Create(InspectionStrategyKind.StrictAutomotive).EvaluateBatch(batch);
            var balanced = InspectionStrategyFactory.Create(InspectionStrategyKind.BalancedThreeSigma).EvaluateBatch(batch);
            var fast = InspectionStrategyFactory.Create(InspectionStrategyKind.HighSpeedSampling).EvaluateBatch(batch);

            Assert.True(strict.YieldPercent < balanced.YieldPercent);
            Assert.True(balanced.YieldPercent < fast.YieldPercent);
            Assert.True(strict.EscapeRiskPercent < balanced.EscapeRiskPercent);
            Assert.True(fast.AvgCycleMs < balanced.AvgCycleMs);
        }

        [Fact]
        public void CommandManager_SupportsMultiStepUndoRedoAndClearsRedoOnNewCommand()
        {
            var mgr = new RecipeCommandManager();
            var initialTemp = mgr.Target.TemperatureC;
            var initialSpeed = mgr.Target.ConveyorSpeedMmS;

            mgr.Execute(new AdjustTemperatureCommand(10));
            mgr.Execute(new AdjustSpeedCommand(50));
            Assert.Equal(initialTemp + 10, mgr.Target.TemperatureC);
            Assert.Equal(initialSpeed + 50, mgr.Target.ConveyorSpeedMmS);
            Assert.Equal(2, mgr.UndoCount);
            Assert.Equal(0, mgr.RedoCount);

            mgr.Undo();
            Assert.Equal(initialSpeed, mgr.Target.ConveyorSpeedMmS);
            Assert.Equal(1, mgr.RedoCount);

            mgr.Redo();
            Assert.Equal(initialSpeed + 50, mgr.Target.ConveyorSpeedMmS);
            Assert.Equal(0, mgr.RedoCount);

            // Undo then execute new command clears Redo stack
            mgr.Undo();
            Assert.True(mgr.CanRedo);
            mgr.Execute(new ToggleRecheckCommand());
            Assert.False(mgr.CanRedo);
        }

        [Fact]
        public void DesignPatternCatalog_ContainsTenPatternsAcrossAllThreeGofCategories()
        {
            Assert.Equal(10, DesignPatternCatalog.All.Count);
            Assert.Equal(2, DesignPatternCatalog.All.Count(p => p.Category == PatternCategory.Creational));
            Assert.Equal(3, DesignPatternCatalog.All.Count(p => p.Category == PatternCategory.Structural));
            Assert.Equal(5, DesignPatternCatalog.All.Count(p => p.Category == PatternCategory.Behavioral));
            Assert.All(DesignPatternCatalog.All, p =>
            {
                Assert.False(string.IsNullOrWhiteSpace(p.CodeSkeleton));
                Assert.False(string.IsNullOrWhiteSpace(p.InterviewPitfall));
            });
        }
    }
}
