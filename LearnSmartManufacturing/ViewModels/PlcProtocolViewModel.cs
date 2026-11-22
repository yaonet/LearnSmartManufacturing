using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LearnSmartManufacturing.Models.Plc;
using LearnSmartManufacturing.Services.Plc;

namespace LearnSmartManufacturing.ViewModels
{
    public sealed record PlcBrandOption(
        PlcBrandKind Brand,
        string Name,
        string PortText,
        string EndianText,
        string MaxPduText,
        string HandshakeRule,
        string PitfallNote);

    public sealed record PlcDataTypeOption(PlcDataType Type, string Name);

    public sealed record PlcQuickAddressChip(
        string Address,
        PlcDataType Type,
        string DefaultWriteValue,
        string Label);

    public partial class PlcProtocolViewModel : ObservableObject
    {
        private const int MaxTransactions = 60;
        private readonly SimulatedPlcEngine _engine = new();

        public PlcProtocolViewModel()
        {
            BrandOptions =
            [
                new(PlcBrandKind.SiemensS7, "西门子 S7 (1200/1500/300)", "TCP 102", "大端序 ABCD", "PDU 240/960 B",
                    "COTP CR/CC + S7 SetupComm",
                    "① TIA Portal 必须关闭 DB 块“优化的块访问”并在 CPU 属性勾选“允许来自远程对象的 PUT/GET 通信”；② S7 String 头部自带 2 字节 [MaxLen][ActLen]，直接按 ASCII 解码会多出前 2 个乱码字符。"),
                new(PlcBrandKind.MitsubishiMc3E, "三菱 MC (SLMP 3E 二进制)", "TCP 5000", "小端序 DCBA", "960 字 / 帧",
                    "直连发 50 00 帧 (无额外握手)",
                    "① D/M 软元件按十进制寻址，而 X/Y/B/W 按十六进制（如 X1A = 26）；② 3E 二进制帧比 ASCII 帧体积减半，高节拍产线严禁使用 ASCII 帧逐点轮询。"),
                new(PlcBrandKind.OmronFinsTcp, "欧姆龙 FINS/TCP (NJ/NX/CJ)", "TCP 9600", "字内大端 · 浮点 CDAB", "999 字 / 帧",
                    "20B 节点握手 (Cmd=0 → SA1/DA1)",
                    "① TCP 建连后必须先发 Cmd=0 获取 PC/PLC 节点号 (SA1/DA1)，直接发 Cmd=2 会被 PLC 断开；② 32 位浮点常采用低字在前的 CDAB 字节序；③ 每次响应须核验 2B EndCode == 00 00。"),
            ];

            DataTypeOptions =
            [
                new(PlcDataType.Bool, "Bool (位)"),
                new(PlcDataType.Int16, "Int16 (16位整数)"),
                new(PlcDataType.Int32, "Int32 (32位整数)"),
                new(PlcDataType.Float32, "Float32 (单精度浮点)"),
                new(PlcDataType.S7String, "S7String (西门子 2B 头)"),
            ];

            BrandComparisons = PlcFrameCodec.BrandComparisons;

            SelectedDataType = DataTypeOptions[3]; // Float32
            SelectedBrand = BrandOptions[0];       // Triggers initial brand sync & Handshake

            // Execute an initial Float32 read so the frame dissector immediately shows a live S7 Read Var frame
            // while the COTP Handshake remains directly below it in the transaction history.
            RecordTransaction(_engine.ExecuteRead(SelectedBrand.Brand, AddressText, SelectedDataType.Type));
            RefreshBatchPlan();
            RefreshFormatDiagnostics();
        }

        public IReadOnlyList<PlcBrandOption> BrandOptions { get; }
        public IReadOnlyList<PlcDataTypeOption> DataTypeOptions { get; }
        public IReadOnlyList<PlcBrandComparisonRow> BrandComparisons { get; }

        public ObservableCollection<PlcMemoryTagSnapshot> MemoryTags { get; } = [];
        public ObservableCollection<PlcQuickAddressChip> QuickAddressChips { get; } = [];
        public ObservableCollection<PlcTransactionRecord> Transactions { get; } = [];

        [ObservableProperty]
        public partial int SelectedTabIndex { get; set; }

        [ObservableProperty]
        public partial PlcBrandOption SelectedBrand { get; set; }

        [ObservableProperty]
        public partial string AddressText { get; set; } = "DB1.DBD4";

        [ObservableProperty]
        public partial PlcDataTypeOption SelectedDataType { get; set; }

        [ObservableProperty]
        public partial string WriteValueText { get; set; } = "192.5";

        [ObservableProperty]
        public partial PlcTransactionRecord? LatestTransaction { get; set; }

        [ObservableProperty]
        public partial bool IsConnected { get; set; }

        [ObservableProperty]
        public partial int TxCount { get; set; }

        [ObservableProperty]
        public partial int ErrorCount { get; set; }

        [ObservableProperty]
        public partial string StatusBanner { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string LastOpBanner { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool IsLastOpSuccess { get; set; } = true;

        // Tab 2: Batch Optimization & Byte-Order / S7 String Diagnostics
        [ObservableProperty]
        public partial int BatchTagCount { get; set; } = 40;

        [ObservableProperty]
        public partial int BatchMaxGapBytes { get; set; } = 16;

        [ObservableProperty]
        public partial int BatchPduLimitBytes { get; set; } = 240;

        [ObservableProperty]
        public partial double BatchRttMs { get; set; } = 3.0;

        [ObservableProperty]
        public partial PlcBatchPlanResult? BatchPlan { get; set; }

        [ObservableProperty]
        public partial string S7StringAnatomyText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string EndianComparisonText { get; set; } = string.Empty;

        public string ActivePortText => SelectedBrand.PortText;
        public string ActiveEndianText => SelectedBrand.EndianText;
        public string ActiveMaxPduText => SelectedBrand.MaxPduText;
        public string ActiveHandshakeRule => SelectedBrand.HandshakeRule;
        public string ActivePitfallNote => SelectedBrand.PitfallNote;
        public string ConnectionBadgeText => IsConnected ? $"● 已建连 · {SelectedBrand.PortText}" : "○ 未连接";
        public string TxErrorSummary => $"{TxCount} / {ErrorCount}";

        public string BatchTagCountText => $"{BatchTagCount} 点";
        public string BatchMaxGapText => $"{BatchMaxGapBytes} B";
        public string BatchPduLimitText => $"{BatchPduLimitBytes} B";
        public string BatchRttText => string.Create(CultureInfo.InvariantCulture, $"{BatchRttMs:F1} ms");

        partial void OnSelectedBrandChanged(PlcBrandOption value)
        {
            RefreshQuickChips(value.Brand);
            RefreshMemoryTags(value.Brand);

            if (QuickAddressChips.Count > 0)
            {
                var preferred = QuickAddressChips.FirstOrDefault(c => c.Type == PlcDataType.Float32) ?? QuickAddressChips[0];
                AddressText = preferred.Address;
                SelectedDataType = DataTypeOptions.First(o => o.Type == preferred.Type);
                WriteValueText = preferred.DefaultWriteValue;
            }

            RecordTransaction(_engine.Handshake(value.Brand));
            IsConnected = _engine.IsConnected;

            OnPropertyChanged(nameof(ActivePortText));
            OnPropertyChanged(nameof(ActiveEndianText));
            OnPropertyChanged(nameof(ActiveMaxPduText));
            OnPropertyChanged(nameof(ActiveHandshakeRule));
            OnPropertyChanged(nameof(ActivePitfallNote));
            OnPropertyChanged(nameof(ConnectionBadgeText));
            StatusBanner = $"当前协议：{value.Name} ({value.PortText}) · {value.HandshakeRule}";
        }

        partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(ConnectionBadgeText));
        partial void OnTxCountChanged(int value) => OnPropertyChanged(nameof(TxErrorSummary));
        partial void OnErrorCountChanged(int value) => OnPropertyChanged(nameof(TxErrorSummary));

        partial void OnBatchTagCountChanged(int value)
        {
            OnPropertyChanged(nameof(BatchTagCountText));
            RefreshBatchPlan();
        }

        partial void OnBatchMaxGapBytesChanged(int value)
        {
            OnPropertyChanged(nameof(BatchMaxGapText));
            RefreshBatchPlan();
        }

        partial void OnBatchPduLimitBytesChanged(int value)
        {
            OnPropertyChanged(nameof(BatchPduLimitText));
            RefreshBatchPlan();
        }

        partial void OnBatchRttMsChanged(double value)
        {
            OnPropertyChanged(nameof(BatchRttText));
            RefreshBatchPlan();
        }

        [RelayCommand]
        private void Handshake()
        {
            var tx = _engine.Handshake(SelectedBrand.Brand);
            IsConnected = _engine.IsConnected;
            RecordTransaction(tx);
            RefreshMemoryTags(SelectedBrand.Brand);
        }

        [RelayCommand]
        private void Read()
        {
            if (string.IsNullOrWhiteSpace(AddressText)) return;
            var tx = _engine.ExecuteRead(SelectedBrand.Brand, AddressText, SelectedDataType.Type);
            RecordTransaction(tx);
        }

        [RelayCommand]
        private void Write()
        {
            if (string.IsNullOrWhiteSpace(AddressText)) return;
            var tx = _engine.ExecuteWrite(SelectedBrand.Brand, AddressText, SelectedDataType.Type, WriteValueText);
            RecordTransaction(tx);
            RefreshMemoryTags(SelectedBrand.Brand);
            RefreshFormatDiagnostics();
        }

        [RelayCommand]
        private void DemoFault()
        {
            var tx = _engine.ExecuteFaultDemo(SelectedBrand.Brand);
            RecordTransaction(tx);
        }

        [RelayCommand]
        private void ApplyAddressChip(PlcQuickAddressChip? chip)
        {
            if (chip is null) return;
            AddressText = chip.Address;
            SelectedDataType = DataTypeOptions.First(o => o.Type == chip.Type);
            WriteValueText = chip.DefaultWriteValue;
            Read();
        }

        [RelayCommand]
        private void SwitchBrandFromComparison(PlcBrandKind brand)
        {
            var match = BrandOptions.FirstOrDefault(b => b.Brand == brand);
            if (match is not null)
            {
                SelectedBrand = match;
                SelectedTabIndex = 0;
            }
        }

        [RelayCommand]
        private void ClearTransactions()
        {
            Transactions.Clear();
            LatestTransaction = null;
            TxCount = 0;
            ErrorCount = 0;
            LastOpBanner = "已清空报文历史，点击“读取 / 写入 / 重新握手”重新抓包解剖。";
            IsLastOpSuccess = true;
        }

        private void RecordTransaction(PlcTransactionRecord tx)
        {
            Transactions.Insert(0, tx);
            while (Transactions.Count > MaxTransactions)
            {
                Transactions.RemoveAt(Transactions.Count - 1);
            }

            LatestTransaction = tx;
            TxCount++;
            if (!tx.IsSuccess)
            {
                ErrorCount++;
            }

            IsLastOpSuccess = tx.IsSuccess;
            LastOpBanner = tx.IsSuccess
                ? $"[{tx.TimeText}] {tx.Operation} {tx.Address} → {tx.DecodedValueText} ({tx.StatusCodeText} · TX {tx.RequestBytes.Length}B / RX {tx.ResponseBytes.Length}B)"
                : $"[{tx.TimeText}] 异常 {tx.Operation} {tx.Address} · {tx.StatusCodeText} — {tx.DecodedValueText}";
        }

        private void RefreshMemoryTags(PlcBrandKind brand)
        {
            MemoryTags.Clear();
            foreach (var tag in _engine.GetMemorySnapshot(brand))
            {
                MemoryTags.Add(tag);
            }
        }

        private void RefreshQuickChips(PlcBrandKind brand)
        {
            QuickAddressChips.Clear();
            IEnumerable<PlcQuickAddressChip> chips = brand switch
            {
                PlcBrandKind.SiemensS7 =>
                [
                    new("DB1.DBX0.0", PlcDataType.Bool, "False", "DB1.DBX0.0 (运行位)"),
                    new("DB1.DBW2", PlcDataType.Int16, "1380", "DB1.DBW2 (速度 Int16)"),
                    new("DB1.DBD4", PlcDataType.Float32, "192.5", "DB1.DBD4 (炉温 Float32)"),
                    new("DB1.DBB8", PlcDataType.S7String, "PCB-B18", "DB1.DBB8 (配方 S7String)"),
                    new("M10.0", PlcDataType.Bool, "True", "M10.0 (安全门 Bit)"),
                ],
                PlcBrandKind.MitsubishiMc3E =>
                [
                    new("D100", PlcDataType.Int16, "4680", "D100 (产能 Int16)"),
                    new("D102", PlcDataType.Float32, "192.5", "D102 (炉温 Float32)"),
                    new("D104", PlcDataType.Int32, "135200", "D104 (伺服脉冲 Int32)"),
                    new("M10", PlcDataType.Bool, "False", "M10 (自动模式 Bit)"),
                    new("X1A", PlcDataType.Bool, "True", "X1A (光幕 Hex 输入)"),
                ],
                _ =>
                [
                    new("D100", PlcDataType.Int16, "1650", "D100 (设定速度 Int16)"),
                    new("D102", PlcDataType.Float32, "192.5", "D102 (炉温 CDAB Float)"),
                    new("D104", PlcDataType.Int32, "9200", "D104 (产能累加 Int32)"),
                    new("W0.00", PlcDataType.Bool, "False", "W0.00 (运行继电器 Bit)"),
                ],
            };

            foreach (var c in chips)
            {
                QuickAddressChips.Add(c);
            }
        }

        private void RefreshBatchPlan()
        {
            BatchPlan = PlcFrameCodec.EvaluateBatchOptimization(
                BatchTagCount, BatchMaxGapBytes, BatchPduLimitBytes, BatchRttMs);
        }

        private void RefreshFormatDiagnostics()
        {
            var s7Tags = _engine.GetMemorySnapshot(PlcBrandKind.SiemensS7);
            var strTag = s7Tags.FirstOrDefault(t => t.DataType == PlcDataType.S7String);
            if (strTag is not null)
            {
                S7StringAnatomyText = $"DB1.DBB8 当前值 \"{strTag.ValueText}\" ({strTag.Note}) · 原始字节：{strTag.RawHex}";
            }

            Span<byte> abcd = stackalloc byte[4];
            BinaryPrimitives.WriteSingleBigEndian(abcd, 186.5f);
            Span<byte> dcba = stackalloc byte[4];
            BinaryPrimitives.WriteSingleLittleEndian(dcba, 186.5f);
            Span<byte> cdab = [abcd[2], abcd[3], abcd[0], abcd[1]];

            EndianComparisonText =
                $"同值 Float32 = 186.5 °C 线上字节对照：西门子 S7 (ABCD) = {PlcFrameCodec.ToHex(abcd)} ｜ 三菱 MC (DCBA) = {PlcFrameCodec.ToHex(dcba)} ｜ 欧姆龙 FINS (CDAB) = {PlcFrameCodec.ToHex(cdab)}";
        }
    }
}
