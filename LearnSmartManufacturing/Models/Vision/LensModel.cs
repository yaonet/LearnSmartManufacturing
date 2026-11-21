using System.Globalization;

namespace LearnSmartManufacturing.Models.Vision
{
    public enum LensMountKind
    {
        CsMount,
        CMount,
        FMount,
        M42Mount,
    }

    public sealed record LensMountSpec(
        LensMountKind Kind,
        string Name,
        double FlangeFocalMm,
        string ThreadSpec,
        string TypicalTarget);

    public sealed record MountMatchResult(
        bool IsCompatible,
        bool NeedsAdapter,
        double AdapterThicknessMm,
        string StatusBadge,
        string Explanation);

    public sealed record ExtensionTubeResult(
        bool IsValid,
        double FocalLengthMm,
        double TargetWdMm,
        double Magnification,
        double ExtensionMm,
        double EffectiveFNumber,
        string Summary);

    public sealed record TelecentricComparisonResult(
        double NominalDiameterMm,
        double DeltaZMm,
        double WorkingDistanceMm,
        double TelecentricityDeg,
        double FaMeasuredMm,
        double FaErrorMm,
        double FaErrorPercent,
        double TcMeasuredMm,
        double TcErrorMm,
        double TcErrorPercent,
        bool FaWithinTolerance,
        bool TcWithinTolerance,
        string Summary);

    public sealed record DistortionSimulationResult(
        double DistortionPercent,
        int SensorResX,
        double EdgeShiftPx,
        string DistortionKindText,
        bool UndistortApplied,
        string Verdict);

    public sealed record LensCatalogRow(
        double FocalLengthMm,
        string Category,
        double FovWidthMm,
        double FovAngleDeg,
        double TypicalDistortionPercent,
        bool IsNearestRecommended)
    {
        public string FocalText => $"{FocalLengthMm:0} mm";
        public string FovText => $"{FovWidthMm:F1} mm";
        public string AngleText => $"{FovAngleDeg:F1}°";
        public string DistortionText => $"{TypicalDistortionPercent:F2}%";
    }

    public sealed record LensScenarioPreset(
        string Name,
        string Tag,
        double FocalLengthMm,
        double WorkingDistanceMm,
        double SensorWidthMm,
        double TargetFovMm,
        double DemoDeltaZMm,
        double DemoDistortionPct,
        string Note);

    /// <summary>
    /// Industrial lens engineering calculations: C/CS/F/M42 flange focal distance compatibility,
    /// macro extension tube (近摄接圈) sizing, FA vs Telecentric perspective error, and radial distortion.
    /// </summary>
    public static class LensEngineeringEngine
    {
        public static readonly double[] StandardFocalLengths = [6, 8, 12, 16, 25, 35, 50, 75, 100];

        public static IReadOnlyList<LensMountSpec> MountSpecs { get; } =
        [
            new(LensMountKind.CsMount, "CS 口 (12.526 mm)", 12.526, "1″-32 TPI 螺纹", "≤ 1/2″ 小靶面经济型相机"),
            new(LensMountKind.CMount, "C 口 (17.526 mm)", 17.526, "1″-32 TPI 螺纹", "1/3″ ~ 1.1″ 主流工业面阵"),
            new(LensMountKind.FMount, "F 口 (46.500 mm)", 46.500, "尼康卡口 (Ø44 mm)", "APS-C / 全画幅高像素与线阵"),
            new(LensMountKind.M42Mount, "M42 口 (45.460 mm)", 45.460, "M42×1 螺纹", "大靶面 25MP~150MP / 8K~16K 线阵"),
        ];

        public static IReadOnlyList<LensScenarioPreset> ScenarioPresets { get; } =
        [
            new("标准 2/3″ 工业定位", "16mm · WD 300", 16, 300, 8.45, 160, 8, -0.8,
                "最均衡的 16mm 定焦配置：畸变低 (<1%)、视野 158mm，适合常规 3C 装配与二维码定位。"),
            new("紧凑空间大视野", "8mm 广角 · 桶形畸变", 8, 220, 8.45, 232, 10, -4.2,
                "短工作距离拍大视野必须用 8mm 广角镜头，但边缘会产生明显桶形畸变，高精度测量前须做张正友标定！"),
            new("远距离细节检测", "50mm 长焦 · 低畸变", 50, 450, 8.45, 76, 6, 0.35,
                "50mm 长焦将视场角压缩至 9.7°，在 450mm 安全距下拍 76mm 局部，畸变极低但工作距离要求长。"),
            new("高精度柱体尺寸测量", "远心零透视对比", 25, 200, 8.45, 68, 12, -0.08,
                "工件高度波动 ±12 mm 时，普通 FA 镜头产生 ±6% 巨大透视误差；换用远心镜头可将误差压至 0.03% 以内！"),
        ];

        /// <summary>Checks flange focal distance (法兰焦距) compatibility between camera body mount and lens mount.</summary>
        public static MountMatchResult EvaluateMountCompatibility(LensMountKind cameraMount, LensMountKind lensMount)
        {
            var cam = MountSpecs.Single(m => m.Kind == cameraMount);
            var lens = MountSpecs.Single(m => m.Kind == lensMount);
            var diff = Math.Round(lens.FlangeFocalMm - cam.FlangeFocalMm, 3);

            if (Math.Abs(diff) < 0.01)
            {
                return new MountMatchResult(true, false, 0, "✓ 原生直连",
                    $"{lens.Name} 与 {cam.Name} 法兰焦距完全一致（{cam.FlangeFocalMm:F3} mm），直接旋入即可正常合焦。");
            }

            if (diff > 0)
            {
                var ringHint = cameraMount == LensMountKind.CsMount && lensMount == LensMountKind.CMount
                    ? "标准 5 mm C/CS 转接环"
                    : $"{diff:F2} mm 专用法兰转接筒";
                return new MountMatchResult(true, true, diff, $"✓ 需加 {diff:F2} mm 转接环",
                    $"镜头法兰距 ({lens.FlangeFocalMm:F3} mm) > 相机法兰距 ({cam.FlangeFocalMm:F3} mm)，加装 {ringHint} 即可精确合焦！");
            }

            return new MountMatchResult(false, false, diff, $"✕ 无法合焦 (短 {-diff:F2} mm)",
                $"致命反配：镜头法兰距 ({lens.FlangeFocalMm:F3} mm) < 相机法兰距 ({cam.FlangeFocalMm:F3} mm)！无法削薄机身，远处目标永远模糊无法合焦（经典面试坑题：CS 镜头不能配 C 口相机）。");
        }

        /// <summary>Computes required C-mount extension tube thickness for close-up macro work below MOD.</summary>
        public static ExtensionTubeResult EvaluateExtensionTube(double focalLengthMm, double targetWdMm, double nominalFNumber = 4.0)
        {
            if (targetWdMm <= focalLengthMm * 1.15)
            {
                return new ExtensionTubeResult(false, focalLengthMm, targetWdMm, 0, 0, nominalFNumber,
                    $"目标物距 WD ({targetWdMm:0} mm) 过于接近焦距 f ({focalLengthMm:0} mm)，超出常规薄透镜近摄接圈实用范围。");
            }

            // Thin lens: 1/u + 1/v = 1/f => v = f*u/(u - f), extension beyond infinity focus = v - f = f^2 / (u - f)
            var ext = (focalLengthMm * focalLengthMm) / (targetWdMm - focalLengthMm);
            var beta = ext / focalLengthMm;
            var effF = nominalFNumber * (1.0 + beta);

            var summary = string.Create(CultureInfo.InvariantCulture,
                $"需加装约 {ext:F1} mm 延长接圈（放大倍率 β={beta:F2}×）：有效光圈由 F{nominalFNumber:F1} 变暗为 F{effF:F1}，且丧失无穷远对焦能力。");

            return new ExtensionTubeResult(true, focalLengthMm, targetWdMm, Math.Round(beta, 3), Math.Round(ext, 2), Math.Round(effF, 2), summary);
        }

        /// <summary>Compares perspective measurement error between standard entocentric FA lens and object-space telecentric lens.</summary>
        public static TelecentricComparisonResult EvaluateTelecentricVsFa(
            double nominalDiameterMm,
            double deltaZMm,
            double workingDistanceMm,
            double toleranceMm = 0.20,
            double telecentricityDeg = 0.08)
        {
            var d = Math.Max(1.0, nominalDiameterMm);
            var wd = Math.Max(40.0, workingDistanceMm);
            // Positive deltaZ = closer to lens (object rises up toward lens -> looks LARGER in FA lens!)
            var effectiveWd = Math.Max(15.0, wd - deltaZMm);
            var faMeasured = d * (wd / effectiveWd);
            var faErrMm = faMeasured - d;
            var faErrPct = (faErrMm / d) * 100.0;

            // Telecentric lens residual error caused only by tiny telecentricity angle (< 0.1 deg)
            var teleRad = telecentricityDeg * Math.PI / 180.0;
            var tcErrMm = 2.0 * deltaZMm * Math.Tan(teleRad);
            var tcMeasured = d + tcErrMm;
            var tcErrPct = (tcErrMm / d) * 100.0;

            var faOk = Math.Abs(faErrMm) <= toleranceMm;
            var tcOk = Math.Abs(tcErrMm) <= toleranceMm;

            var summary = Math.Abs(deltaZMm) < 0.05
                ? "当前高度波动 ΔZ = 0 mm：两类镜头在标定基准面测量一致，请拖动 ΔZ 滑块模拟工件厚度/高低起伏。"
                : $"工件起伏 ΔZ={deltaZMm:+0.0;-0.0;0.0} mm：普通 FA 镜头因近大远小产生 {faErrMm:+0.00;-0.00;0.00} mm ({faErrPct:+0.00;-0.00;0.00}%) 透视误差；远心镜头平行光路误差仅 {tcErrMm:+0.000;-0.000;0.000} mm！";

            return new TelecentricComparisonResult(
                d, deltaZMm, wd, telecentricityDeg,
                Math.Round(faMeasured, 3), Math.Round(faErrMm, 3), Math.Round(faErrPct, 2),
                Math.Round(tcMeasured, 3), Math.Round(tcErrMm, 4), Math.Round(tcErrPct, 3),
                faOk, tcOk, summary);
        }

        /// <summary>Evaluates radial distortion edge displacement in pixels before/after camera calibration undistort.</summary>
        public static DistortionSimulationResult EvaluateDistortion(double distortionPercent, int sensorResX, bool undistortApplied)
        {
            var rawPct = Math.Clamp(distortionPercent, -8.0, 8.0);
            var effPct = undistortApplied ? rawPct * 0.04 : rawPct; // Zhang calibration reduces residual < 0.05-0.2px
            var halfWidthPx = Math.Max(100, sensorResX) * 0.5;
            var edgeShiftPx = Math.Abs(effPct / 100.0) * halfWidthPx;

            var kind = Math.Abs(rawPct) < 0.15
                ? "近零畸变 (远心/双高斯高端镜头)"
                : rawPct < 0 ? $"桶形畸变 Barrel ({rawPct:F2}% · 常见于广角镜头)" : $"枕形畸变 Pincushion (+{rawPct:F2}% · 常见于长焦镜头)";

            var verdict = undistortApplied
                ? $"已启用张正友棋盘格标定去畸变 (k1/k2 逆映射)：边缘残留误差从 {Math.Abs(rawPct / 100.0) * halfWidthPx:F1} px 降至 {edgeShiftPx:F2} px！"
                : edgeShiftPx > 3.0
                    ? $"画面边缘产生 {edgeShiftPx:F1} px 径向偏移！直接用于尺寸测量会产生严重边缘误差，需换低畸变镜头或执行畸变标定校正。"
                    : $"边缘畸变偏移 {edgeShiftPx:F2} px，处于较低水平，适合高精度视觉测量。";

            return new DistortionSimulationResult(rawPct, sensorResX, Math.Round(edgeShiftPx, 2), kind, undistortApplied, verdict);
        }

        public static IReadOnlyList<LensCatalogRow> BuildStandardLensCatalog(double wdMm, double sensorWidthMm, double? targetFocalMm)
        {
            var nearest = targetFocalMm is double tf && tf > 0
                ? StandardFocalLengths.MinBy(f => Math.Abs(f - tf))
                : (double?)null;

            var list = new List<LensCatalogRow>(StandardFocalLengths.Length);
            foreach (var f in StandardFocalLengths)
            {
                var opt = OpticsCalculator.Compute(f, wdMm, sensorWidthMm);
                var category = f switch
                {
                    <= 8 => "超广角/广角",
                    <= 16 => "标准广角",
                    <= 35 => "标准/中焦",
                    _ => "长焦远摄",
                };
                var typicalDist = f switch
                {
                    6 => -4.80,
                    8 => -3.20,
                    12 => -1.50,
                    16 => -0.75,
                    25 => -0.25,
                    35 => -0.15,
                    50 => 0.18,
                    75 => 0.35,
                    _ => 0.45,
                };
                list.Add(new LensCatalogRow(f, category, Math.Round(opt.FovWidth, 1), Math.Round(opt.FovAngleDeg, 1),
                    typicalDist, nearest is double n && Math.Abs(n - f) < 1e-6));
            }

            return list;
        }
    }
}
