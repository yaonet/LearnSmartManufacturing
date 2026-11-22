using LearnSmartManufacturing.Models.OpcUa;
using LearnSmartManufacturing.Services.OpcUa;
using Xunit;

namespace LearnSmartManufacturing.Tests.Services
{
    public class SimulatedOpcUaServerTests
    {
        [Fact]
        public void Connect_RejectsUntrustedCertificateUnderSignAndEncrypt()
        {
            var server = new SimulatedOpcUaServer { IsClientCertificateTrusted = false };

            var rejected = server.Connect(SimulatedOpcUaServer.DefaultEndpointUrl, OpcUaSecurityMode.SignAndEncrypt);
            Assert.False(rejected.Success);
            Assert.Equal(OpcUaStatusCodes.BadCertificateUntrusted, rejected.StatusCode);
            Assert.Equal(OpcUaConnectionState.CertificateRejected, server.State);

            server.IsClientCertificateTrusted = true;
            var accepted = server.Connect(SimulatedOpcUaServer.DefaultEndpointUrl, OpcUaSecurityMode.SignAndEncrypt);
            Assert.True(accepted.Success);
            Assert.Equal(OpcUaConnectionState.Connected, server.State);
        }

        [Fact]
        public void WriteNode_EnforcesAccessLevelAndDataTypeChecks()
        {
            var server = new SimulatedOpcUaServer();
            server.Connect(SimulatedOpcUaServer.DefaultEndpointUrl, OpcUaSecurityMode.None);

            // RO variable rejects write
            var ro = server.WriteNode(SimulatedOpcUaServer.TempNodeId, "200.0");
            Assert.False(ro.Success);
            Assert.Equal(OpcUaStatusCodes.BadNotWritable, ro.StatusCode);

            // Type mismatch rejected
            var badType = server.WriteNode(SimulatedOpcUaServer.TargetTempNodeId, "not-a-double");
            Assert.False(badType.Success);
            Assert.Equal(OpcUaStatusCodes.BadTypeMismatch, badType.StatusCode);

            // Valid RW write succeeds
            var ok = server.WriteNode(SimulatedOpcUaServer.TargetTempNodeId, "210.5");
            Assert.True(ok.Success);
            Assert.Equal("210.5", server.ReadNode(SimulatedOpcUaServer.TargetTempNodeId).OutputValue);
        }

        [Fact]
        public void MethodCallAndSensorFault_UpdateAddressSpaceAndResetCleanly()
        {
            var server = new SimulatedOpcUaServer();
            server.Connect(SimulatedOpcUaServer.DefaultEndpointUrl, OpcUaSecurityMode.None);

            server.SetSensorFault(true);
            var badRead = server.ReadNode(SimulatedOpcUaServer.TempNodeId);
            Assert.False(badRead.Success);
            Assert.Equal(OpcUaStatusCodes.BadSensorFailure, badRead.StatusCode);

            var reset = server.CallMethod(SimulatedOpcUaServer.ResetAlarmMethodId);
            Assert.True(reset.Success);
            Assert.True(server.ReadNode(SimulatedOpcUaServer.TempNodeId).Success);

            var inspect = server.CallMethod(SimulatedOpcUaServer.TriggerInspectMethodId);
            Assert.True(inspect.Success);
            Assert.Contains("Total=1201", inspect.OutputValue);
        }

        [Fact]
        public void Subscription_DeadbandFiltersSmallJitterAndStatusChangeBypassesDeadband()
        {
            var server = new SimulatedOpcUaServer
            {
                SamplingIntervalMs = 100,
                PublishingIntervalMs = 500,
                DeadbandAbsolute = 1.5,
                MaxKeepAliveCount = 2,
            };
            var notifications = new List<OpcUaNotificationRecord>();
            server.NotificationPublished += notifications.Add;
            server.Connect(SimulatedOpcUaServer.DefaultEndpointUrl, OpcUaSecurityMode.None);

            for (var i = 0; i < 20; i++)
            {
                server.StepSubscriptionSample();
            }

            Assert.True(server.FilteredByDeadbandCount > 0);
            Assert.True(server.PublishedNotificationsCount < server.TotalSamplesCount);

            // Sensor fault must bypass deadband and immediately publish BadSensorFailure on next publish boundary!
            server.SetSensorFault(true);
            for (var i = 0; i < 5; i++)
            {
                server.StepSubscriptionSample();
            }

            Assert.Contains(notifications, n => n.StatusCode == OpcUaStatusCodes.BadSensorFailure);
        }
    }
}
