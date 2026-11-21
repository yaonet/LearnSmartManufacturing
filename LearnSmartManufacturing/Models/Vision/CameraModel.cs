using System.Globalization;

namespace LearnSmartManufacturing.Models.Vision
{
    public enum PixelFormatKind
    {
        Mono8,
        Mono12Packed,
        Mono12Unpacked,
        Rgb8,
    }

    public enum ShutterKind
    {
        Global,
        Rolling,
    }

    public sealed record IndustrialCameraSpec(
        string Name,
        string SensorModel,
        int Width,
        int Height,
        double PixelSizeUm,
        string SensorFormat,
        double SensorWidthMm,
        double SensorHeightMm,
        int MaxFps,
        ShutterKind Shutter,
        string PrimaryInterface)
    {
        public double Megapixels => Width * (double)Height / 1_000_000.0;
        public double DiagonalMm => double.Hypot(SensorWidthMm, SensorHeightMm);
        public string ResolutionText => $"{Width} × {Height}";
        public string ShutterText => Shutter == ShutterKind.Global ? "Global 全局" : "Rolling 卷帘";
        public string PhysicalSpecText => $"{SensorFormat} ({SensorWidthMm:F1}×{SensorHeightMm:F1}mm, {PixelSizeUm:F2}µm)";
    }

    public sealed record LensImageCircleSpec(string Name, double DiameterMm);

    public sealed record CameraScenarioPreset(
        string Name,
        string Tag,
        string PixelAccuracyText,
        string TargetLengthText,
        string TargetWidthText,
        double FovMarginPercent,
        string RecommendedLensCircleName,
        double DemoLineSpeedMmPerS,
        double DemoFps,
        string Note);

    public sealed record CameraSelectionVisualData(
        bool IsValid,
        bool ExceedsAreaScan,
        long RequiredNx,
        long RequiredNy,
        double EffectiveLengthMm,
        double EffectiveWidthMm,
        double RawLengthMm,
        double RawWidthMm,
        double FovMarginPercent,
        IndustrialCameraSpec? RecommendedCamera,
        bool IsRotatedMatch,
        double SensorUtilizationPercent,
        LensImageCircleSpec SelectedLensCircle,
        bool HasVignetting,
        double VignettingRatio,
        double LineScanSuggestedRateKHz);

    public sealed record InterfaceBandwidthRow(
        string Name,
        double MaxEffectiveMBps,
        double LoadPercent,
        bool IsSupported,
        string EngineeringNote)
    {
        public string CeilingText => $"{MaxEffectiveMBps:0} MB/s";
        public string LoadText => LoadPercent > 999 ? ">999%" : $"{LoadPercent:F1}%";
        public double ClampedLoadPercent => Math.Clamp(LoadPercent, 0, 100);
        public string StatusText => IsSupported
            ? (LoadPercent > 80 ? "可用 (余量偏紧)" : "✓ 充裕")
            : "✕ 带宽超载/丢帧";
    }

    public sealed record BandwidthEvaluation(
        int Width,
        int Height,
        PixelFormatKind Format,
        double BytesPerPixel,
        double Fps,
        double RequiredMBps,
        double RequiredGbps,
        string RecommendedInterface,
        string FormulaText,
        IReadOnlyList<InterfaceBandwidthRow> Interfaces);

    public sealed record ShutterSimulationResult(
        double LineSpeedMmPerS,
        double ExposureUs,
        double RollingReadoutMs,
        double PixelAccuracyMm,
        double MotionBlurPx,
        double RollingSkewPx,
        double RollingSkewAngleDeg,
        bool IsRollingAcceptable,
        string GlobalSummary,
        string RollingSummary);

    /// <summary>
    /// Industrial camera engineering calculator covering standard CMOS sensor catalog matching,
    /// lens image-circle vignetting verification, Global vs Rolling shutter motion distortion,
    /// and GigE/USB3/5GigE/10GigE/CoaXPress bandwidth budgeting.
    /// </summary>
    public static class CameraSelectionEngine
    {
        public static IReadOnlyList<IndustrialCameraSpec> StandardCameras { get; } =
        [
            new("130 万", "IMX273 / Python1300", 1280, 1024, 4.80, "1/2″", 6.14, 4.92, 210, ShutterKind.Global, "GigE / USB3"),
            new("200 万", "IMX265-Crop", 1600, 1200, 4.50, "1/1.8″", 7.20, 5.40, 120, ShutterKind.Global, "GigE / USB3"),
            new("230 万", "Sony IMX249", 1920, 1200, 5.86, "1/1.2″", 11.25, 7.03, 41, ShutterKind.Global, "GigE / USB3"),
            new("320 万", "Sony IMX265", 2048, 1536, 3.45, "1/1.8″", 7.07, 5.30, 55, ShutterKind.Global, "GigE / USB3"),
            new("500 万", "Sony IMX250", 2448, 2048, 3.45, "2/3″", 8.45, 7.07, 75, ShutterKind.Global, "GigE / USB3"),
            new("630 万", "Sony IMX178", 3072, 2048, 2.40, "1/1.8″", 7.37, 4.92, 59, ShutterKind.Rolling, "USB3 / GigE"),
            new("1200 万", "Sony IMX304", 4096, 3000, 3.45, "1.1″", 14.13, 10.35, 23, ShutterKind.Global, "USB3 / 5GigE"),
            new("2000 万", "Sony IMX183", 5472, 3648, 2.40, "1″", 13.13, 8.76, 18, ShutterKind.Rolling, "USB3 / 5GigE"),
            new("2500 万", "Gpixel GMAX0505", 5328, 4608, 2.50, "1.1″", 13.32, 11.52, 42, ShutterKind.Global, "10GigE / CXP"),
            new("5000 万", "Gpixel GMAX3265", 8192, 6144, 3.20, "APS-H", 26.21, 19.66, 22, ShutterKind.Global, "10GigE / CXP"),
        ];

        public static IReadOnlyList<LensImageCircleSpec> LensImageCircles { get; } =
        [
            new("1/3″ 镜头 (Ø 6.0 mm)", 6.0),
            new("1/2″ 镜头 (Ø 8.0 mm)", 8.0),
            new("1/1.8″ 镜头 (Ø 9.0 mm)", 9.0),
            new("2/3″ 镜头 (Ø 11.0 mm)", 11.0),
            new("1″ 镜头 (Ø 16.0 mm)", 16.0),
            new("1.1″ 镜头 (Ø 17.6 mm)", 17.6),
            new("APS-C 镜头 (Ø 28.4 mm)", 28.4),
            new("全画幅 / F口 (Ø 43.3 mm)", 43.3),
        ];

        public static IReadOnlyList<CameraScenarioPreset> ScenarioPresets { get; } =
        [
            new("标准教材算例", "500 万 · 2/3″", "0.05", "100", "80", 0, "2/3″ 镜头 (Ø 11.0 mm)", 400, 30,
                "100×80 mm @ 0.05 mm/px 理论需 2000×1600 px (3.20 MP)，推荐 500 万 (2448×2048, 2/3″ IMX250)。"),
            new("3C 手机中框精密定位", "1200 万 · 1.1″", "0.04", "150", "90", 10, "1.1″ 镜头 (Ø 17.6 mm)", 250, 20,
                "加入 10% 上料公差余量后需 4125×2475 px；若改用 2/3″ 小像圆镜头会立即出现严重四角暗角！"),
            new("高速飞拍瓶盖/电芯", "130 万 · Global", "0.10", "110", "85", 10, "1/2″ 镜头 (Ø 8.0 mm)", 1200, 120,
                "高速产线 (1200 mm/s) 必须选用 Global 全局快门 + USB3/5GigE 高帧率，卷帘快门会产生严重斜切畸变。"),
            new("大板 PCB AOI 焊点", "2500 万 · 10GigE", "0.05", "240", "200", 10, "1.1″ 镜头 (Ø 17.6 mm)", 300, 35,
                "高像素 (25MP) + 35 fps 带宽达 859 MB/s，远超千兆网 GigE 上限，需选用 10GigE 或 CoaXPress 接口。"),
            new("宽幅极片连续检测", "超面阵 → 线阵", "0.01", "200", "100", 0, "全画幅 / F口 (Ø 43.3 mm)", 800, 60,
                "需 20000×10000 px (200 MP) 超出常规面阵，工业现场应改用 16K/20K 线阵相机 (行频 80 kHz) 或多相机拼接。"),
            new("小像圆暗角反例", "2/3″ 相机配 1/2″ 镜头", "0.05", "100", "80", 0, "1/2″ 镜头 (Ø 8.0 mm)", 400, 30,
                "经典面试坑题：IMX250 2/3″ 靶面对角线 Ø 11.0 mm > 1/2″ 镜头像圆 Ø 8.0 mm，成像四角出现明显暗角黑边！"),
        ];

        public static double GetBytesPerPixel(PixelFormatKind fmt) => fmt switch
        {
            PixelFormatKind.Mono8 => 1.0,
            PixelFormatKind.Mono12Packed => 1.5,
            PixelFormatKind.Mono12Unpacked => 2.0,
            PixelFormatKind.Rgb8 => 3.0,
            _ => 1.0,
        };

        public static string FormatPixelFormat(PixelFormatKind fmt) => fmt switch
        {
            PixelFormatKind.Mono8 => "Mono8 (1 B/px · 黑白常用)",
            PixelFormatKind.Mono12Packed => "Mono12 Packed (1.5 B/px · 高动态)",
            PixelFormatKind.Mono12Unpacked => "Mono12/16 (2 B/px · 高比特)",
            PixelFormatKind.Rgb8 => "RGB8 (3 B/px · 彩色三通道)",
            _ => fmt.ToString(),
        };

        public static CameraSelectionVisualData EvaluateSelection(
            double accuracyMm,
            double rawLengthMm,
            double rawWidthMm,
            double fovMarginPercent,
            LensImageCircleSpec lensCircle,
            double lineSpeedMmPerS = 500)
        {
            var marginScale = 1.0 + Math.Clamp(fovMarginPercent, 0, 50) / 100.0;
            var effLen = rawLengthMm * marginScale;
            var effWid = rawWidthMm * marginScale;

            var nxDouble = Math.Ceiling(effLen / accuracyMm);
            var nyDouble = Math.Ceiling(effWid / accuracyMm);
            if (!double.IsFinite(nxDouble) || !double.IsFinite(nyDouble) || nxDouble > int.MaxValue || nyDouble > int.MaxValue)
            {
                return new CameraSelectionVisualData(false, false, 0, 0, effLen, effWid, rawLengthMm, rawWidthMm,
                    fovMarginPercent, null, false, 0, lensCircle, false, 1.0, 0);
            }

            var nx = (long)nxDouble;
            var ny = (long)nyDouble;

            // Accept rotated sensor mounting (same rule as existing CameraViewModel)
            var match = StandardCameras.FirstOrDefault(c =>
                (c.Width >= nx && c.Height >= ny) || (c.Width >= ny && c.Height >= nx));

            if (match is null)
            {
                var lineRateKHz = (lineSpeedMmPerS / Math.Max(0.001, accuracyMm)) / 1000.0;
                return new CameraSelectionVisualData(true, true, nx, ny, effLen, effWid, rawLengthMm, rawWidthMm,
                    fovMarginPercent, null, false, 100.0, lensCircle, false, 1.0, Math.Round(lineRateKHz, 1));
            }

            var fitsNormal = match.Width >= nx && match.Height >= ny;
            var rotated = !fitsNormal;
            var utilization = Math.Clamp((nx * (double)ny) / (match.Width * (double)match.Height) * 100.0, 0, 100);
            var diag = match.DiagonalMm;
            // Allow tiny 0.2mm tolerance on nominal 2/3" (11.02mm vs 11.0mm)
            var hasVignetting = lensCircle.DiameterMm + 0.25 < diag;
            var ratio = lensCircle.DiameterMm / Math.Max(0.1, diag);

            return new CameraSelectionVisualData(true, false, nx, ny, effLen, effWid, rawLengthMm, rawWidthMm,
                fovMarginPercent, match, rotated, Math.Round(utilization, 1), lensCircle, hasVignetting, ratio, 0);
        }

        public static BandwidthEvaluation EvaluateBandwidth(int width, int height, PixelFormatKind format, double fps)
        {
            var w = Math.Max(1, width);
            var h = Math.Max(1, height);
            var f = Math.Max(0.1, fps);
            var bpp = GetBytesPerPixel(format);

            var mbPerSec = (w * (double)h * bpp * f) / 1_000_000.0;
            var gbps = mbPerSec * 8.0 / 1000.0;

            var specs = new (string Name, double EffectiveMBps, string Note)[]
            {
                ("GigE Vision (1 Gbps)", 115.0, "线长 ≤100m · 支持 PoE · 成本最低 (实际有效 ~115 MB/s)"),
                ("USB3 Vision (5 Gbps)", 380.0, "即插即用 · 线长 ≤3~5m · 适合近距高性价比机台"),
                ("5GigE Vision (5 Gbps)", 560.0, "兼容 Cat6A 网线 ≤100m · 性价比升级千兆网"),
                ("10GigE Vision (10 Gbps)", 1120.0, "支持万兆光/电口 · 适合 12MP~25MP 高速面阵与多机汇总"),
                ("CoaXPress CXP-12 (4-Lane)", 4800.0, "需专用采集卡 · 极低延迟高精度触发 · 旗舰超高速方案"),
            };

            var rows = new List<InterfaceBandwidthRow>(specs.Length);
            foreach (var (name, ceiling, note) in specs)
            {
                var load = (mbPerSec / ceiling) * 100.0;
                rows.Add(new InterfaceBandwidthRow(name, ceiling, Math.Round(load, 1), mbPerSec <= ceiling, note));
            }

            var rec = rows.FirstOrDefault(r => r.IsSupported)?.Name ?? "超出单卡 CXP-12，需多路分流或硬件 ROI 降帧";
            var formula = string.Create(CultureInfo.InvariantCulture,
                $"{w} × {h} × {bpp:0.#} B/px × {f:0.#} fps = {mbPerSec:F1} MB/s ({gbps:F2} Gbps)");

            return new BandwidthEvaluation(w, h, format, bpp, f, Math.Round(mbPerSec, 1), Math.Round(gbps, 2), rec, formula, rows);
        }

        public static ShutterSimulationResult EvaluateShutter(
            double lineSpeedMmPerS,
            double exposureUs,
            double rollingReadoutMs,
            double pixelAccuracyMm,
            double fovHeightMm)
        {
            var speed = Math.Max(0.0, lineSpeedMmPerS);
            var expUs = Math.Max(1.0, exposureUs);
            var readoutMs = Math.Max(1.0, rollingReadoutMs);
            var acc = Math.Max(0.001, pixelAccuracyMm);
            var fovH = Math.Max(1.0, fovHeightMm);

            // Motion blur during exposure (affects both Global and Rolling)
            var blurMm = speed * (expUs / 1_000_000.0);
            var blurPx = blurMm / acc;

            // Rolling shutter top-to-bottom horizontal skew across full frame readout
            var skewMm = speed * (readoutMs / 1000.0);
            var skewPx = skewMm / acc;
            var skewAngleDeg = Math.Atan2(skewMm, fovH) * (180.0 / Math.PI);

            var rollingOk = skewPx <= 1.0;
            var globalText = blurPx <= 1.0
                ? $"Global 全局快门：垂直无畸变 (Skew 0 px)，曝光拖影 {blurPx:F2} px ≤ 1 px，稳定成像。"
                : $"Global 全局快门：垂直无畸变，但曝光 {expUs:0} µs 过长导致水平拖影 {blurPx:F1} px，需缩短曝光或加频闪。";

            var rollingText = rollingOk
                ? $"Rolling 卷帘快门：低速下斜切偏移仅 {skewPx:F2} px ({skewAngleDeg:F1}°)，可用于静态/慢速低成本检测。"
                : $"Rolling 卷帘快门：逐行读出耗时 {readoutMs:F1} ms 造成严重果冻斜切 {skewPx:F1} px (倾角 {skewAngleDeg:F1}°)，测量彻底失效！";

            return new ShutterSimulationResult(
                speed, expUs, readoutMs, acc,
                Math.Round(blurPx, 2),
                Math.Round(skewPx, 2),
                Math.Round(skewAngleDeg, 1),
                rollingOk, globalText, rollingText);
        }
    }
}
