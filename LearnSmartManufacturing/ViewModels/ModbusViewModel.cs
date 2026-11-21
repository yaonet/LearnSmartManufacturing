using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LearnSmartManufacturing.Models.Modbus;
using LearnSmartManufacturing.Services.Modbus;

namespace LearnSmartManufacturing.ViewModels
{
    public record TransportModeOption(string Name, ModbusTransportMode Value);

    public record FunctionCodeOption(string Name, ModbusFunctionCode Value);

    public record ByteOrderOption(string Name, ModbusByteOrder Value);

    public sealed record EndianPreviewRow(string OrderName, string Reg0Hex, string Reg1Hex, string WireBytesHex, string DecodedFromPlc);

    public partial class ModbusViewModel : ObservableObject, IDisposable
    {
        private const int MaxTransactions = 80;

        private readonly SimulatedModbusSlave _slave = new();
        private readonly SemaphoreSlim _busLock = new(1, 1);

        private IModbusTransport? _transport;
        private CancellationTokenSource? _pollCts;
        private Task? _pollTask;
        private ushort _nextTransId = 1;
        private ushort[] _lastHoldingRegs = new ushort[8];
        private ushort[] _lastInputRegs = new ushort[4];
        private bool _disposed;

        public ModbusViewModel()
        {
            TransportModes =
            [
                new TransportModeOption("内置模拟 PLC · Modbus TCP (MBAP)", ModbusTransportMode.SimulatedTcp),
                new TransportModeOption("内置模拟 PLC · Modbus RTU (CRC-16)", ModbusTransportMode.SimulatedRtu),
                new TransportModeOption("真实 Modbus TCP 设备 (Socket)", ModbusTransportMode.RealTcp),
            ];

            FunctionCodes =
            [
                new FunctionCodeOption("FC 0x03 · 读保持寄存器 (4xxxx)", ModbusFunctionCode.ReadHoldingRegisters),
                new FunctionCodeOption("FC 0x04 · 读输入寄存器 (3xxxx)", ModbusFunctionCode.ReadInputRegisters),
                new FunctionCodeOption("FC 0x01 · 读线圈 (0xxxx)", ModbusFunctionCode.ReadCoils),
                new FunctionCodeOption("FC 0x02 · 读离散输入 (1xxxx)", ModbusFunctionCode.ReadDiscreteInputs),
                new FunctionCodeOption("FC 0x05 · 写单线圈 (0xxxx)", ModbusFunctionCode.WriteSingleCoil),
                new FunctionCodeOption("FC 0x06 · 写单寄存器 (4xxxx)", ModbusFunctionCode.WriteSingleRegister),
                new FunctionCodeOption("FC 0x10 · 写浮点 2 寄存器 (Float32)", ModbusFunctionCode.WriteMultipleRegisters),
            ];

            ByteOrders =
            [
                new ByteOrderOption("CDAB · 字交换 (汇川/台达常用)", ModbusByteOrder.CDAB),
                new ByteOrderOption("ABCD · 大端 (标准网络序/西门子)", ModbusByteOrder.ABCD),
                new ByteOrderOption("DCBA · 小端 (完全反转)", ModbusByteOrder.DCBA),
                new ByteOrderOption("BADC · 字节交换", ModbusByteOrder.BADC),
            ];

            SelectedFunctionCode = FunctionCodes[0];
            SelectedByteOrder = ByteOrders[0];
            // Auto-connect to the built-in simulated PLC via OnSelectedTransportModeChanged.
            SelectedTransportMode = TransportModes[0];
        }

        public string Heading => "Modbus";

        public IReadOnlyList<TransportModeOption> TransportModes { get; }
        public IReadOnlyList<FunctionCodeOption> FunctionCodes { get; }
        public IReadOnlyList<ByteOrderOption> ByteOrders { get; }

        public ObservableCollection<ModbusTransaction> Transactions { get; } = [];
        public ObservableCollection<RegisterRow> RegisterRows { get; } = [];
        public ObservableCollection<EndianPreviewRow> EndianRows { get; } = [];

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsRealTcpMode), nameof(IsSimulatedMode), nameof(IsSimulatedRtuMode))]
        public partial TransportModeOption SelectedTransportMode { get; set; }

        [ObservableProperty]
        public partial string Host { get; set; } = "127.0.0.1";

        [ObservableProperty]
        public partial string PortText { get; set; } = "502";

        [ObservableProperty]
        public partial string UnitIdText { get; set; } = "1";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ConnectionBadgeText), nameof(ConnectButtonText))]
        public partial bool IsConnected { get; private set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PollButtonText))]
        public partial bool IsPolling { get; private set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PollIntervalText))]
        public partial int PollIntervalMs { get; set; } = 500;

        [ObservableProperty]
        public partial string ConnectionStatusMessage { get; private set; } = "已连接至内置模拟 PLC (Modbus TCP)";

        // Request builder
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsReadFunction), nameof(IsWriteFunction), nameof(ModbusAddressHint), nameof(WriteValueLabel))]
        public partial FunctionCodeOption SelectedFunctionCode { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ModbusAddressHint))]
        public partial string StartAddressText { get; set; } = "0";

        [ObservableProperty]
        public partial string QuantityText { get; set; } = "8";

        [ObservableProperty]
        public partial string WriteValueText { get; set; } = "235.5";

        [ObservableProperty]
        public partial ModbusTransaction? LatestTransaction { get; set; }

        [ObservableProperty]
        public partial int TxCount { get; private set; }

        [ObservableProperty]
        public partial int RxCount { get; private set; }

        [ObservableProperty]
        public partial int ErrorCount { get; private set; }

        [ObservableProperty]
        public partial string ArmedFaultBanner { get; private set; } = string.Empty;

        // Byte-order lab
        [ObservableProperty]
        public partial ByteOrderOption SelectedByteOrder { get; set; }

        [ObservableProperty]
        public partial string EncoderFloatText { get; set; } = "225.5";

        [ObservableProperty]
        public partial string PlcRawPairHexText { get; private set; } = "40001-40002 原始字: —";

        // Simulated PLC dashboard
        [ObservableProperty]
        public partial string ActualTempText { get; private set; } = "224.8 °C";

        [ObservableProperty]
        public partial string TargetTempText { get; private set; } = "225.5 °C";

        [ObservableProperty]
        public partial string ConveyorSpeedText { get; private set; } = "45.0 mm/s";

        [ObservableProperty]
        public partial string ProductionCountText { get; private set; } = "12,840 pcs";

        [ObservableProperty]
        public partial string CurrentAndPressureText { get; private set; } = "加热电流 18.2 A · 气压 0.62 MPa";

        [ObservableProperty]
        public partial bool CoilRunEnable { get; private set; } = true;

        [ObservableProperty]
        public partial bool CoilHeaterEnable { get; private set; } = true;

        [ObservableProperty]
        public partial bool CoilFanEnable { get; private set; }

        [ObservableProperty]
        public partial bool CoilAlarmLamp { get; private set; }

        [ObservableProperty]
        public partial bool DiSafetyDoor { get; private set; } = true;

        [ObservableProperty]
        public partial bool DiAirPressure { get; private set; } = true;

        [ObservableProperty]
        public partial bool DiEStopOk { get; private set; } = true;

        [ObservableProperty]
        public partial bool DiPartSensor { get; private set; } = true;

        public bool IsRealTcpMode => SelectedTransportMode?.Value == ModbusTransportMode.RealTcp;
        public bool IsSimulatedMode => !IsRealTcpMode;
        public bool IsSimulatedRtuMode => SelectedTransportMode?.Value == ModbusTransportMode.SimulatedRtu;
        public string ConnectionBadgeText => IsConnected ? "● 已连接" : "○ 未连接";
        public string ConnectButtonText => IsConnected ? "断开连接" : "连接";
        public string PollButtonText => IsPolling ? "停止轮询" : "开始轮询";
        public string PollIntervalText => $"{PollIntervalMs} ms";

        public bool IsReadFunction => SelectedFunctionCode?.Value is ModbusFunctionCode.ReadCoils
            or ModbusFunctionCode.ReadDiscreteInputs
            or ModbusFunctionCode.ReadHoldingRegisters
            or ModbusFunctionCode.ReadInputRegisters;

        public bool IsWriteFunction => !IsReadFunction;

        public string WriteValueLabel => SelectedFunctionCode?.Value switch
        {
            ModbusFunctionCode.WriteSingleCoil => "写入状态 (1=ON / 0=OFF)",
            ModbusFunctionCode.WriteSingleRegister => "写入值 (UInt16: 0 ~ 65535)",
            ModbusFunctionCode.WriteMultipleRegisters => $"写入 Float32 (按 {SelectedByteOrder?.Value})",
            _ => "写入值",
        };

        public string ModbusAddressHint
        {
            get
            {
                if (!ushort.TryParse(StartAddressText, out var addr))
                {
                    return "0-based 地址无效";
                }

                var prefix = SelectedFunctionCode?.Value switch
                {
                    ModbusFunctionCode.ReadCoils or ModbusFunctionCode.WriteSingleCoil => 1,
                    ModbusFunctionCode.ReadDiscreteInputs => 10001,
                    ModbusFunctionCode.ReadInputRegisters => 30001,
                    _ => 40001,
                };
                return $"协议偏移 {addr} → 传统 5 位地址 {prefix + addr:D5}";
            }
        }

        [RelayCommand]
        private async Task ToggleConnectionAsync()
        {
            if (IsConnected)
            {
                StopPolling();
                DisconnectInternal();
                ConnectionStatusMessage = "已断开连接";
                return;
            }

            await ConnectAsync();
        }

        [RelayCommand]
        private void TogglePolling()
        {
            if (IsPolling)
            {
                StopPolling();
                return;
            }

            if (!IsConnected)
            {
                ConnectionStatusMessage = "请先建立连接后再启动周期轮询";
                return;
            }

            _pollCts = new CancellationTokenSource();
            IsPolling = true;
            _pollTask = RunPollingLoopAsync(_pollCts.Token);
        }

        [RelayCommand]
        private async Task SendRequestAsync()
        {
            if (!IsConnected || _transport is null)
            {
                ConnectionStatusMessage = "未连接，无法发送 Modbus 请求";
                return;
            }

            if (!byte.TryParse(UnitIdText, out var unitId))
            {
                ConnectionStatusMessage = "站号 UnitID 须为 0 ~ 255";
                return;
            }

            if (!ushort.TryParse(StartAddressText, out var startAddr))
            {
                ConnectionStatusMessage = "起始地址须为 0 ~ 65535";
                return;
            }

            var fc = SelectedFunctionCode.Value;
            byte[] pdu;
            ushort readQty = 1;

            try
            {
                switch (fc)
                {
                    case ModbusFunctionCode.ReadCoils:
                    case ModbusFunctionCode.ReadDiscreteInputs:
                    case ModbusFunctionCode.ReadHoldingRegisters:
                    case ModbusFunctionCode.ReadInputRegisters:
                        if (!ushort.TryParse(QuantityText, out readQty) || readQty < 1 || readQty > 125)
                        {
                            ConnectionStatusMessage = "读取数量须为 1 ~ 125";
                            return;
                        }

                        pdu = ModbusCodec.BuildReadPdu(fc, startAddr, readQty);
                        break;

                    case ModbusFunctionCode.WriteSingleCoil:
                        var on = WriteValueText.Trim() is "1" or "true" or "True" or "ON" or "on";
                        pdu = ModbusCodec.BuildWriteSingleCoilPdu(startAddr, on);
                        break;

                    case ModbusFunctionCode.WriteSingleRegister:
                        if (!ushort.TryParse(WriteValueText, out var regVal))
                        {
                            ConnectionStatusMessage = "单寄存器写入值须为 0 ~ 65535";
                            return;
                        }

                        pdu = ModbusCodec.BuildWriteSingleRegisterPdu(startAddr, regVal);
                        break;

                    case ModbusFunctionCode.WriteMultipleRegisters:
                        if (!float.TryParse(WriteValueText, NumberStyles.Float, CultureInfo.CurrentCulture, out var floatVal) || !float.IsFinite(floatVal))
                        {
                            ConnectionStatusMessage = "请输入有效的 Float32 浮点数值";
                            return;
                        }

                        var (r0, r1) = ModbusCodec.EncodeFloat32(floatVal, SelectedByteOrder.Value);
                        pdu = ModbusCodec.BuildWriteMultipleRegistersPdu(startAddr, [r0, r1]);
                        break;

                    default:
                        return;
                }
            }
            catch (Exception ex)
            {
                ConnectionStatusMessage = ex.Message;
                return;
            }

            var (tx, respPdu) = await ExecuteTransactionAsync(unitId, fc, pdu, CancellationToken.None);
            if (tx.IsSuccess && respPdu.Length > 0)
            {
                if (fc is ModbusFunctionCode.ReadHoldingRegisters or ModbusFunctionCode.ReadInputRegisters)
                {
                    var regs = ModbusCodec.ParseReadRegistersPdu(respPdu);
                    UpdateRegisterRows(fc, startAddr, regs);
                }
                else if (fc is ModbusFunctionCode.ReadCoils or ModbusFunctionCode.ReadDiscreteInputs)
                {
                    var bits = ModbusCodec.ParseReadBitsPdu(respPdu, readQty);
                    UpdateBitRows(fc, startAddr, bits);
                }
                else
                {
                    // Refresh PLC state after any write so Tab 2 reflects the write immediately.
                    await RefreshDashboardBatchAsync();
                }
            }
        }

        [RelayCommand]
        private async Task WriteFloatToTargetTempAsync()
        {
            if (!IsConnected)
            {
                ConnectionStatusMessage = "未连接，无法写入目标炉温";
                return;
            }

            if (!float.TryParse(EncoderFloatText, NumberStyles.Float, CultureInfo.CurrentCulture, out var val) || !float.IsFinite(val))
            {
                ConnectionStatusMessage = "请输入有效的 Float32 数值";
                return;
            }

            var unitId = byte.TryParse(UnitIdText, out var u) ? u : (byte)1;
            var (r0, r1) = ModbusCodec.EncodeFloat32(val, SelectedByteOrder.Value);
            var pdu = ModbusCodec.BuildWriteMultipleRegistersPdu(0, [r0, r1]);
            await ExecuteTransactionAsync(unitId, ModbusFunctionCode.WriteMultipleRegisters, pdu, CancellationToken.None);
            await RefreshDashboardBatchAsync();
        }

        [RelayCommand]
        private async Task ToggleCoilAsync(string? addressText)
        {
            if (!IsConnected || !ushort.TryParse(addressText, out var addr))
            {
                return;
            }

            var current = addr switch
            {
                0 => CoilRunEnable,
                1 => CoilHeaterEnable,
                2 => CoilFanEnable,
                3 => CoilAlarmLamp,
                _ => false,
            };

            var unitId = byte.TryParse(UnitIdText, out var u) ? u : (byte)1;
            var pdu = ModbusCodec.BuildWriteSingleCoilPdu(addr, !current);
            await ExecuteTransactionAsync(unitId, ModbusFunctionCode.WriteSingleCoil, pdu, CancellationToken.None);
            await RefreshDashboardBatchAsync();
        }

        [RelayCommand]
        private void ArmFault(string? faultName)
        {
            if (_transport is not LoopbackModbusTransport loopback)
            {
                return;
            }

            if (Enum.TryParse<ModbusFaultInjection>(faultName, out var fault))
            {
                if (fault == ModbusFaultInjection.CorruptRtuCrc && loopback.FrameFormat != ModbusFrameFormat.Rtu)
                {
                    ConnectionStatusMessage = "请先切换到“Modbus RTU (CRC-16)”模式再注入 CRC 损坏故障";
                    return;
                }

                loopback.ArmOneShotFault(fault);
                ArmedFaultBanner = fault switch
                {
                    ModbusFaultInjection.IllegalAddress => "已预置下一次请求故障：从站返回异常码 0x02 (非法地址)",
                    ModbusFaultInjection.SlaveFailure => "已预置下一次请求故障：从站返回异常码 0x04 (从站故障)",
                    ModbusFaultInjection.CorruptRtuCrc => "已预置下一次请求故障：RTU 响应帧 CRC-16 字节损坏",
                    ModbusFaultInjection.Timeout => "已预置下一次请求故障：从站响应超时",
                    _ => string.Empty,
                };
            }
        }

        [RelayCommand]
        private void ClearTransactions()
        {
            Transactions.Clear();
            TxCount = 0;
            RxCount = 0;
            ErrorCount = 0;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopPolling();
            DisconnectInternal();
            _busLock.Dispose();
        }

        partial void OnSelectedTransportModeChanged(TransportModeOption value)
        {
            StopPolling();
            DisconnectInternal();
            ArmedFaultBanner = string.Empty;

            if (value.Value == ModbusTransportMode.SimulatedTcp)
            {
                ConnectSimulatedSync(ModbusFrameFormat.Tcp);
            }
            else if (value.Value == ModbusTransportMode.SimulatedRtu)
            {
                ConnectSimulatedSync(ModbusFrameFormat.Rtu);
            }
            else
            {
                ConnectionStatusMessage = "真实 TCP 模式：请确认 IP 与端口后点击“连接”";
            }
        }

        partial void OnSelectedByteOrderChanged(ByteOrderOption value)
        {
            OnPropertyChanged(nameof(WriteValueLabel));
            RebuildEndianTable();
            UpdateDashboardFromRegisters();
        }

        partial void OnEncoderFloatTextChanged(string value) => RebuildEndianTable();

        private void ConnectSimulatedSync(ModbusFrameFormat format)
        {
            _transport?.Dispose();
            var loopback = new LoopbackModbusTransport(_slave, format, simulatedLatencyMs: 3);
            loopback.ConnectAsync().GetAwaiter().GetResult();
            _transport = loopback;
            IsConnected = true;
            ConnectionStatusMessage = format == ModbusFrameFormat.Tcp
                ? "已连接至内置模拟 PLC (Modbus TCP · MBAP)"
                : "已连接至内置模拟 PLC (Modbus RTU · CRC-16)";

            SyncSnapshotFromSlave(format);
        }

        private void SyncSnapshotFromSlave(ModbusFrameFormat format)
        {
            var unitId = byte.TryParse(UnitIdText, out var u) ? u : (byte)1;

            var holdReq = ModbusCodec.WrapAdu(format, _nextTransId++, unitId,
                ModbusCodec.BuildReadPdu(ModbusFunctionCode.ReadHoldingRegisters, 0, 8));
            var holdResp = _slave.ProcessAdu(format, holdReq);
            if (ModbusCodec.TryUnwrapAdu(format, holdResp, out _, out _, out var holdPdu, out _))
            {
                _lastHoldingRegs = ModbusCodec.ParseReadRegistersPdu(holdPdu);
                UpdateRegisterRows(ModbusFunctionCode.ReadHoldingRegisters, 0, _lastHoldingRegs);
                LatestTransaction = new ModbusTransaction(DateTime.Now, format, (ushort)(_nextTransId - 1), unitId,
                    ModbusFunctionCode.ReadHoldingRegisters, holdReq, holdResp, true, ModbusExceptionCode.None, 0.5,
                    $"初始同步 FC 0x03 ({holdResp.Length} B)");
            }

            var inResp = _slave.ProcessAdu(format, ModbusCodec.WrapAdu(format, _nextTransId++, unitId,
                ModbusCodec.BuildReadPdu(ModbusFunctionCode.ReadInputRegisters, 0, 4)));
            if (ModbusCodec.TryUnwrapAdu(format, inResp, out _, out _, out var inPdu, out _))
            {
                _lastInputRegs = ModbusCodec.ParseReadRegistersPdu(inPdu);
            }

            var coilResp = _slave.ProcessAdu(format, ModbusCodec.WrapAdu(format, _nextTransId++, unitId,
                ModbusCodec.BuildReadPdu(ModbusFunctionCode.ReadCoils, 0, 4)));
            if (ModbusCodec.TryUnwrapAdu(format, coilResp, out _, out _, out var coilPdu, out _))
            {
                var coils = ModbusCodec.ParseReadBitsPdu(coilPdu, 4);
                CoilRunEnable = coils[0];
                CoilHeaterEnable = coils[1];
                CoilFanEnable = coils[2];
                CoilAlarmLamp = coils[3];
            }

            var diResp = _slave.ProcessAdu(format, ModbusCodec.WrapAdu(format, _nextTransId++, unitId,
                ModbusCodec.BuildReadPdu(ModbusFunctionCode.ReadDiscreteInputs, 0, 4)));
            if (ModbusCodec.TryUnwrapAdu(format, diResp, out _, out _, out var diPdu, out _))
            {
                var dis = ModbusCodec.ParseReadBitsPdu(diPdu, 4);
                DiSafetyDoor = dis[0];
                DiAirPressure = dis[1];
                DiEStopOk = dis[2];
                DiPartSensor = dis[3];
            }

            UpdateDashboardFromRegisters();
            RebuildEndianTable();
        }

        private async Task ConnectAsync()
        {
            DisconnectInternal();
            ArmedFaultBanner = string.Empty;

            if (SelectedTransportMode.Value == ModbusTransportMode.SimulatedTcp)
            {
                ConnectSimulatedSync(ModbusFrameFormat.Tcp);
                await RefreshDashboardBatchAsync();
                return;
            }

            if (SelectedTransportMode.Value == ModbusTransportMode.SimulatedRtu)
            {
                ConnectSimulatedSync(ModbusFrameFormat.Rtu);
                await RefreshDashboardBatchAsync();
                return;
            }

            if (!int.TryParse(PortText, out var port) || port is < 1 or > 65535)
            {
                ConnectionStatusMessage = "端口号须在 1 ~ 65535 之间";
                return;
            }

            var socketTransport = new TcpSocketModbusTransport(Host.Trim(), port);
            try
            {
                ConnectionStatusMessage = $"正在连接 {Host}:{port} ...";
                await socketTransport.ConnectAsync();
                _transport = socketTransport;
                IsConnected = true;
                ConnectionStatusMessage = $"已连接至真实 Modbus TCP 设备 {Host}:{port}";
            }
            catch (Exception ex)
            {
                socketTransport.Dispose();
                IsConnected = false;
                ConnectionStatusMessage = $"TCP 连接失败：{ex.Message}";
            }
        }

        private void DisconnectInternal()
        {
            _transport?.Dispose();
            _transport = null;
            IsConnected = false;
        }

        private void StopPolling()
        {
            if (_pollCts is not null)
            {
                _pollCts.Cancel();
                _pollCts.Dispose();
                _pollCts = null;
            }

            IsPolling = false;
            _pollTask = null;
        }

        private async Task RunPollingLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && IsConnected)
                {
                    if (IsSimulatedMode)
                    {
                        _slave.AdvanceProcessSimulation();
                    }

                    await RefreshDashboardBatchAsync(ct);
                    await Task.Delay(Math.Clamp(PollIntervalMs, 150, 3000), ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal poll stop.
            }
            finally
            {
                IsPolling = false;
            }
        }

        private async Task RefreshDashboardBatchAsync(CancellationToken ct = default)
        {
            if (!IsConnected || _transport is null)
            {
                return;
            }

            var unitId = byte.TryParse(UnitIdText, out var u) ? u : (byte)1;

            // Merged batch reads: 1 frame per register bank instead of per-register polling.
            var (txHold, pduHold) = await ExecuteTransactionAsync(unitId, ModbusFunctionCode.ReadHoldingRegisters,
                ModbusCodec.BuildReadPdu(ModbusFunctionCode.ReadHoldingRegisters, 0, 8), ct);
            if (txHold.IsSuccess && pduHold.Length > 0)
            {
                _lastHoldingRegs = ModbusCodec.ParseReadRegistersPdu(pduHold);
                UpdateRegisterRows(ModbusFunctionCode.ReadHoldingRegisters, 0, _lastHoldingRegs);
            }

            var (txIn, pduIn) = await ExecuteTransactionAsync(unitId, ModbusFunctionCode.ReadInputRegisters,
                ModbusCodec.BuildReadPdu(ModbusFunctionCode.ReadInputRegisters, 0, 4), ct);
            if (txIn.IsSuccess && pduIn.Length > 0)
            {
                _lastInputRegs = ModbusCodec.ParseReadRegistersPdu(pduIn);
            }

            var (txCoil, pduCoil) = await ExecuteTransactionAsync(unitId, ModbusFunctionCode.ReadCoils,
                ModbusCodec.BuildReadPdu(ModbusFunctionCode.ReadCoils, 0, 4), ct);
            if (txCoil.IsSuccess && pduCoil.Length > 0)
            {
                var coils = ModbusCodec.ParseReadBitsPdu(pduCoil, 4);
                CoilRunEnable = coils[0];
                CoilHeaterEnable = coils[1];
                CoilFanEnable = coils[2];
                CoilAlarmLamp = coils[3];
            }

            var (txDi, pduDi) = await ExecuteTransactionAsync(unitId, ModbusFunctionCode.ReadDiscreteInputs,
                ModbusCodec.BuildReadPdu(ModbusFunctionCode.ReadDiscreteInputs, 0, 4), ct);
            if (txDi.IsSuccess && pduDi.Length > 0)
            {
                var dis = ModbusCodec.ParseReadBitsPdu(pduDi, 4);
                DiSafetyDoor = dis[0];
                DiAirPressure = dis[1];
                DiEStopOk = dis[2];
                DiPartSensor = dis[3];
            }

            UpdateDashboardFromRegisters();
            RebuildEndianTable();
        }

        private async Task<(ModbusTransaction Tx, byte[] ResponsePdu)> ExecuteTransactionAsync(
            byte unitId,
            ModbusFunctionCode fc,
            byte[] pdu,
            CancellationToken ct)
        {
            await _busLock.WaitAsync(ct);
            try
            {
                if (_transport is not { IsConnected: true } transport)
                {
                    var disconnectedTx = new ModbusTransaction(DateTime.Now, ModbusFrameFormat.Tcp, 0, unitId, fc, [], [], false,
                        ModbusExceptionCode.None, 0, "未连接");
                    return (disconnectedTx, []);
                }

                var format = transport.FrameFormat;
                var transId = _nextTransId++;
                if (_nextTransId == 0)
                {
                    _nextTransId = 1;
                }

                var reqAdu = ModbusCodec.WrapAdu(format, transId, unitId, pdu);
                ArmedFaultBanner = string.Empty;
                TxCount++;

                var sw = Stopwatch.StartNew();
                byte[] respAdu = [];
                byte[] respPdu = [];
                bool ok = false;
                var exCode = ModbusExceptionCode.None;
                string summary;

                try
                {
                    respAdu = await transport.ExchangeAsync(reqAdu, timeoutMs: 800, ct);
                    sw.Stop();

                    if (!ModbusCodec.TryUnwrapAdu(format, respAdu, out var respTransId, out _, out respPdu, out var unwrapErr))
                    {
                        ErrorCount++;
                        summary = unwrapErr;
                    }
                    else if (format == ModbusFrameFormat.Tcp && respTransId != transId)
                    {
                        ErrorCount++;
                        summary = $"MBAP TransactionID 不匹配（发 0x{transId:X4}，收 0x{respTransId:X4}）";
                    }
                    else if (ModbusCodec.IsExceptionPdu(respPdu, out exCode))
                    {
                        ErrorCount++;
                        RxCount++;
                        summary = $"从站异常响应：{ModbusCodec.DescribeException(exCode)}";
                    }
                    else
                    {
                        ok = true;
                        RxCount++;
                        summary = $"FC 0x{(byte)fc:X2} 成功 ({respAdu.Length} B，{sw.Elapsed.TotalMilliseconds:F1} ms)";
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    sw.Stop();
                    ErrorCount++;
                    summary = ex.Message;
                }

                var tx = new ModbusTransaction(DateTime.Now, format, transId, unitId, fc, reqAdu, respAdu, ok, exCode, sw.Elapsed.TotalMilliseconds, summary);
                RecordTransaction(tx);
                return (tx, ok ? respPdu : []);
            }
            finally
            {
                _busLock.Release();
            }
        }

        private void RecordTransaction(ModbusTransaction tx)
        {
            if (_disposed)
            {
                return;
            }

            LatestTransaction = tx;
            Transactions.Insert(0, tx);
            while (Transactions.Count > MaxTransactions)
            {
                Transactions.RemoveAt(Transactions.Count - 1);
            }
        }

        private void UpdateRegisterRows(ModbusFunctionCode fc, ushort startAddress, ushort[] regs)
        {
            RegisterRows.Clear();
            var prefix = fc == ModbusFunctionCode.ReadInputRegisters ? 30001 : 40001;
            for (var i = 0; i < regs.Length; i++)
            {
                var addr = (ushort)(startAddress + i);
                var (desc, eng) = DescribeRegister(fc, addr, regs, i);
                RegisterRows.Add(new RegisterRow(addr, $"{prefix + addr:D5}", desc, regs[i], eng));
            }
        }

        private void UpdateBitRows(ModbusFunctionCode fc, ushort startAddress, bool[] bits)
        {
            RegisterRows.Clear();
            var prefix = fc == ModbusFunctionCode.ReadDiscreteInputs ? 10001 : 1;
            for (var i = 0; i < bits.Length; i++)
            {
                var addr = (ushort)(startAddress + i);
                var desc = DescribeBit(fc, addr);
                RegisterRows.Add(new RegisterRow(addr, $"{prefix + addr:D5}", desc, bits[i] ? (ushort)1 : (ushort)0, bits[i] ? "ON (1)" : "OFF (0)"));
            }
        }

        private void UpdateDashboardFromRegisters()
        {
            var order = SelectedByteOrder?.Value ?? ModbusByteOrder.CDAB;
            if (_lastHoldingRegs.Length >= 5)
            {
                var target = ModbusCodec.DecodeFloat32(_lastHoldingRegs[0], _lastHoldingRegs[1], order);
                var speed = _lastHoldingRegs[2] / 10.0;
                var prod = ModbusCodec.DecodeInt32(_lastHoldingRegs[3], _lastHoldingRegs[4], order);
                TargetTempText = float.IsFinite(target) ? $"{target:F1} °C" : "NaN";
                ConveyorSpeedText = $"{speed:F1} mm/s";
                ProductionCountText = $"{prod:N0} pcs";
                PlcRawPairHexText = $"40001-40002 原始寄存器: 0x{_lastHoldingRegs[0]:X4}  0x{_lastHoldingRegs[1]:X4}";
            }

            if (_lastInputRegs.Length >= 4)
            {
                var actual = ModbusCodec.DecodeFloat32(_lastInputRegs[0], _lastInputRegs[1], order);
                var current = _lastInputRegs[2] / 10.0;
                var pressure = _lastInputRegs[3] / 100.0;
                ActualTempText = float.IsFinite(actual) ? $"{actual:F1} °C" : "NaN";
                CurrentAndPressureText = $"加热电流 {current:F1} A · 气压 {pressure:F2} MPa";
            }
        }

        private void RebuildEndianTable()
        {
            if (ByteOrders is null)
            {
                return;
            }

            EndianRows.Clear();
            var hasInput = float.TryParse(EncoderFloatText, NumberStyles.Float, CultureInfo.CurrentCulture, out var fVal) && float.IsFinite(fVal);
            var hasPlc = _lastHoldingRegs.Length >= 2;

            foreach (var opt in ByteOrders)
            {
                var order = opt.Value;
                string r0Hex = "—", r1Hex = "—", wireHex = "—";
                if (hasInput)
                {
                    var (r0, r1) = ModbusCodec.EncodeFloat32(fVal, order);
                    r0Hex = $"0x{r0:X4}";
                    r1Hex = $"0x{r1:X4}";
                    wireHex = $"{r0 >> 8:X2} {r0 & 0xFF:X2} {r1 >> 8:X2} {r1 & 0xFF:X2}";
                }

                string decoded = "—";
                if (hasPlc)
                {
                    var d = ModbusCodec.DecodeFloat32(_lastHoldingRegs[0], _lastHoldingRegs[1], order);
                    decoded = float.IsFinite(d)
                        ? (Math.Abs(d) is > 1e5f or (< 1e-3f and > 0) ? $"{d:E2}" : $"{d:F2} °C")
                        : "NaN";
                }

                EndianRows.Add(new EndianPreviewRow(order.ToString(), r0Hex, r1Hex, wireHex, decoded));
            }
        }

        private (string Description, string Engineering) DescribeRegister(ModbusFunctionCode fc, ushort addr, ushort[] regs, int idx)
        {
            var order = SelectedByteOrder?.Value ?? ModbusByteOrder.CDAB;
            if (fc == ModbusFunctionCode.ReadHoldingRegisters)
            {
                return addr switch
                {
                    0 => ("目标炉温 Float (字0)", idx + 1 < regs.Length ? $"→ {ModbusCodec.DecodeFloat32(regs[idx], regs[idx + 1], order):F1} °C ({order})" : "Float 低/高字"),
                    1 => ("目标炉温 Float (字1)", "与 40001 组成 32-bit Float"),
                    2 => ("传送带速度 (×0.1)", $"{regs[idx] / 10.0:F1} mm/s"),
                    3 => ("累计产能 Int32 (字0)", idx + 1 < regs.Length ? $"→ {ModbusCodec.DecodeInt32(regs[idx], regs[idx + 1], order):N0} pcs" : "Int32 字"),
                    4 => ("累计产能 Int32 (字1)", "与 40004 组成 32-bit Int"),
                    5 => ("当前配方号", $"Recipe #{regs[idx]}"),
                    6 => ("PID 比例系数 Kp (×0.01)", $"{regs[idx] / 100.0:F2}"),
                    7 => ("超温报警上限", $"{regs[idx]} °C"),
                    _ => ($"保持寄存器 #{addr}", $"{regs[idx]}"),
                };
            }

            return addr switch
            {
                0 => ("实测炉温 Float (字0)", idx + 1 < regs.Length ? $"→ {ModbusCodec.DecodeFloat32(regs[idx], regs[idx + 1], order):F1} °C ({order})" : "Float 字"),
                1 => ("实测炉温 Float (字1)", "与 30001 组成 32-bit Float"),
                2 => ("实时加热电流 (×0.1)", $"{regs[idx] / 10.0:F1} A"),
                3 => ("压缩空气压力 (×0.01)", $"{regs[idx] / 100.0:F2} MPa"),
                _ => ($"输入寄存器 #{addr}", $"{regs[idx]}"),
            };
        }

        private static string DescribeBit(ModbusFunctionCode fc, ushort addr)
        {
            if (fc == ModbusFunctionCode.ReadCoils)
            {
                return addr switch
                {
                    0 => "产线运行启动 (RunEnable)",
                    1 => "加热区使能 (HeaterEnable)",
                    2 => "冷却风机 (FanEnable)",
                    3 => "报警蜂鸣灯 (AlarmLamp)",
                    _ => $"线圈 #{addr}",
                };
            }

            return addr switch
            {
                0 => "安全门闭合传感器",
                1 => "气源压力正常开关",
                2 => "急停按钮释放状态",
                3 => "工件到位光电开关",
                _ => $"离散输入 #{addr}",
            };
        }
    }
}
