using LearnSmartManufacturing.Models.OpcUa;
using LearnSmartManufacturing.ViewModels;
using Xunit;

namespace LearnSmartManufacturing.Tests.ViewModels
{
    public class OpcUaViewModelTests
    {
        [Fact]
        public void Constructor_ConnectsBuildsAddressSpaceAndPopulatesInitialSamples()
        {
            using var vm = new OpcUaViewModel();

            Assert.True(vm.IsConnected);
            Assert.Single(vm.RootNodes);
            Assert.NotNull(vm.SelectedNode);
            Assert.Equal(24, vm.MonitoredSamples.Count);
            Assert.NotNull(vm.LatestTrace);
        }

        [Fact]
        public void UntrustedCertificateDemoAndTrustRecovery_UpdateConnectionState()
        {
            using var vm = new OpcUaViewModel();

            vm.DemoUntrustedCertificateCommand.Execute(null);
            Assert.True(vm.IsCertRejected);
            Assert.False(vm.IsConnected);

            vm.TrustCertificateAndReconnectCommand.Execute(null);
            Assert.True(vm.IsConnected);
            Assert.False(vm.IsCertRejected);
        }

        [Fact]
        public void SensorFaultAndTempStep_PushBadStatusAndDataChangeNotifications()
        {
            using var vm = new OpcUaViewModel();

            vm.ToggleSensorFaultCommand.Execute(null);
            Assert.True(vm.IsSensorFaultInjected);
            Assert.Contains(vm.Notifications, n => n.StatusCode == OpcUaStatusCodes.BadSensorFailure);

            vm.ToggleSensorFaultCommand.Execute(null);
            Assert.False(vm.IsSensorFaultInjected);

            var beforePub = vm.PublishedNotificationsCount;
            vm.InjectTempStepCommand.Execute(null);
            Assert.True(vm.PublishedNotificationsCount > beforePub);
        }
    }
}
