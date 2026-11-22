using LearnSmartManufacturing.Models.Plc;
using LearnSmartManufacturing.ViewModels;

namespace LearnSmartManufacturing.Tests.ViewModels
{
    public class PlcProtocolViewModelTests
    {
        [Fact]
        public void Constructor_InitializesSiemensS7ConnectedWithDissectedFrameAndDiagnostics()
        {
            var vm = new PlcProtocolViewModel();

            Assert.True(vm.IsConnected);
            Assert.Equal(PlcBrandKind.SiemensS7, vm.SelectedBrand.Brand);
            Assert.Equal("TCP 102", vm.ActivePortText);
            Assert.NotNull(vm.LatestTransaction);
            Assert.Equal("READ", vm.LatestTransaction!.Operation);
            Assert.True(vm.Transactions.Count >= 2);
            Assert.NotNull(vm.BatchPlan);
            Assert.Contains("43 3A 80 00", vm.EndianComparisonText, StringComparison.Ordinal);
            Assert.Contains("00 80 3A 43", vm.EndianComparisonText, StringComparison.Ordinal);
            Assert.Contains("80 00 43 3A", vm.EndianComparisonText, StringComparison.Ordinal);
            Assert.Contains("PCB-A09", vm.S7StringAnatomyText, StringComparison.Ordinal);
        }

        [Fact]
        public void SwitchingBrands_UpdatesMemoryTagsChipsAndExecutesBrandHandshake()
        {
            var vm = new PlcProtocolViewModel();

            vm.SelectedBrand = vm.BrandOptions.Single(b => b.Brand == PlcBrandKind.OmronFinsTcp);
            Assert.Equal("TCP 9600", vm.ActivePortText);
            Assert.Equal("CONNECT", vm.LatestTransaction!.Operation);
            Assert.Equal(PlcBrandKind.OmronFinsTcp, vm.LatestTransaction.Brand);
            Assert.Contains(vm.MemoryTags, t => t.Address == "W0.00");

            vm.SelectedBrand = vm.BrandOptions.Single(b => b.Brand == PlcBrandKind.MitsubishiMc3E);
            Assert.Equal("TCP 5000", vm.ActivePortText);
            Assert.Contains(vm.MemoryTags, t => t.Address == "X1A");
        }

        [Fact]
        public void WriteReadAndFaultCommands_UpdateMemoryDiagnosticsAndErrorCounters()
        {
            var vm = new PlcProtocolViewModel();

            var s7StrChip = vm.QuickAddressChips.Single(c => c.Type == PlcDataType.S7String);
            vm.ApplyAddressChipCommand.Execute(s7StrChip);
            Assert.Equal("DB1.DBB8", vm.AddressText);

            vm.WriteValueText = "BATCH-42";
            vm.WriteCommand.Execute(null);
            Assert.True(vm.IsLastOpSuccess);
            Assert.Contains("BATCH-42", vm.S7StringAnatomyText, StringComparison.Ordinal);
            Assert.Equal("BATCH-42", vm.MemoryTags.Single(t => t.Address == "DB1.DBB8").ValueText);

            var errorsBefore = vm.ErrorCount;
            vm.DemoFaultCommand.Execute(null);
            Assert.Equal(errorsBefore + 1, vm.ErrorCount);
            Assert.False(vm.IsLastOpSuccess);
            Assert.False(vm.LatestTransaction!.IsSuccess);
        }

        [Fact]
        public void Tab2BatchSlidersAndComparisonSwitch_WorkReactively()
        {
            var vm = new PlcProtocolViewModel();
            vm.SelectedTabIndex = 1;

            vm.BatchMaxGapBytes = 0;
            var tripsZeroGap = vm.BatchPlan!.BatchedRoundTrips;

            vm.BatchMaxGapBytes = 24;
            var tripsMergedGap = vm.BatchPlan!.BatchedRoundTrips;
            Assert.True(tripsMergedGap < tripsZeroGap);

            vm.SwitchBrandFromComparisonCommand.Execute(PlcBrandKind.OmronFinsTcp);
            Assert.Equal(PlcBrandKind.OmronFinsTcp, vm.SelectedBrand.Brand);
            Assert.Equal(0, vm.SelectedTabIndex);
        }
    }
}
