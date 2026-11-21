using System.Net;
using System.Net.Sockets;

namespace LearnSmartManufacturing.Services.SerialTcp
{
    /// <summary>
    /// Real localhost TCP Socket loopback pair (TcpListener + TcpClient on 127.0.0.1) so the user can
    /// prove the byte-stream frame decoder runs over genuine OS TCP sockets with zero external hardware.
    /// </summary>
    public sealed class LoopbackTcpChannel : IDisposable
    {
        private TcpListener? _listener;
        private TcpClient? _serverPeer;
        private TcpClient? _client;
        private CancellationTokenSource? _cts;
        private Task? _readLoop;
        private bool _disposed;

        public bool IsConnected => _client is { Connected: true } && _serverPeer is { Connected: true };
        public int Port { get; private set; }

        public event Action<byte[]>? ChunkReceived;

        public async Task StartAsync(CancellationToken ct = default)
        {
            Stop();

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            _client = new TcpClient { NoDelay = true };
            var acceptTask = _listener.AcceptTcpClientAsync(ct);
            await _client.ConnectAsync(IPAddress.Loopback, Port, ct).ConfigureAwait(false);
            _serverPeer = await acceptTask.ConfigureAwait(false);
            _serverPeer.NoDelay = true;

            _cts = new CancellationTokenSource();
            var readStream = _serverPeer.GetStream();
            _readLoop = Task.Run(() => RunReadLoopAsync(readStream, _cts.Token), CancellationToken.None);
        }

        public async Task SendAsync(byte[] payload, CancellationToken ct = default)
        {
            if (_client is not { Connected: true })
            {
                throw new InvalidOperationException("本地 TCP 环回未启动。");
            }

            await _client.GetStream().WriteAsync(payload, ct).ConfigureAwait(false);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            _client?.Dispose();
            _client = null;
            _serverPeer?.Dispose();
            _serverPeer = null;

            try
            {
                _listener?.Stop();
            }
            catch
            {
                // Ignore listener stop errors on teardown.
            }

            _listener = null;
            Port = 0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }

        private async Task RunReadLoopAsync(NetworkStream stream, CancellationToken ct)
        {
            var buf = new byte[1024];
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var n = await stream.ReadAsync(buf, ct).ConfigureAwait(false);
                    if (n == 0) break;
                    var copy = new byte[n];
                    Buffer.BlockCopy(buf, 0, copy, 0, n);
                    ChunkReceived?.Invoke(copy);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch
            {
                // Socket closed.
            }
        }
    }
}
