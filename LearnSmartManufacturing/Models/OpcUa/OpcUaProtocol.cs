using System.Buffers.Binary;
using System.Text;

namespace LearnSmartManufacturing.Models.OpcUa
{
    public enum OpcUaSecurityMode
    {
        None,
        Sign,
        SignAndEncrypt,
    }

    public enum OpcUaNodeClass
    {
        Object,
        Variable,
        Method,
    }

    public enum OpcUaConnectionState
    {
        Disconnected,
        Connected,
        CertificateRejected,
    }

    /// <summary>Standard OPC UA 32-bit StatusCodes (IEC 62541-4).</summary>
    public static class OpcUaStatusCodes
    {
        public const uint Good = 0x0000_0000;
        public const uint UncertainLastUsableValue = 0x4090_0000;
        public const uint BadCertificateUntrusted = 0x801A_0000;
        public const uint BadNodeIdUnknown = 0x8034_0000;
        public const uint BadNotWritable = 0x803B_0000;
        public const uint BadTypeMismatch = 0x8074_0000;
        public const uint BadSensorFailure = 0x808F_0000;
        public const uint BadSessionClosed = 0x8026_0000;

        public static bool IsGood(uint code) => (code & 0xC000_0000) == 0x0000_0000;
        public static bool IsUncertain(uint code) => (code & 0xC000_0000) == 0x4000_0000;
        public static bool IsBad(uint code) => (code & 0xC000_0000) == 0x8000_0000;

        public static string Format(uint code) => code switch
        {
            Good => "Good (0x00000000)",
            UncertainLastUsableValue => "UncertainLastUsableValue (0x40900000)",
            BadCertificateUntrusted => "BadCertificateUntrusted (0x801A0000)",
            BadNodeIdUnknown => "BadNodeIdUnknown (0x80340000)",
            BadNotWritable => "BadNotWritable (0x803B0000)",
            BadTypeMismatch => "BadTypeMismatch (0x80740000)",
            BadSensorFailure => "BadSensorFailure (0x808F0000)",
            BadSessionClosed => "BadSessionClosed (0x80260000)",
            _ => $"0x{code:X8}",
        };
    }

    public sealed record OpcUaNodeSnapshot(
        string NodeId,
        string BrowseName,
        string DisplayName,
        string ParentNodeId,
        OpcUaNodeClass NodeClass,
        string DataType,
        bool IsWritable,
        string EngineeringUnit,
        string ValueText,
        uint StatusCode,
        DateTime SourceTimestamp,
        DateTime ServerTimestamp,
        string Description)
    {
        public string AccessText => NodeClass switch
        {
            OpcUaNodeClass.Object => "Folder / Object",
            OpcUaNodeClass.Method => "Executable (RPC)",
            _ => IsWritable ? "CurrentRead | CurrentWrite" : "CurrentRead (RO)",
        };

        public string StatusText => OpcUaStatusCodes.Format(StatusCode);
        public bool IsGoodStatus => OpcUaStatusCodes.IsGood(StatusCode);
        public string SourceTimeText => SourceTimestamp.ToString("HH:mm:ss.fff");
        public string ServerTimeText => ServerTimestamp.ToString("HH:mm:ss.fff");
        public string ClassTag => NodeClass switch
        {
            OpcUaNodeClass.Object => "OBJ",
            OpcUaNodeClass.Method => "MTH",
            _ => "VAR",
        };
    }

    public sealed record OpcUaHandshakeStep(
        string PacketTag,
        bool FromClientToServer,
        string Detail,
        bool IsError = false,
        bool IsEncrypted = false);

    public sealed record OpcUaHandshakeTrace(
        DateTime Timestamp,
        string Title,
        OpcUaSecurityMode SecurityMode,
        byte[] RawUacpBytes,
        IReadOnlyList<OpcUaHandshakeStep> Steps);

    public sealed record OpcUaOperationResult(
        bool Success,
        uint StatusCode,
        string Summary,
        string OutputValue = "")
    {
        public string StatusText => OpcUaStatusCodes.Format(StatusCode);
    }

    public sealed record OpcUaMonitoredSample(
        int SampleIndex,
        DateTime Timestamp,
        double RawPlcValue,
        double LastReportedValue,
        double Deadband,
        bool PassedDeadband,
        bool PublishedToClient,
        bool IsKeepAlive,
        uint StatusCode);

    public sealed record OpcUaNotificationRecord(
        long SequenceNumber,
        DateTime Timestamp,
        string Kind, // DataChange | KeepAlive
        string NodeId,
        string ValueText,
        uint StatusCode,
        string Detail)
    {
        public string TimeText => Timestamp.ToString("HH:mm:ss.fff");
        public string StatusText => OpcUaStatusCodes.Format(StatusCode);
        public bool IsGood => OpcUaStatusCodes.IsGood(StatusCode);
    }

    public sealed record OpcUaLogEntry(DateTime Timestamp, string Level, string Service, string Message)
    {
        public string TimeText => Timestamp.ToString("HH:mm:ss.fff");
    }

    /// <summary>
    /// Wire-format OPC UA Binary TCP (UACP) header encoder per IEC 62541-6 (HEL, ACK, OPN, MSG, CLO).
    /// </summary>
    public static class OpcUaBinaryCodec
    {
        /// <summary>Builds a wire-format UACP Hello ('HEL' + 'F') message containing endpointUrl.</summary>
        public static byte[] BuildHelloPacket(string endpointUrl, uint receiveBufferSize = 65535, uint sendBufferSize = 65535)
        {
            var urlBytes = Encoding.UTF8.GetBytes(endpointUrl);
            // 8B UACP Header + 20B protocol limits + 4B string length + urlBytes
            var totalLen = 8 + 20 + 4 + urlBytes.Length;
            var buf = new byte[totalLen];

            buf[0] = (byte)'H';
            buf[1] = (byte)'E';
            buf[2] = (byte)'L';
            buf[3] = (byte)'F'; // Final chunk
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4, 4), (uint)totalLen);

            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8, 4), 0); // ProtocolVersion = 0
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(12, 4), receiveBufferSize);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(16, 4), sendBufferSize);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(20, 4), 4_194_304); // MaxMessageSize
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(24, 4), 0);         // MaxChunkCount
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(28, 4), urlBytes.Length);
            urlBytes.CopyTo(buf, 32);
            return buf;
        }

        /// <summary>Builds a UACP OpenSecureChannel ('OPN' + 'F') message header with SecurityPolicyUri.</summary>
        public static byte[] BuildOpenSecureChannelPacket(uint secureChannelId, string securityPolicyUri)
        {
            var policyBytes = Encoding.UTF8.GetBytes(securityPolicyUri);
            var totalLen = 8 + 4 + 4 + policyBytes.Length + 8; // UACP(8) + SecChannelId(4) + PolicyLen(4) + Policy + Seq/ReqId(8)
            var buf = new byte[totalLen];
            buf[0] = (byte)'O';
            buf[1] = (byte)'P';
            buf[2] = (byte)'N';
            buf[3] = (byte)'F';
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4, 4), (uint)totalLen);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8, 4), secureChannelId);
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(12, 4), policyBytes.Length);
            policyBytes.CopyTo(buf, 16);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(16 + policyBytes.Length, 4), 1); // SequenceNumber
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(20 + policyBytes.Length, 4), 1); // RequestId
            return buf;
        }

        public static bool TryParseUacpHeader(ReadOnlySpan<byte> packet, out string messageType, out char chunkType, out uint messageSize)
        {
            messageType = string.Empty;
            chunkType = '\0';
            messageSize = 0;

            if (packet.Length < 8)
            {
                return false;
            }

            messageType = Encoding.ASCII.GetString(packet[..3]);
            chunkType = (char)packet[3];
            messageSize = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(4, 4));
            return messageSize == (uint)packet.Length;
        }

        public static string ToHex(ReadOnlySpan<byte> bytes, int maxBytes = 20)
        {
            if (bytes.Length == 0) return string.Empty;
            var count = Math.Min(bytes.Length, maxBytes);
            var hex = string.Join(' ', bytes[..count].ToArray().Select(b => b.ToString("X2")));
            return bytes.Length > maxBytes ? $"{hex} … ({bytes.Length}B)" : hex;
        }
    }
}
