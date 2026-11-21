using LearnSmartManufacturing.Models.EtherCat;
using LearnSmartManufacturing.Services.EtherCat;
using Xunit;

namespace LearnSmartManufacturing.Tests.Services
{
    public class SimulatedEtherCatMasterTests
    {
        [Fact]
        public void EsmStates_ControlWkcAndOutputSafetyCorrectly()
        {
            var master = new SimulatedEtherCatMaster { CspSineTrajectoryEnabled = false };

            // INIT -> WKC 0
            var (fInit, _) = master.ExecuteCycle();
            Assert.Equal((ushort)0, fInit.ExpectedWkc);
            Assert.Equal((ushort)0, fInit.ActualWkc);

            // Reject direct jump INIT -> OP
            Assert.False(master.RequestState(EsmState.Op, out var al, out _));
            Assert.Equal((ushort)0x0011, al);

            // PRE-OP -> WKC 0, Mailbox allowed
            Assert.True(master.RequestState(EsmState.PreOp, out _, out _));
            Assert.True(master.IsMailboxAllowed);
            Assert.False(master.IsInputsActive);

            // SAFE-OP -> WKC 3 (TxPDO read-only), RxPDO outputs held safe
            Assert.True(master.RequestState(EsmState.SafeOp, out _, out _));
            master.SetServoTargetPosition(1, 5000);
            var (fSafe, _) = master.ExecuteCycle();
            Assert.Equal((ushort)3, fSafe.ExpectedWkc);
            Assert.Equal((ushort)3, fSafe.ActualWkc);
            Assert.Equal(0, master.GetSlaveSnapshots()[1].ActualPosition);

            // OP -> WKC 9, Enable servos -> servo tracks target
            Assert.True(master.RequestState(EsmState.Op, out _, out _));
            Assert.True(master.EnableServos(out _));
            var (fOp, _) = master.ExecuteCycle();
            Assert.Equal((ushort)9, fOp.ExpectedWkc);
            Assert.Equal((ushort)9, fOp.ActualWkc);
            Assert.True(master.GetSlaveSnapshots()[1].ActualPosition > 0);
        }

        [Fact]
        public void CableBreakAtSlave2_DropsDownstreamSlave3AndCausesWkcMismatch()
        {
            var master = new SimulatedEtherCatMaster();
            master.BootToOp(out _);

            master.InjectFault(EtherCatFaultKind.CableBreakAtSlave2);
            var (frame, _) = master.ExecuteCycle();
            var slaves = master.GetSlaveSnapshots();

            Assert.True(slaves[0].IsOnline);
            Assert.True(slaves[1].IsOnline);
            Assert.False(slaves[2].IsOnline);
            Assert.False(slaves[3].IsOnline);
            Assert.Equal((ushort)9, frame.ExpectedWkc);
            Assert.Equal((ushort)3, frame.ActualWkc);
            Assert.False(frame.IsWkcValid);
            Assert.True(master.WkcErrorCount >= 1);
        }

        [Fact]
        public void SmWatchdogTimeout_DropsToSafeOpWithAl001BAndServoFault()
        {
            var master = new SimulatedEtherCatMaster();
            master.BootToOp(out _);
            master.EnableServos(out _);

            master.InjectFault(EtherCatFaultKind.SmWatchdogTimeout);
            var slaves = master.GetSlaveSnapshots();

            Assert.Equal(EsmState.SafeOp, master.MasterState);
            Assert.Equal((ushort)0x001B, slaves[1].AlStatusCode);
            Assert.Equal(Cia402State.Fault, slaves[1].Cia402State);
            Assert.True(slaves[1].HasError);
        }

        [Fact]
        public void DistributedClocks_ContrastsNanosecondLockVsMicrosecondDrift()
        {
            var master = new SimulatedEtherCatMaster { DcEnabled = true, SimulateOsJitter = true };
            master.BootToOp(out _);

            double maxDcOn = 0;
            for (var i = 0; i < 20; i++)
            {
                var (_, sample) = master.ExecuteCycle();
                maxDcOn = Math.Max(maxDcOn, sample.MaxAbsOffsetNs);
            }

            Assert.True(maxDcOn < 200, $"Expected DC-locked jitter < 200 ns, got {maxDcOn:F1} ns");

            master.DcEnabled = false;
            double maxDcOff = 0;
            for (var i = 0; i < 20; i++)
            {
                var (_, sample) = master.ExecuteCycle();
                maxDcOff = Math.Max(maxDcOff, sample.MaxAbsOffsetNs);
            }

            Assert.True(maxDcOff > 2000, $"Expected DC-disabled drift > 2000 ns, got {maxDcOff:F1} ns");
        }

        [Fact]
        public void SdoMailbox_EnforcesStateAndAccessAbortCodes()
        {
            var master = new SimulatedEtherCatMaster();

            // INIT blocks SDO mailbox (0x08000022)
            var initRead = master.ReadSdo(1, 0x1000, 0x00);
            Assert.False(initRead.Success);
            Assert.Equal(0x08000022u, initRead.AbortCode);

            master.RequestState(EsmState.PreOp, out _, out _);

            // Valid read in PRE-OP
            var okRead = master.ReadSdo(1, 0x6065, 0x00);
            Assert.True(okRead.Success);
            Assert.Equal("5000", okRead.ValueText);

            // Read-only write rejected (0x06010002)
            var roWrite = master.WriteSdo(1, 0x1000, 0x00, "123");
            Assert.False(roWrite.Success);
            Assert.Equal(0x06010002u, roWrite.AbortCode);

            // Nonexistent index rejected (0x06020000)
            var badIdx = master.ReadSdo(1, 0x9999, 0x00);
            Assert.False(badIdx.Success);
            Assert.Equal(0x06020000u, badIdx.AbortCode);

            // Out-of-range rejected (0x06090030)
            var badRange = master.WriteSdo(1, 0x6060, 0x00, "99");
            Assert.False(badRange.Success);
            Assert.Equal(0x06090030u, badRange.AbortCode);

            // Valid RW write updates dictionary
            var okWrite = master.WriteSdo(1, 0x6065, 0x00, "12000");
            Assert.True(okWrite.Success);
            Assert.Equal("12000", master.ReadSdo(1, 0x6065, 0x00).ValueText);
        }
    }
}
