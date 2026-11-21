using LearnSmartManufacturing.Models.EtherCat;
using LearnSmartManufacturing.ViewModels;
using Xunit;

namespace LearnSmartManufacturing.Tests.ViewModels
{
    public class EtherCatViewModelTests
    {
        [Fact]
        public void Constructor_BootsToOpWithHealthyWkcAndInitialDcSamples()
        {
            using var vm = new EtherCatViewModel();

            Assert.Equal(EsmState.Op, vm.MasterState);
            Assert.Equal(4, vm.SlaveSnapshots.Count);
            Assert.All(vm.SlaveSnapshots, s => Assert.True(s.IsOnline));
            Assert.True(vm.IsWkcValid);
            Assert.Equal("9 / 9", vm.WkcSummaryText);
            Assert.Equal(24, vm.DcJitterSamples.Count);
            Assert.True(vm.IsDcWithinSpec);
            Assert.NotEmpty(vm.SdoEntries);
        }

        [Fact]
        public void IllegalJumpFromInitToOp_IsRejectedAndKeepsInitState()
        {
            using var vm = new EtherCatViewModel();

            vm.RequestEsmStateCommand.Execute("Init");
            Assert.Equal(EsmState.Init, vm.MasterState);
            Assert.False(vm.IsMailboxAllowed);

            vm.RequestEsmStateCommand.Execute("Op");
            Assert.Equal(EsmState.Init, vm.MasterState);
            Assert.Contains("0x0011", vm.StatusBanner);
        }

        [Fact]
        public void FaultInjectionAndRecovery_UpdatesWkcAndRestoresBus()
        {
            using var vm = new EtherCatViewModel();

            vm.InjectFaultCommand.Execute("CableBreakAtSlave2");
            Assert.True(vm.HasActiveFault);
            Assert.False(vm.IsWkcValid);
            Assert.Equal("3 / 9", vm.WkcSummaryText);

            vm.BootToOpCommand.Execute(null);
            Assert.False(vm.HasActiveFault);
            Assert.True(vm.IsWkcValid);
            Assert.Equal("9 / 9", vm.WkcSummaryText);
        }

        [Fact]
        public void DisablingDcSync_ExpandsOffsetBeyondSpec()
        {
            using var vm = new EtherCatViewModel();

            vm.DcEnabled = false;
            Assert.False(vm.IsDcWithinSpec);
            Assert.Contains("µs", vm.MaxDcOffsetText);
        }

        [Fact]
        public void SdoWriteAndAbortDemo_UpdatesEntriesAndResultBanner()
        {
            using var vm = new EtherCatViewModel();

            vm.SdoIndexText = "0x6065";
            vm.SdoSubIndexText = "0x00";
            vm.SdoWriteValueText = "8500";
            vm.WriteSdoCommand.Execute(null);

            Assert.True(vm.IsLastSdoSuccess);
            Assert.Equal("8500", vm.SdoEntries.Single(e => e.Index == 0x6065).ValueText);

            vm.DemoSdoAbortCommand.Execute("ReadOnly");
            Assert.False(vm.IsLastSdoSuccess);
            Assert.Contains("0x06010002", vm.LastSdoBanner);
        }
    }
}
