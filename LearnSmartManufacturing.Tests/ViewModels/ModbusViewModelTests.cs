using LearnSmartManufacturing.Models.Modbus;
using LearnSmartManufacturing.ViewModels;

namespace LearnSmartManufacturing.Tests.ViewModels
{
    public class ModbusViewModelTests
    {
        [Fact]
        public async Task DefaultConnection_ReadsPlcAndUpdatesEndianRows()
        {
            using var vm = new ModbusViewModel();
            Assert.True(vm.IsConnected);

            await vm.SendRequestCommand.ExecuteAsync(null);
            Assert.NotNull(vm.LatestTransaction);
            Assert.True(vm.LatestTransaction.IsSuccess);
            Assert.Equal(4, vm.EndianRows.Count);
            Assert.NotEmpty(vm.RegisterRows);
        }

        [Fact]
        public async Task SwitchingToRtuAndFaultInjection_RecordsCrcError()
        {
            using var vm = new ModbusViewModel();
            vm.SelectedTransportMode = vm.TransportModes.First(m => m.Value == ModbusTransportMode.SimulatedRtu);
            Assert.True(vm.IsConnected);
            Assert.True(vm.IsSimulatedRtuMode);

            vm.ArmFaultCommand.Execute("CorruptRtuCrc");
            await vm.SendRequestCommand.ExecuteAsync(null);

            Assert.NotNull(vm.LatestTransaction);
            Assert.False(vm.LatestTransaction.IsSuccess);
            Assert.Equal(ModbusFrameFormat.Rtu, vm.LatestTransaction.Format);
            Assert.Contains("CRC-16", vm.LatestTransaction.Summary);
            Assert.True(vm.ErrorCount >= 1);
        }

        [Fact]
        public async Task WriteFloatAndToggleCoil_UpdatePlcDashboard()
        {
            using var vm = new ModbusViewModel();
            vm.EncoderFloatText = "242.5";
            await vm.WriteFloatToTargetTempCommand.ExecuteAsync(null);
            Assert.Equal("242.5 °C", vm.TargetTempText);

            var before = vm.CoilFanEnable;
            await vm.ToggleCoilCommand.ExecuteAsync("2");
            Assert.Equal(!before, vm.CoilFanEnable);
        }
    }
}
