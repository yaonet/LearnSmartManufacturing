using System.Globalization;

namespace LearnSmartManufacturing.Models.Vision
{
    public enum LightGeometryType
    {
        HighAngleRing,
        LowAngleDarkField,
        Coaxial,
        Dome,
        Backlight,
        DualBar,
    }

    public enum LightWavelength
    {
        White,
        Red630,
        Green525,
        Blue470,
        Ir850,
        Uv365,
    }

    public enum WorkpieceType
    {
        MetalMirrorScratch,
        GreenPcbSilkscreen,
        StampedContour,
        CurvedSpecularMetal,
        WaferMicroPit,
        GlossyFilmLabel,
    }

    public enum TargetSurfaceColor
    {
        Red,
        Green,
        Blue,
        Yellow,
        White,
        DarkGray,
    }

    public sealed record LightingParameters(
        LightGeometryType LightType,
        WorkpieceType Workpiece,
        LightWavelength Wavelength,
        double IncidenceAngleDeg,
        double WorkingDistanceMm,
        bool UseCrossPolarizer,
        bool UseStrobeOverdrive,
        double ExposureGainPercent);

    public sealed record ColorPatchReading(
        TargetSurfaceColor SurfaceColor,
        string Name,
        string HexColor,
        double Reflectance,
        int MonochromeGray);

    public sealed record LightingSimulationResult(
        LightingParameters Parameters,
        bool IsDarkFieldMode,
        string IlluminationModeText,
        int BackgroundGray,
        int FeatureGray,
        double ContrastPercent,
        double GlareRiskPercent,
        double OpticalEfficiencyPercent,
        string QualityGrade,
        bool IsOptimalSetup,
        string EngineeringAdvice,
        IReadOnlyList<double> ScanlineProfile,
        IReadOnlyList<ColorPatchReading> ColorPatches);

    public sealed record StrobeCalculationResult(
        double MaxExposureUs,
        double ActualBlurAt1msPx,
        bool NeedsStrobeOverdrive,
        double RecommendedOverdriveFactor,
        double DutyCyclePercent,
        bool IsDutyCycleSafe,
        string Verdict);

    public sealed record LightingScenarioPreset(
        string Name,
        string ShortGoal,
        WorkpieceType Workpiece,
        LightGeometryType RecommendedLight,
        LightWavelength RecommendedWavelength,
        double RecommendedAngleDeg,
        bool RecommendedPolarizer,
        string PhysicsReason);

    public sealed record LightFixtureGuideRow(
        LightGeometryType LightType,
        string Name,
        string OpticalMode,
        string TypicalApplications,
        string KeyTradeoff);

    /// <summary>
    /// Pure physics-inspired machine vision illumination simulator covering:
    /// - Bright-field vs Dark-field geometric reflection &amp; angle thresholds
    /// - Coaxial beam-splitter loss (25%), Dome diffuse glare suppression, Backlight silhouettes
    /// - Cross-polarizer specular glare rejection (-65% throughput)
    /// - Wavelength spectral reflectance (same-color bright, complementary-color dark, IR penetration, UV/Blue scratch scatter)
    /// - Flying-capture strobe pulse-width &amp; duty-cycle calculator
    /// </summary>
    public static class LightingEngine
    {
        public static IReadOnlyList<LightingScenarioPreset> Presets { get; } =
        [
            new(
                "金属镜面微划痕", "暗场低角 + 蓝光",
                WorkpieceType.MetalMirrorScratch, LightGeometryType.LowAngleDarkField, LightWavelength.Blue470, 20, false,
                "低角度掠射使镜面背景反射光偏离镜头呈纯黑，划痕斜面散射短波长蓝光进入镜头形成极高信噪比。"),
            new(
                "PCB 绿板丝印与红标", "明场环形 + 红光互补",
                WorkpieceType.GreenPcbSilkscreen, LightGeometryType.HighAngleRing, LightWavelength.Red630, 68, false,
                "红光 (630nm) 与绿色阻焊油墨互补被强烈吸收变暗，白色丝印与红色标记反射红光变亮，显著拉开对比度。"),
            new(
                "精密冲压件尺寸测量", "远心背光剪影",
                WorkpieceType.StampedContour, LightGeometryType.Backlight, LightWavelength.Green525, 90, false,
                "背光从工件底部透射形成纯黑高锐度剪影，彻底消除表面反光与纹理干扰，满足亚像素尺寸测量。"),
            new(
                "曲面高反光金属/焊点", "穹顶无影光",
                WorkpieceType.CurvedSpecularMetal, LightGeometryType.Dome, LightWavelength.White, 75, false,
                "穹顶内壁漫反射涂层从 360° 均匀柔和照明，彻底消除曲面金属与锡球焊点的刺眼镜面炫光和硬阴影。"),
            new(
                "硅片/IC 镜面压伤", "同轴光垂直照明",
                WorkpieceType.WaferMicroPit, LightGeometryType.Coaxial, LightWavelength.Blue470, 90, false,
                "45° 分光镜使入射光与镜头光轴完全重合，平坦镜面均匀明亮，微小凹坑/压伤因偏折不入镜呈清晰黑点。"),
            new(
                "塑膜反光字符去炫光", "条形光 + 正交偏振",
                WorkpieceType.GlossyFilmLabel, LightGeometryType.DualBar, LightWavelength.Red630, 62, true,
                "镜面反光保持偏振态被镜头正交偏振片滤除，字符漫反射退偏振通过镜头，解决透明膜耀斑遮挡问题。"),
        ];

        public static IReadOnlyList<LightFixtureGuideRow> FixtureGuides { get; } =
        [
            new(LightGeometryType.HighAngleRing, "高角度环形光 (60°~85°)", "明场 (Bright Field)",
                "PCB 字符、二维码、通用漫反射定位与装配检测", "性价比高、光效高，但对高反光金属易产生环形镜面炫光"),
            new(LightGeometryType.LowAngleDarkField, "低角度暗场光 (10°~30°)", "暗场 (Dark Field)",
                "金属/玻璃表面划痕、毛刺、冲压凹凸、镭雕 DPM 码", "平面背景纯黑、缺陷极亮；需紧贴工件安装，工作距离受限"),
            new(LightGeometryType.Coaxial, "同轴光 (45° 分光镜)", "垂直明场 (Coaxial)",
                "镜面硅片、玻璃基板、IC 芯片、金属平面激光打标", "无阴影且对微倾角凹坑极敏感；分光镜二次透反损失 ~75% 光通量"),
            new(LightGeometryType.Dome, "穹顶无影光 (Dome / 碗光)", "全向漫射无影",
                "圆柱金属件、BGA 焊锡球、易拉罐底、褶皱反光包装膜", "完全消除高反光曲面耀斑与阴影；体积较大、近距离安装"),
            new(LightGeometryType.Backlight, "底部平行背光 (Backlight)", "透射剪影 (Silhouette)",
                "冲压件/齿轮精密尺寸测量、缺口/毛刺/通孔、透明瓶液位", "轮廓边缘对比度接近 100%，但无法检测工件正面表面特征"),
            new(LightGeometryType.DualBar, "双向条形组合光 (Bar)", "明/暗场可调角度",
                "大视野表面检测、光伏硅片、四面独立程控多角度打光", "安装灵活可兼顾明场与暗场；需仔细调平两侧照度均匀性"),
        ];

        public static LightingSimulationResult Simulate(LightingParameters p)
        {
            var angle = Math.Clamp(p.IncidenceAngleDeg, 10.0, 90.0);
            var wd = Math.Clamp(p.WorkingDistanceMm, 30.0, 400.0);
            var gain = Math.Clamp(p.ExposureGainPercent, 30.0, 200.0) / 100.0;

            var isDarkField = p.LightType switch
            {
                LightGeometryType.LowAngleDarkField => true,
                LightGeometryType.DualBar or LightGeometryType.HighAngleRing => angle <= 35.0,
                _ => false,
            };

            // 1. Optical throughput efficiency (%)
            var baseEff = p.LightType switch
            {
                LightGeometryType.HighAngleRing => 92.0,
                LightGeometryType.LowAngleDarkField => 78.0,
                LightGeometryType.Coaxial => 25.0,   // 50% reflection x 50% transmission through 45° beam splitter
                LightGeometryType.Dome => 58.0,      // Diffuse cavity bounce losses
                LightGeometryType.Backlight => 96.0,
                LightGeometryType.DualBar => 86.0,
                _ => 85.0,
            };

            var eff = baseEff * (p.UseCrossPolarizer ? 0.35 : 1.0);
            // Distance attenuation (gentle industrial inverse-square approximation normalized around 120mm)
            var wdFactor = p.LightType == LightGeometryType.Backlight
                ? 1.0
                : Math.Clamp(Math.Pow(120.0 / Math.Max(60.0, wd), 0.35), 0.65, 1.25);
            var strobeBoost = p.UseStrobeOverdrive ? 1.35 : 1.0;
            // Effective sensor exposure scale (compensates partially for lower efficiency via auto-exposure baseline,
            // while still reflecting dimness when coaxial + polarizer stack without strobe/gain)
            var fluxScale = Math.Clamp((0.58 + 0.42 * (eff / 92.0)) * wdFactor * strobeBoost * gain, 0.25, 1.9);

            // 2. Compute physically motivated Background Gray, Feature Gray, and Glare Risk
            ComputeSurfaceResponse(p, angle, isDarkField, fluxScale,
                out var bgGray, out var featGray, out var glareRisk, out var advice);

            var bgInt = (int)Math.Round(Math.Clamp(bgGray, 0, 255));
            var featInt = (int)Math.Round(Math.Clamp(featGray, 0, 255));
            var rawContrast = Math.Abs(featInt - bgInt) / 255.0 * 100.0;
            // Severe glare degrades usable contrast in the ROI
            var effectiveContrast = Math.Clamp(rawContrast * (1.0 - glareRisk * 0.55), 0.0, 100.0);
            var glarePct = Math.Clamp(glareRisk * 100.0, 0.0, 100.0);

            var (grade, optimal) = (effectiveContrast, glarePct) switch
            {
                ( >= 68, <= 20) => ("极佳 · 稳定阈值分割", true),
                ( >= 45, <= 38) => ("良好 · 可用", true),
                ( >= 25, _) => ("偏低 · 抗干扰弱", false),
                _ => ("不合格 · 难以稳定检出", false),
            };

            var modeText = p.LightType switch
            {
                LightGeometryType.Backlight => "透射背光剪影 (Backlight)",
                LightGeometryType.Coaxial => "同轴垂直明场 (Coaxial)",
                LightGeometryType.Dome => "360° 穹顶漫射无影 (Dome)",
                _ => isDarkField
                    ? $"暗场掠射 ({angle:0}° · Dark Field)"
                    : $"明场反射 ({angle:0}° · Bright Field)",
            };

            var scanline = BuildScanlineProfile(p, bgInt, featInt, glareRisk);
            var patches = ComputeColorPatches(p.Wavelength);

            return new LightingSimulationResult(
                p, isDarkField, modeText, bgInt, featInt,
                Math.Round(effectiveContrast, 1),
                Math.Round(glarePct, 1),
                Math.Round(eff, 1),
                grade, optimal, advice, scanline, patches);
        }

        public static IReadOnlyList<ColorPatchReading> ComputeColorPatches(LightWavelength wavelength)
        {
            var items = new (TargetSurfaceColor Color, string Name, string Hex)[]
            {
                (TargetSurfaceColor.Red, "红色标贴/油墨", "#E05263"),
                (TargetSurfaceColor.Green, "绿色 PCB 阻焊", "#2E9E62"),
                (TargetSurfaceColor.Blue, "蓝色喷码/元件", "#3B82F6"),
                (TargetSurfaceColor.Yellow, "黄色 Kapton 胶带", "#EAB308"),
                (TargetSurfaceColor.White, "白色丝印/陶瓷", "#E2E8F0"),
                (TargetSurfaceColor.DarkGray, "黑色橡胶/氧化铝", "#334155"),
            };

            var result = new List<ColorPatchReading>(items.Length);
            foreach (var (color, name, hex) in items)
            {
                var refl = GetSpectralReflectance(color, wavelength);
                var gray = (int)Math.Round(Math.Clamp(refl * 245.0, 8, 252));
                result.Add(new ColorPatchReading(color, name, hex, Math.Round(refl, 2), gray));
            }

            return result;
        }

        public static (LightWavelength BestWavelength, string StrategyText, int PredictedDeltaGray) RecommendWavelengthForColorPair(
            TargetSurfaceColor targetColor,
            TargetSurfaceColor backgroundColor)
        {
            if (targetColor == backgroundColor)
            {
                return (LightWavelength.White, "目标与背景颜色相同，单靠色差无法分离，应改用暗场/同轴/背光等几何打光方式。", 0);
            }

            LightWavelength[] candidates =
            [
                LightWavelength.Red630,
                LightWavelength.Green525,
                LightWavelength.Blue470,
                LightWavelength.Ir850,
                LightWavelength.White,
            ];

            var bestWave = LightWavelength.Red630;
            var bestDelta = -1;
            var targetBrighter = true;

            foreach (var w in candidates)
            {
                var tGray = (int)Math.Round(GetSpectralReflectance(targetColor, w) * 245.0);
                var bGray = (int)Math.Round(GetSpectralReflectance(backgroundColor, w) * 245.0);
                var delta = Math.Abs(tGray - bGray);
                if (delta > bestDelta)
                {
                    bestDelta = delta;
                    bestWave = w;
                    targetBrighter = tGray >= bGray;
                }
            }

            var relation = targetBrighter
                ? $"使用 {FormatWavelengthShort(bestWave)}：目标反射增强变亮，背景互补吸收变暗（理论灰度差 Δ={bestDelta}）。"
                : $"使用 {FormatWavelengthShort(bestWave)}：背景反射变白“擦除”干扰，目标强烈吸收变黑（理论灰度差 Δ={bestDelta}）。";

            return (bestWave, relation, bestDelta);
        }

        /// <summary>Computes maximum permissible exposure/strobe pulse width to bound motion blur.</summary>
        public static StrobeCalculationResult CalculateStrobe(
            double lineSpeedMmPerS,
            double pixelResUmPerPx,
            double maxBlurPx,
            double triggerFps)
        {
            var speed = Math.Max(1.0, lineSpeedMmPerS);
            var res = Math.Max(1.0, pixelResUmPerPx);
            var blur = Math.Max(0.1, maxBlurPx);
            var fps = Math.Max(0.5, triggerFps);

            // T_max (us) = (blur_px * um/px) / (mm/s * 0.001 um/us) = blur * res * 1000 / speed
            var maxExposureUs = (blur * res * 1000.0) / speed;
            // Motion blur if camera used a typical 1ms (1000 us) continuous-light exposure
            var blurAt1ms = (speed * 1.0) / res; // 1ms * speed(mm/s) = speed um -> / res(um/px)

            var needsStrobe = maxExposureUs < 400.0;
            var overdrive = maxExposureUs switch
            {
                < 40 => 5.0,
                < 100 => 4.0,
                < 250 => 3.0,
                < 400 => 2.0,
                _ => 1.0,
            };

            var dutyCyclePct = (maxExposureUs / 1_000_000.0) * fps * 100.0;
            var isDutySafe = !needsStrobe || dutyCyclePct <= 10.0;

            var verdict = !needsStrobe
                ? string.Create(CultureInfo.InvariantCulture,
                    $"允许曝光 {maxExposureUs:F0} µs（较充裕），常规常亮光源即可满足 ≤{blur:F1} px 拖影要求。")
                : isDutySafe
                    ? string.Create(CultureInfo.InvariantCulture,
                        $"必须采用频闪控制器：脉宽 ≤ {maxExposureUs:F1} µs，建议 {overdrive:F0}× 过驱动爆闪（占空比 {dutyCyclePct:F2}% ≤ 10%，安全不过热）。")
                    : string.Create(CultureInfo.InvariantCulture,
                        $"频闪脉宽 ≤ {maxExposureUs:F1} µs，但当前帧率下占空比 {dutyCyclePct:F1}% > 10% 过驱动热安全限，需降帧率或加散热/增光源亮度！");

            return new StrobeCalculationResult(
                Math.Round(maxExposureUs, 1),
                Math.Round(blurAt1ms, 2),
                needsStrobe,
                overdrive,
                Math.Round(dutyCyclePct, 2),
                isDutySafe,
                verdict);
        }

        public static double GetSpectralReflectance(TargetSurfaceColor color, LightWavelength wavelength) => (color, wavelength) switch
        {
            // White reflects across visible & IR; slightly less in UV
            (TargetSurfaceColor.White, LightWavelength.Uv365) => 0.78,
            (TargetSurfaceColor.White, _) => 0.94,

            // DarkGray absorbs across visible; slightly higher in IR
            (TargetSurfaceColor.DarkGray, LightWavelength.Ir850) => 0.34,
            (TargetSurfaceColor.DarkGray, _) => 0.14,

            // Red reflects Red & IR, absorbs Green & Blue (complementary!)
            (TargetSurfaceColor.Red, LightWavelength.Red630) => 0.90,
            (TargetSurfaceColor.Red, LightWavelength.Ir850) => 0.85,
            (TargetSurfaceColor.Red, LightWavelength.White) => 0.56,
            (TargetSurfaceColor.Red, LightWavelength.Green525) => 0.14,
            (TargetSurfaceColor.Red, LightWavelength.Blue470) => 0.12,
            (TargetSurfaceColor.Red, LightWavelength.Uv365) => 0.18,

            // Green reflects Green, absorbs Red & Blue
            (TargetSurfaceColor.Green, LightWavelength.Green525) => 0.86,
            (TargetSurfaceColor.Green, LightWavelength.White) => 0.52,
            (TargetSurfaceColor.Green, LightWavelength.Red630) => 0.11,
            (TargetSurfaceColor.Green, LightWavelength.Blue470) => 0.18,
            (TargetSurfaceColor.Green, LightWavelength.Ir850) => 0.62,
            (TargetSurfaceColor.Green, LightWavelength.Uv365) => 0.16,

            // Blue reflects Blue & UV, strongly absorbs Red
            (TargetSurfaceColor.Blue, LightWavelength.Blue470) => 0.88,
            (TargetSurfaceColor.Blue, LightWavelength.Uv365) => 0.72,
            (TargetSurfaceColor.Blue, LightWavelength.White) => 0.48,
            (TargetSurfaceColor.Blue, LightWavelength.Green525) => 0.24,
            (TargetSurfaceColor.Blue, LightWavelength.Red630) => 0.10,
            (TargetSurfaceColor.Blue, LightWavelength.Ir850) => 0.55,

            // Yellow = Red + Green -> bright under Red/Green/White, DARK under complementary Blue!
            (TargetSurfaceColor.Yellow, LightWavelength.Red630) => 0.88,
            (TargetSurfaceColor.Yellow, LightWavelength.Green525) => 0.84,
            (TargetSurfaceColor.Yellow, LightWavelength.White) => 0.82,
            (TargetSurfaceColor.Yellow, LightWavelength.Ir850) => 0.86,
            (TargetSurfaceColor.Yellow, LightWavelength.Blue470) => 0.12,
            (TargetSurfaceColor.Yellow, LightWavelength.Uv365) => 0.15,

            _ => 0.5,
        };

        public static string FormatWavelengthShort(LightWavelength w) => w switch
        {
            LightWavelength.White => "白光 (White)",
            LightWavelength.Red630 => "红光 (630 nm)",
            LightWavelength.Green525 => "绿光 (525 nm)",
            LightWavelength.Blue470 => "蓝光 (470 nm)",
            LightWavelength.Ir850 => "近红外 (850 nm)",
            LightWavelength.Uv365 => "紫外 (365 nm)",
            _ => w.ToString(),
        };

        public static string GetWavelengthHex(LightWavelength w) => w switch
        {
            LightWavelength.Red630 => "#F38BA8",
            LightWavelength.Green525 => "#A6E3A1",
            LightWavelength.Blue470 => "#89B4FA",
            LightWavelength.Ir850 => "#E05263",
            LightWavelength.Uv365 => "#CBA6F7",
            _ => "#F8FAFC",
        };

        private static void ComputeSurfaceResponse(
            LightingParameters p,
            double angle,
            bool isDarkField,
            double fluxScale,
            out double bgGray,
            out double featGray,
            out double glareRisk,
            out string advice)
        {
            // Wavelength micro-scratch sensitivity bonus (shorter lambda scatters more strongly off micro-scratches)
            var shortWaveBonus = p.Wavelength switch
            {
                LightWavelength.Uv365 => 1.18,
                LightWavelength.Blue470 => 1.14,
                LightWavelength.Green525 => 1.04,
                LightWavelength.White => 1.0,
                LightWavelength.Red630 => 0.90,
                LightWavelength.Ir850 => 0.78,
                _ => 1.0,
            };

            if (p.LightType == LightGeometryType.Backlight)
            {
                if (p.Workpiece == WorkpieceType.StampedContour)
                {
                    bgGray = 248 * fluxScale;
                    featGray = 10;
                    glareRisk = 0.02;
                    advice = "背光剪影模式：工件不透光呈极黑轮廓，背景高均匀透亮，非常适合尺寸与轮廓缺陷检测。";
                }
                else
                {
                    bgGray = 245 * fluxScale;
                    featGray = 12;
                    glareRisk = 0.05;
                    advice = "警告：底部背光仅呈现外轮廓剪影，工件正面表面划痕/字符/焊盘完全不可见，请切换正面反射光源。";
                    // Penalize contrast for surface-inspection tasks under backlight (you see silhouette, not the surface defect!)
                    bgGray = 22;
                    featGray = 14;
                }

                return;
            }

            switch (p.Workpiece)
            {
                case WorkpieceType.MetalMirrorScratch:
                    if (isDarkField)
                    {
                        bgGray = 18 * fluxScale;
                        featGray = 228 * shortWaveBonus * fluxScale;
                        glareRisk = 0.04;
                        advice = $"暗场打光生效 ({angle:0}°)：镜面反射光偏离镜头呈暗底，划痕边缘散射光入镜呈高亮白线（搭配短波蓝/UV 效果更佳）。";
                    }
                    else if (p.LightType == LightGeometryType.Coaxial)
                    {
                        bgGray = 222 * fluxScale;
                        featGray = 48 * fluxScale;
                        glareRisk = 0.12;
                        advice = "同轴光打光：镜面呈均匀亮底，划痕散射偏离光轴呈清晰暗线；注意同轴分光镜有 ~75% 光损。";
                    }
                    else if (p.LightType == LightGeometryType.Dome)
                    {
                        bgGray = 188 * fluxScale;
                        featGray = 124 * fluxScale;
                        glareRisk = 0.05;
                        advice = "穹顶光能抑制炫光，但全向漫射光会把浅划痕“填平”，微划痕对比度不如低角度暗场光。";
                    }
                    else
                    {
                        // Bright-field ring/bar on flat mirror metal -> strong specular glare!
                        var specular = p.UseCrossPolarizer ? 0.18 : Math.Clamp((angle - 35.0) / 50.0, 0.2, 0.95);
                        bgGray = (p.UseCrossPolarizer ? 140 : 215 + 40 * specular) * fluxScale;
                        featGray = (p.UseCrossPolarizer ? 75 : 165) * fluxScale;
                        glareRisk = specular * 0.85;
                        advice = p.UseCrossPolarizer
                            ? "正交偏振片已大幅抑制金属反光，但微划痕仍建议改用低角度暗场光以获得最高信噪比。"
                            : "高角度明场照射镜面金属易产生耀斑过曝并淹没细划痕！建议降至 15°~25° 暗场光或改用同轴光。";
                    }

                    break;

                case WorkpieceType.GreenPcbSilkscreen:
                    {
                        var greenRefl = GetSpectralReflectance(TargetSurfaceColor.Green, p.Wavelength);
                        var whiteRefl = GetSpectralReflectance(TargetSurfaceColor.White, p.Wavelength);
                        if (isDarkField)
                        {
                            bgGray = 24 * fluxScale;
                            featGray = 128 * whiteRefl * fluxScale;
                            glareRisk = 0.06;
                            advice = "PCB 字符/丝印属于色差特征，低角度暗场易突出焊锡边缘噪声，建议改用高角度明场环形光 + 互补红光 (630nm)。";
                        }
                        else
                        {
                            bgGray = (235 * greenRefl) * fluxScale;
                            featGray = (242 * whiteRefl) * fluxScale;
                            glareRisk = p.LightType == LightGeometryType.HighAngleRing && !p.UseCrossPolarizer ? 0.16 : 0.06;
                            advice = p.Wavelength == LightWavelength.Red630
                                ? "红光 (630nm) 与绿色 PCB 阻焊互补：绿底强烈吸收红光变黑，白色丝印高度反射变白，对比度最优！"
                                : p.Wavelength == LightWavelength.Green525
                                    ? "绿光使绿色 PCB 背景反光变亮，导致绿底与白丝印灰度接近、对比度下降，建议切换红光 (630nm)！"
                                    : "当前为明场照明，切换互补色红光 (630nm) 可将绿色 PCB 基板压得更黑，大幅提升丝印分割稳定性。";
                        }

                        break;
                    }

                case WorkpieceType.StampedContour:
                    bgGray = isDarkField ? 32 * fluxScale : 148 * fluxScale;
                    featGray = isDarkField ? 142 * fluxScale : 96 * fluxScale;
                    glareRisk = p.LightType == LightGeometryType.HighAngleRing && !p.UseCrossPolarizer ? 0.35 : 0.10;
                    advice = "正面反射光受工件倒角反光与工作台背景影响，边缘定位易漂移；精密外径测量强烈建议改用底部平行背光！";
                    break;

                case WorkpieceType.CurvedSpecularMetal:
                    if (p.LightType == LightGeometryType.Dome)
                    {
                        bgGray = 205 * fluxScale;
                        featGray = 38 * fluxScale;
                        glareRisk = 0.04;
                        advice = "穹顶无影光完美贴合曲面高反光工件：360° 柔和漫射彻底消除圆柱镜面耀斑，表面字符/缺陷清晰稳定。";
                    }
                    else if (p.UseCrossPolarizer && !isDarkField)
                    {
                        bgGray = 172 * fluxScale;
                        featGray = 52 * fluxScale;
                        glareRisk = 0.14;
                        advice = "正交偏振片有效削弱了曲面炫光，但曲面边缘仍有亮度衰减，优先推荐穹顶无影光。";
                    }
                    else
                    {
                        bgGray = (isDarkField ? 35 : 235) * fluxScale;
                        featGray = (isDarkField ? 95 : 195) * fluxScale;
                        glareRisk = isDarkField ? 0.42 : 0.90;
                        advice = "曲面金属在定向光源下出现严重镜面过曝（255 饱和白条）与大面积暗影，强烈建议切换“穹顶无影光”！";
                    }

                    break;

                case WorkpieceType.WaferMicroPit:
                    if (p.LightType == LightGeometryType.Coaxial)
                    {
                        bgGray = 228 * fluxScale;
                        featGray = 24 * fluxScale;
                        glareRisk = 0.06;
                        advice = "同轴光最佳场景：硅片/IC 镜面垂直反射均匀高亮，微米级凹坑/压伤因斜面偏折不入镜而呈锐利黑点。";
                    }
                    else if (isDarkField)
                    {
                        bgGray = 16 * fluxScale;
                        featGray = 184 * shortWaveBonus * fluxScale;
                        glareRisk = 0.05;
                        advice = "低角度暗场也可突出凹坑边缘亮环，但对极浅平滑压伤的敏感度略逊于垂直同轴光。";
                    }
                    else
                    {
                        bgGray = 198 * fluxScale;
                        featGray = 152 * fluxScale;
                        glareRisk = p.UseCrossPolarizer ? 0.18 : 0.72;
                        advice = "普通环形光照射镜面芯片会在画面中央留下光源倒影炫光，建议改用“同轴光”实现无阴影均匀明场。";
                    }

                    break;

                case WorkpieceType.GlossyFilmLabel:
                    {
                        var redAbs = 1.0 - GetSpectralReflectance(TargetSurfaceColor.Blue, p.Wavelength);
                        if (p.UseCrossPolarizer && !isDarkField)
                        {
                            bgGray = 224 * fluxScale;
                            featGray = (28 + 38 * (1.0 - redAbs)) * fluxScale;
                            glareRisk = 0.05;
                            advice = "正交偏振片成功滤除塑料覆膜的镜面偏振反光，下方印刷字符清晰还原（若为蓝色字符配红光吸收更黑）。";
                        }
                        else if (p.LightType == LightGeometryType.Dome)
                        {
                            bgGray = 210 * fluxScale;
                            featGray = 54 * fluxScale;
                            glareRisk = 0.10;
                            advice = "穹顶无影光可柔化覆膜反光，开启“正交偏振片”可进一步彻底消除镜面雾白。";
                        }
                        else if (p.Wavelength == LightWavelength.Ir850 && !isDarkField)
                        {
                            bgGray = 212 * fluxScale;
                            featGray = 68 * fluxScale;
                            glareRisk = 0.22;
                            advice = "近红外 IR (850nm) 对部分彩色覆膜有穿透效果，但高光泽膜镜面反光仍需配合正交偏振片消除。";
                        }
                        else
                        {
                            bgGray = (isDarkField ? 38 : 238) * fluxScale;
                            featGray = (isDarkField ? 72 : 196) * fluxScale;
                            glareRisk = isDarkField ? 0.28 : 0.88;
                            advice = "塑料透明覆膜产生强镜面反光白斑遮挡字符！请勾选“正交偏振片 (光源+镜头 90°)”或改用穹顶无影光。";
                        }

                        break;
                    }

                default:
                    bgGray = 128;
                    featGray = 128;
                    glareRisk = 0;
                    advice = string.Empty;
                    break;
            }
        }

        private static IReadOnlyList<double> BuildScanlineProfile(LightingParameters p, int bgGray, int featGray, double glareRisk)
        {
            const int n = 64;
            var profile = new double[n];
            for (var i = 0; i < n; i++)
            {
                var u = (i / (double)(n - 1)) * 2.0 - 1.0; // -1 .. +1
                // Subtle natural lens vignetting + illumination rolloff at edges
                var vignette = p.LightType is LightGeometryType.Backlight or LightGeometryType.Dome ? 0.04 : 0.10;
                var val = bgGray * (1.0 - vignette * u * u);

                // Feature profile depends on workpiece geometry
                if (p.Workpiece == WorkpieceType.StampedContour)
                {
                    if (Math.Abs(u) < 0.44)
                    {
                        val = featGray;
                    }
                    else if (Math.Abs(u) < 0.48)
                    {
                        var t = (Math.Abs(u) - 0.44) / 0.04;
                        val = featGray + (bgGray - featGray) * t;
                    }
                }
                else if (p.Workpiece is WorkpieceType.MetalMirrorScratch or WorkpieceType.WaferMicroPit)
                {
                    // Narrow sharp defect peak/dip near center & secondary defect at +0.32
                    var d1 = Math.Exp(-Math.Pow((u + 0.08) / 0.065, 2));
                    var d2 = 0.85 * Math.Exp(-Math.Pow((u - 0.30) / 0.055, 2));
                    var blend = Math.Min(1.0, d1 + d2);
                    val = val * (1.0 - blend) + featGray * blend;
                }
                else
                {
                    // Multi-bar character / silkscreen / solder pads
                    var bar = Math.Abs(u + 0.36) < 0.08 || Math.Abs(u) < 0.09 || Math.Abs(u - 0.35) < 0.08;
                    if (bar)
                    {
                        val = featGray;
                    }
                }

                // Specular glare hotspot bloom (pushes center/off-center pixels toward 255 saturation!)
                if (glareRisk > 0.15)
                {
                    var bloom = Math.Exp(-Math.Pow((u - 0.12) / 0.32, 2)) * glareRisk * 145.0;
                    val += bloom;
                }

                // Tiny deterministic sensor read noise
                val += Math.Sin(i * 1.7 + bgGray * 0.1) * 2.2;
                profile[i] = Math.Clamp(val, 0, 255);
            }

            return profile;
        }
    }
}
