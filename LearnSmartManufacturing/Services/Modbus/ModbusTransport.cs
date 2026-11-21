using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using LearnSmartManufacturing.Models.Modbus;

namespace LearnSmartManufacturing.Services.Modbus
{
    /// <summary>Transport abstraction for Modbus ADU exchange (loopback simulator or real TCP socket).</summary>
    public interface IModbusTransport : IDisposable
    {
        ModbusFrameFormat FrameFormat { get; }
        bool IsConnected { get; }

        Task ConnectAsync(CancellationToken cancellationToken = default);

        void Disconnect();

        Task<byte[]> ExchangeAsync(byte[] requestAdu, int timeoutMs, CancellationToken cancellationToken = default);
    }

    /// <summary>Offline in-process Modbus transport wired directly to <see cref="SimulatedModbusSlave"/>.</summary>
    public sealed class LoopbackModbusTransport : IModbusTransport
    {
        private readonly SimulatedModbusSlave _slave;
        private readonly int _simulatedLatencyMs;
        private bool _simulateNextTimeout;

        public LoopbackModbusTransport(SimulatedModbusSlave slave, ModbusFrameFormat format, int simulatedLatencyMs = 4)
        {
            _slave = slave;
            FrameFormat = format;
            _simulatedLatencyMs = simulatedLatencyMs;
        }

        public ModbusFrameFormat FrameFormat { get; }
        public bool IsConnected { get; private set; }

        public void ArmOneShotFault(ModbusFaultInjection fault)
        {
            if (fault == ModbusFaultInjection.Timeout)
            {
                _simulateNextTimeout = true;
            }
            else
            {
                _slave.ArmOneShotFault(fault);
            }
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public void Disconnect() => IsConnected = false;

        public async Task<byte[]> ExchangeAsync(byte[] requestAdu, int timeoutMs, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Modbus transport is not connected.");
            }

            if (_simulateNextTimeout)
            {
                _simulateNextTimeout = false;
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(Math.Min(timeoutMs, 450));
                try
                {
                    await Task.Delay(Timeout.Infinite, timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"Modbus 从站响应超时 (>{Math.Min(timeoutMs, 450)} ms)。");
                }
            }

            if (_simulatedLatencyMs > 0)
            {
                await Task.Delay(_simulatedLatencyMs, cancellationToken).ConfigureAwait(false);
            }

            return _slave.ProcessAdu(FrameFormat, requestAdu);
        }

        public void Dispose() => Disconnect();
    }

    /// <summary>
    /// Real asynchronous Modbus TCP socket client using exact MBAP Length framing
    /// (<see cref="Stream.ReadExactlyAsync(Memory{byte}, CancellationToken)"/>) to prevent TCP sticky/partial packets.
    /// </summary>
    public sealed class TcpSocketModbusTransport : IModbusTransport
    {
        private readonly string _host;
        private readonly int _port;
        private readonly SemaphoreSlim _ioLock = new(1, 1);
        private TcpClient? _client;

        public TcpSocketModbusTransport(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public ModbusFrameFormat FrameFormat => ModbusFrameFormat.Tcp;
        public bool IsConnected => _client is { Connected: true };

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            Disconnect();
            var client = new TcpClient { NoDelay = true };
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                await client.ConnectAsync(_host, _port, connectCts.Token).ConfigureAwait(false);
                _client = client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        public void Disconnect()
        {
            _client?.Dispose();
            _client = null;
        }

        public async Task<byte[]> ExchangeAsync(byte[] requestAdu, int timeoutMs, CancellationToken cancellationToken = default)
        {
            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_client is not { Connected: true })
                {
                    throw new InvalidOperationException("Modbus TCP 未连接。");
                }

                var stream = _client.GetStream();
                using var ioCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                ioCts.CancelAfter(timeoutMs);

                try
                {
                    await stream.WriteAsync(requestAdu, ioCts.Token).ConfigureAwait(false);

                    // Step 1: Read exact 7-byte MBAP header (prevents TCP sticky/fragmented packet bugs).
                    var header = new byte[7];
                    await stream.ReadExactlyAsync(header, ioCts.Token).ConfigureAwait(false);
                    var remainingLength = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2)) - 1;
                    if (remainingLength is < 1 or > 256)
                    {
                        throw new InvalidDataException($"非法 MBAP 长度字段：{remainingLength + 1}。");
                    }

                    // Step 2: Read exact PDU payload bytes.
                    var response = new byte[7 + remainingLength];
                    header.CopyTo(response, 0);
                    await stream.ReadExactlyAsync(response.AsMemory(7, remainingLength), ioCts.Token).ConfigureAwait(false);
                    return response;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"Modbus TCP 响应超时 (>{timeoutMs} ms)。");
                }
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public void Dispose()
        {
            Disconnect();
            _ioLock.Dispose();
        }
    }
}
