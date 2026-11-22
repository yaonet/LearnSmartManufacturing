using System.Globalization;

namespace LearnSmartManufacturing.Models.Patterns
{
    public enum PlcVendorKind
    {
        SiemensS7,
        MitsubishiMc,
        OmronFins,
    }

    public enum PipelineNodeState
    {
        Neutral,
        Passed,
        Recovered,
        Blocked,
        Skipped,
    }

    public sealed record PipelineVisualNode(
        string Group,
        string Title,
        string Subtitle,
        PipelineNodeState State);

    public sealed record PatternTraceEntry(
        string PatternTag,
        string Stage,
        string Message,
        string Level);

    public sealed record PlcReadOutcome(bool IsSuccess, string ValueText, int Attempts, double ElapsedMs, string Detail);

    #region 1. Factory Pattern (Multi-Brand PLC Driver HAL)

    public interface IPlcDriver
    {
        string VendorName { get; }
        string ProtocolBadge { get; }
        PlcReadOutcome ReadStationData(string address, ref int transientFailuresRemaining, List<PatternTraceEntry> trace);
    }

    public sealed class SiemensS7Driver : IPlcDriver
    {
        public string VendorName => "SiemensS7Driver";
        public string ProtocolBadge => "S7Comm · TCP 102";

        public PlcReadOutcome ReadStationData(string address, ref int transientFailuresRemaining, List<PatternTraceEntry> trace)
        {
            if (transientFailuresRemaining > 0)
            {
                transientFailuresRemaining--;
                trace.Add(new("Factory/Driver", VendorName, $"读取 {address} 遭遇 TCP 瞬断 (模拟干扰，剩余失败 {transientFailuresRemaining} 次)", "Warn"));
                return new(false, "ERR_TIMEOUT", 1, 18.0, "S7 TPKT 响应超时");
            }

            trace.Add(new("Factory/Driver", VendorName, $"读取 {address} (Big-Endian DB1) 成功 → 186.50 °C", "Info"));
            return new(true, "186.50 °C", 1, 2.4, "S7 0xFF OK");
        }
    }

    public sealed class MitsubishiMcDriver : IPlcDriver
    {
        public string VendorName => "MitsubishiMcDriver";
        public string ProtocolBadge => "SLMP 3E · TCP 5000";

        public PlcReadOutcome ReadStationData(string address, ref int transientFailuresRemaining, List<PatternTraceEntry> trace)
        {
            if (transientFailuresRemaining > 0)
            {
                transientFailuresRemaining--;
                trace.Add(new("Factory/Driver", VendorName, $"读取 {address} 遭遇瞬断 (剩余失败 {transientFailuresRemaining} 次)", "Warn"));
                return new(false, "ERR_TIMEOUT", 1, 16.0, "MC 3E 响应超时");
            }

            trace.Add(new("Factory/Driver", VendorName, $"读取 {address} (Little-Endian D 区) 成功 → 186.50 °C", "Info"));
            return new(true, "186.50 °C", 1, 1.9, "MC 0x0000 OK");
        }
    }

    public sealed class OmronFinsDriver : IPlcDriver
    {
        public string VendorName => "OmronFinsDriver";
        public string ProtocolBadge => "FINS/TCP · 9600";

        public PlcReadOutcome ReadStationData(string address, ref int transientFailuresRemaining, List<PatternTraceEntry> trace)
        {
            if (transientFailuresRemaining > 0)
            {
                transientFailuresRemaining--;
                trace.Add(new("Factory/Driver", VendorName, $"读取 {address} 遭遇瞬断 (剩余失败 {transientFailuresRemaining} 次)", "Warn"));
                return new(false, "ERR_TIMEOUT", 1, 20.0, "FINS/TCP 帧超时");
            }

            trace.Add(new("Factory/Driver", VendorName, $"读取 {address} (CDAB DM 区) 成功 → 186.50 °C", "Info"));
            return new(true, "186.50 °C", 1, 2.7, "FINS 00 00 OK");
        }
    }

    /// <summary>Factory Method encapsulating concrete PLC driver instantiation behind IPlcDriver.</summary>
    public static class PlcDriverFactory
    {
        public static IPlcDriver Create(PlcVendorKind vendor) => vendor switch
        {
            PlcVendorKind.SiemensS7 => new SiemensS7Driver(),
            PlcVendorKind.MitsubishiMc => new MitsubishiMcDriver(),
            _ => new OmronFinsDriver(),
        };

        public static string DefaultAddress(PlcVendorKind vendor) => vendor switch
        {
            PlcVendorKind.SiemensS7 => "DB1.DBD4",
            PlcVendorKind.MitsubishiMc => "D102",
            _ => "DM102",
        };
    }

    #endregion

    #region 2. Decorator Pattern (Transparent Cross-Cutting Wrappers)

    public abstract class PlcDriverDecoratorBase(IPlcDriver inner) : IPlcDriver
    {
        protected IPlcDriver Inner { get; } = inner;
        public virtual string VendorName => Inner.VendorName;
        public virtual string ProtocolBadge => Inner.ProtocolBadge;
        public abstract PlcReadOutcome ReadStationData(string address, ref int transientFailuresRemaining, List<PatternTraceEntry> trace);
    }

    public sealed class LoggingDriverDecorator(IPlcDriver inner) : PlcDriverDecoratorBase(inner)
    {
        public override PlcReadOutcome ReadStationData(string address, ref int transientFailuresRemaining, List<PatternTraceEntry> trace)
        {
            trace.Add(new("Decorator", "Logging ↳", $"审计拦截：开始调用 {Inner.VendorName}.Read({address})", "Info"));
            var result = Inner.ReadStationData(address, ref transientFailuresRemaining, trace);
            trace.Add(new("Decorator", "Logging ↲",
                string.Create(CultureInfo.InvariantCulture, $"审计记录：结果={(result.IsSuccess ? "OK" : "FAIL")}，值={result.ValueText}，耗时 {result.ElapsedMs:F1} ms"),
                result.IsSuccess ? "Info" : "Error"));
            return result with { ElapsedMs = result.ElapsedMs + 0.3 };
        }
    }

    public sealed class RetryDriverDecorator(IPlcDriver inner, int maxAttempts = 3) : PlcDriverDecoratorBase(inner)
    {
        private readonly int _maxAttempts = Math.Max(1, maxAttempts);

        public override PlcReadOutcome ReadStationData(string address, ref int transientFailuresRemaining, List<PatternTraceEntry> trace)
        {
            double totalMs = 0;
            PlcReadOutcome last = new(false, "ERR", 0, 0, "未执行");

            for (var attempt = 1; attempt <= _maxAttempts; attempt++)
            {
                last = Inner.ReadStationData(address, ref transientFailuresRemaining, trace);
                totalMs += last.ElapsedMs;
                if (last.IsSuccess)
                {
                    if (attempt > 1)
                    {
                        trace.Add(new("Decorator", "Retry ✓", $"第 {attempt} 次重试恢复成功（透明屏蔽网络抖动）", "Warn"));
                    }

                    return last with { Attempts = attempt, ElapsedMs = totalMs };
                }

                if (attempt < _maxAttempts)
                {
                    var backoffMs = attempt * 15;
                    totalMs += backoffMs;
                    trace.Add(new("Decorator", "Retry ↻", $"第 {attempt} 次失败，退避 {backoffMs} ms 后触发第 {attempt + 1} 次重试…", "Warn"));
                }
            }

            trace.Add(new("Decorator", "Retry ✕", $"已达最大重试次数 ({_maxAttempts})，向上抛出故障", "Error"));
            return last with { Attempts = _maxAttempts, ElapsedMs = totalMs };
        }
    }

    public sealed class CacheFallbackDriverDecorator(IPlcDriver inner, string cachedSnapshot = "186.10 °C (缓存降级)") : PlcDriverDecoratorBase(inner)
    {
        public override PlcReadOutcome ReadStationData(string address, ref int transientFailuresRemaining, List<PatternTraceEntry> trace)
        {
            var result = Inner.ReadStationData(address, ref transientFailuresRemaining, trace);
            if (result.IsSuccess) return result;

            trace.Add(new("Decorator", "CacheFallback", $"底层通讯失败，熔断降级返回最近有效快照：{cachedSnapshot}", "Warn"));
            return new(true, cachedSnapshot, result.Attempts, result.ElapsedMs + 0.2, "缓存降级放行");
        }
    }

    #endregion

    #region 3. Chain of Responsibility Pattern (Workpiece Station Quality & MES Pipeline)

    public sealed class WorkpieceInspectionContext
    {
        public required string SerialNumber { get; init; }
        public required string PlcTemperatureText { get; init; }
        public bool PlcReadSucceeded { get; init; }
        public bool DuplicateBarcodeFault { get; init; }
        public bool DimensionOutOfSpecFault { get; init; }
        public bool SurfaceScratchFault { get; init; }
        public bool MesOfflineFault { get; init; }

        public bool IsPassed { get; set; } = true;
        public string? BlockedByStage { get; set; }
        public string? FailureReason { get; set; }
        public List<PipelineVisualNode> ChainNodes { get; } = [];
        public List<PatternTraceEntry> Trace { get; } = [];
    }

    public interface IStationCheckHandler
    {
        string StageName { get; }
        IStationCheckHandler SetNext(IStationCheckHandler next);
        void Handle(WorkpieceInspectionContext context);
    }

    public abstract class StationCheckHandlerBase : IStationCheckHandler
    {
        private IStationCheckHandler? _next;
        public abstract string StageName { get; }

        public IStationCheckHandler SetNext(IStationCheckHandler next)
        {
            _next = next;
            return next;
        }

        public void Handle(WorkpieceInspectionContext context)
        {
            if (!context.IsPassed)
            {
                context.ChainNodes.Add(new("Chain", StageName, "前级已拦截·跳过", PipelineNodeState.Skipped));
                _next?.Handle(context);
                return;
            }

            if (Evaluate(context, out var okDetail, out var failReason))
            {
                context.ChainNodes.Add(new("Chain", StageName, okDetail, PipelineNodeState.Passed));
                context.Trace.Add(new("Chain", StageName, $"放行：{okDetail}", "Info"));
                _next?.Handle(context);
            }
            else
            {
                context.IsPassed = false;
                context.BlockedByStage = StageName;
                context.FailureReason = failReason;
                context.ChainNodes.Add(new("Chain", StageName, failReason, PipelineNodeState.Blocked));
                context.Trace.Add(new("Chain", StageName, $"短路拦截：{failReason}（后续环节自动跳过）", "Error"));
                _next?.Handle(context);
            }
        }

        protected abstract bool Evaluate(WorkpieceInspectionContext context, out string okDetail, out string failReason);
    }

    public sealed class BarcodeDedupHandler : StationCheckHandlerBase
    {
        public override string StageName => "1. 条码防重";

        protected override bool Evaluate(WorkpieceInspectionContext ctx, out string okDetail, out string failReason)
        {
            if (ctx.DuplicateBarcodeFault)
            {
                okDetail = string.Empty;
                failReason = $"SN {ctx.SerialNumber} 重复过站";
                return false;
            }

            okDetail = $"SN {ctx.SerialNumber} 唯一";
            failReason = string.Empty;
            return true;
        }
    }

    public sealed class DimensionToleranceHandler : StationCheckHandlerBase
    {
        public override string StageName => "2. 尺寸公差";

        protected override bool Evaluate(WorkpieceInspectionContext ctx, out string okDetail, out string failReason)
        {
            if (ctx.DimensionOutOfSpecFault)
            {
                okDetail = string.Empty;
                failReason = "实测 ±0.08mm 超出 ±0.03mm";
                return false;
            }

            okDetail = "偏差 +0.012mm OK";
            failReason = string.Empty;
            return true;
        }
    }

    public sealed class CosmeticVisionHandler : StationCheckHandlerBase
    {
        public override string StageName => "3. 外观复检";

        protected override bool Evaluate(WorkpieceInspectionContext ctx, out string okDetail, out string failReason)
        {
            if (ctx.SurfaceScratchFault)
            {
                okDetail = string.Empty;
                failReason = "检出 1.4mm 表面划痕";
                return false;
            }

            okDetail = "无划痕/异物 OK";
            failReason = string.Empty;
            return true;
        }
    }

    public sealed class MesUploadHandler : StationCheckHandlerBase
    {
        public override string StageName => "4. MES 过站";

        protected override bool Evaluate(WorkpieceInspectionContext ctx, out string okDetail, out string failReason)
        {
            if (!ctx.PlcReadSucceeded)
            {
                okDetail = string.Empty;
                failReason = "缺少 PLC 工艺参数，禁止过站";
                return false;
            }

            if (ctx.MesOfflineFault)
            {
                okDetail = string.Empty;
                failReason = "MES 接口 503，已入本地断网缓存";
                return false;
            }

            okDetail = $"已绑定 {ctx.PlcTemperatureText}";
            failReason = string.Empty;
            return true;
        }
    }

    #endregion

    public sealed record PipelineExecutionResult(
        string DriverName,
        int DecoratorCount,
        PlcReadOutcome PlcOutcome,
        bool OverallPassed,
        string FinalVerdict,
        IReadOnlyList<PipelineVisualNode> VisualNodes,
        IReadOnlyList<PatternTraceEntry> Trace);

    /// <summary>Composes Factory + Decorator + Chain of Responsibility into one executable station run.</summary>
    public static class StationPipelineRunner
    {
        public static PipelineExecutionResult Execute(
            PlcVendorKind vendor,
            bool enableLogging,
            bool enableRetry,
            bool enableCacheFallback,
            int transientFailures,
            bool duplicateBarcode,
            bool dimensionFault,
            bool scratchFault,
            bool mesOffline,
            int batchSeq)
        {
            var trace = new List<PatternTraceEntry>();
            var visualNodes = new List<PipelineVisualNode>();

            // Step 1: Factory creates concrete PLC driver behind IPlcDriver
            var baseDriver = PlcDriverFactory.Create(vendor);
            var address = PlcDriverFactory.DefaultAddress(vendor);
            trace.Add(new("Factory", "PlcDriverFactory", $"按配置创建 {baseDriver.VendorName} ({baseDriver.ProtocolBadge})", "Info"));

            // Step 2: Dynamically wrap with selected Decorators (inner -> outer)
            IPlcDriver driver = baseDriver;
            var decNames = new List<string>();
            if (enableRetry)
            {
                driver = new RetryDriverDecorator(driver, maxAttempts: 3);
                decNames.Add("Retry(×3)");
            }

            if (enableCacheFallback)
            {
                driver = new CacheFallbackDriverDecorator(driver);
                decNames.Add("CacheFallback");
            }

            if (enableLogging)
            {
                driver = new LoggingDriverDecorator(driver);
                decNames.Add("Logging");
            }

            var remainingFails = Math.Clamp(transientFailures, 0, 5);
            var plcOutcome = driver.ReadStationData(address, ref remainingFails, trace);

            var driverState = !plcOutcome.IsSuccess
                ? PipelineNodeState.Blocked
                : (plcOutcome.Attempts > 1 || plcOutcome.Detail.Contains("降级", StringComparison.Ordinal))
                    ? PipelineNodeState.Recovered
                    : PipelineNodeState.Passed;

            visualNodes.Add(new("Factory", baseDriver.VendorName, baseDriver.ProtocolBadge, driverState));
            visualNodes.Add(new("Decorator",
                decNames.Count == 0 ? "无装饰器直连" : string.Join(" + ", decNames),
                plcOutcome.IsSuccess ? $"{plcOutcome.ValueText} ({plcOutcome.Attempts}次)" : plcOutcome.Detail,
                driverState));

            // Step 3: Build & execute Chain of Responsibility
            var h1 = new BarcodeDedupHandler();
            var h2 = new DimensionToleranceHandler();
            var h3 = new CosmeticVisionHandler();
            var h4 = new MesUploadHandler();
            h1.SetNext(h2).SetNext(h3).SetNext(h4);

            var ctx = new WorkpieceInspectionContext
            {
                SerialNumber = $"PCB-{batchSeq:D4}",
                PlcTemperatureText = plcOutcome.ValueText,
                PlcReadSucceeded = plcOutcome.IsSuccess,
                DuplicateBarcodeFault = duplicateBarcode,
                DimensionOutOfSpecFault = dimensionFault,
                SurfaceScratchFault = scratchFault,
                MesOfflineFault = mesOffline,
            };

            h1.Handle(ctx);
            visualNodes.AddRange(ctx.ChainNodes);
            trace.AddRange(ctx.Trace);

            var verdict = ctx.IsPassed
                ? $"PASS · 工件 {ctx.SerialNumber} 全流程放行（{plcOutcome.ValueText}）"
                : $"NG · 在 [{ctx.BlockedByStage}] 拦截：{ctx.FailureReason}";

            return new PipelineExecutionResult(
                baseDriver.VendorName, decNames.Count, plcOutcome, ctx.IsPassed, verdict, visualNodes, trace);
        }
    }
}
