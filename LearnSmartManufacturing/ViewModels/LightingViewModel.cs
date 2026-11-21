using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LearnSmartManufacturing.Models.Vision;

namespace LearnSmartManufacturing.ViewModels
{
    public sealed record LightGeometryOption(LightGeometryType Type, string Name, double DefaultAngleDeg);

    public sealed record WorkpieceOption(WorkpieceType Type, string Name);

    public sealed record WavelengthOption(LightWavelength Wavelength, string Name);

    public sealed record SurfaceColorOption(TargetSurfaceColor Color, string Name);

    /// <summary>
    /// Machine Vision Lighting / Illumination Lab ViewModel:
    /// - Tab 1: 2D Bright/Dark-Field Optical Ray Tracing, Monochrome Camera Image &amp; Scanline Grayscale Profile
    /// - Tab 2: Spectral Complementary Color Wheel Lab, Flying-Capture Strobe Calculator &amp; 6-Fixture Selection Guide
    /// </summary>
    public partial class LightingViewModel : ObservableObject
    {
        private readonly bool _isInitialized;
        private bool _suppressAngleSnap;

        public LightingViewModel()
        {
            ScenarioPresets = LightingEngine.Presets;
            FixtureGuides = LightingEngine.FixtureGuides;

            LightGeometryOptions =
            [
                new(LightGeometryType.HighAngleRing, "高角度环形光 (明场 60°~85°)", 68),
                new(LightGeometryType.LowAngleDarkField, "低角度暗场光 (掠射 10°~30°)", 20),
                new(LightGeometryType.Coaxial, "同轴光 (45° 分光镜垂直明场)", 90),
                new(LightGeometryType.Dome, "穹顶无影光 (360° 漫射 Dome)", 75),
                new(LightGeometryType.Backlight, "底部平行背光 (轮廓剪影)", 90),
                new(LightGeometryType.DualBar, "双向条形光 (角度可调)", 55),
            ];

            WorkpieceOptions =
            [
                new(WorkpieceType.MetalMirrorScratch, "镜面金属 + 微划痕"),
                new(WorkpieceType.GreenPcbSilkscreen, "绿底 PCB 丝印与红标"),
                new(WorkpieceType.StampedContour, "精密冲压件轮廓测量"),
                new(WorkpieceType.CurvedSpecularMetal, "曲面高反光圆柱/焊点"),
                new(WorkpieceType.WaferMicroPit, "硅片/IC 镜面微凹坑"),
                new(WorkpieceType.GlossyFilmLabel, "高反光塑膜印刷字符"),
            ];

            WavelengthOptions =
            [
                new(LightWavelength.White, "白光 (400-700 nm · 通用)"),
                new(LightWavelength.Red630, "红光 (630 nm · 压暗绿/蓝)"),
                new(LightWavelength.Green525, "绿光 (525 nm · 压暗红)"),
                new(LightWavelength.Blue470, "蓝光 (470 nm · 微划痕/压暗黄红)"),
                new(LightWavelength.Ir850, "近红外 IR (850 nm · 穿透油墨/薄塑)"),
                new(LightWavelength.Uv365, "紫外 UV (365 nm · 超微表面/荧光)"),
            ];

            SurfaceColorOptions =
            [
                new(TargetSurfaceColor.Red, "红色 (标贴/油墨)"),
                new(TargetSurfaceColor.Green, "绿色 (PCB 阻焊)"),
                new(TargetSurfaceColor.Blue, "蓝色 (喷码/元件)"),
                new(TargetSurfaceColor.Yellow, "黄色 (Kapton 胶带)"),
                new(TargetSurfaceColor.White, "白色 (丝印/陶瓷)"),
                new(TargetSurfaceColor.DarkGray, "深灰/黑色 (金属/橡胶)"),
            ];

            // Default selections before enabling reactive recalculation
            SelectedLightGeometry = LightGeometryOptions[1]; // LowAngleDarkField
            SelectedWorkpiece = WorkpieceOptions[0];         // MetalMirrorScratch
            SelectedWavelength = WavelengthOptions[3];       // Blue 470nm
            SelectedTargetColor = SurfaceColorOptions[4];    // White silkscreen
            SelectedBackgroundColor = SurfaceColorOptions[1]; // Green PCB

            _isInitialized = true;

            ApplyScenarioPreset(ScenarioPresets[0]);
            RecalculateColorAdvice();
            RecalculateStrobe();
        }

        public IReadOnlyList<LightingScenarioPreset> ScenarioPresets { get; }
        public IReadOnlyList<LightFixtureGuideRow> FixtureGuides { get; }
        public IReadOnlyList<LightGeometryOption> LightGeometryOptions { get; }
        public IReadOnlyList<WorkpieceOption> WorkpieceOptions { get; }
        public IReadOnlyList<WavelengthOption> WavelengthOptions { get; }
        public IReadOnlyList<SurfaceColorOption> SurfaceColorOptions { get; }

        [ObservableProperty]
        private LightingScenarioPreset? selectedScenarioPreset;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsAngleAdjustable))]
        private LightGeometryOption selectedLightGeometry = null!;

        [ObservableProperty]
        private WorkpieceOption selectedWorkpiece = null!;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActiveWavelength))]
        private WavelengthOption selectedWavelength = null!;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IncidenceAngleText))]
        private double incidenceAngleDeg = 20;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(WorkingDistanceText))]
        private double workingDistanceMm = 120;

        [ObservableProperty]
        private bool useCrossPolarizer;

        [ObservableProperty]
        private bool useStrobeOverdrive;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ExposureGainText))]
        private double exposureGainPercent = 100;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ContrastText))]
        [NotifyPropertyChangedFor(nameof(GrayPairText))]
        [NotifyPropertyChangedFor(nameof(GlareRiskText))]
        [NotifyPropertyChangedFor(nameof(OpticalEfficiencyText))]
        [NotifyPropertyChangedFor(nameof(QualityGradeText))]
        [NotifyPropertyChangedFor(nameof(IsOptimalSetup))]
        [NotifyPropertyChangedFor(nameof(IsHighGlareRisk))]
        [NotifyPropertyChangedFor(nameof(EngineeringAdvice))]
        [NotifyPropertyChangedFor(nameof(ColorPatches))]
        private LightingSimulationResult simulationResult = null!;

        // Tab 2: Complementary Color Recommendation Lab
        [ObservableProperty]
        private SurfaceColorOption selectedTargetColor = null!;

        [ObservableProperty]
        private SurfaceColorOption selectedBackgroundColor = null!;

        [ObservableProperty]
        private LightWavelength recommendedPairWavelength = LightWavelength.Red630;

        [ObservableProperty]
        private string colorRecommendationText = string.Empty;

        // Tab 2: Flying-Capture Strobe Motion-Blur Calculator
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LineSpeedText))]
        private double lineSpeedMmPerS = 500;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PixelResText))]
        private double pixelResUmPerPx = 50;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MaxBlurText))]
        private double maxBlurPx = 0.5;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TriggerFpsText))]
        private double triggerFps = 25;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MaxExposureUsText))]
        [NotifyPropertyChangedFor(nameof(BlurAt1msText))]
        [NotifyPropertyChangedFor(nameof(OverdriveText))]
        [NotifyPropertyChangedFor(nameof(DutyCycleText))]
        [NotifyPropertyChangedFor(nameof(StrobeVerdictText))]
        [NotifyPropertyChangedFor(nameof(IsStrobeDutySafe))]
        private StrobeCalculationResult strobeResult = null!;

        [ObservableProperty]
        private string activeScenarioHint = string.Empty;

        public LightWavelength ActiveWavelength => SelectedWavelength?.Wavelength ?? LightWavelength.White;

        public bool IsAngleAdjustable => SelectedLightGeometry?.Type is
            LightGeometryType.HighAngleRing or LightGeometryType.LowAngleDarkField or LightGeometryType.DualBar;

        public string IncidenceAngleText => IsAngleAdjustable ? $"{IncidenceAngleDeg:0}°" : "固定光路";
        public string WorkingDistanceText => $"{WorkingDistanceMm:0} mm";
        public string ExposureGainText => $"{ExposureGainPercent:0}%";

        public string ContrastText => SimulationResult is null ? "--" : $"{SimulationResult.ContrastPercent:F1}%";
        public string GrayPairText => SimulationResult is null ? "--" : $"{SimulationResult.FeatureGray} / {SimulationResult.BackgroundGray}";
        public string GlareRiskText => SimulationResult is null ? "--" : $"{SimulationResult.GlareRiskPercent:F0}%";
        public string OpticalEfficiencyText => SimulationResult is null ? "--" : $"{SimulationResult.OpticalEfficiencyPercent:F0}%";
        public string QualityGradeText => SimulationResult?.QualityGrade ?? string.Empty;
        public bool IsOptimalSetup => SimulationResult?.IsOptimalSetup ?? false;
        public bool IsHighGlareRisk => (SimulationResult?.GlareRiskPercent ?? 0) >= 45;
        public string EngineeringAdvice => SimulationResult?.EngineeringAdvice ?? string.Empty;
        public IReadOnlyList<ColorPatchReading> ColorPatches => SimulationResult?.ColorPatches ?? [];

        public string LineSpeedText => $"{LineSpeedMmPerS:0} mm/s";
        public string PixelResText => $"{PixelResUmPerPx:0} µm/px";
        public string MaxBlurText => $"{MaxBlurPx:F1} px";
        public string TriggerFpsText => $"{TriggerFps:0} fps";

        public string MaxExposureUsText => StrobeResult is null ? "--" : $"{StrobeResult.MaxExposureUs:F1} µs";
        public string BlurAt1msText => StrobeResult is null ? "--" : $"常亮 1ms 拖影: {StrobeResult.ActualBlurAt1msPx:F1} px";
        public string OverdriveText => StrobeResult is null ? "--" : StrobeResult.NeedsStrobeOverdrive ? $"{StrobeResult.RecommendedOverdriveFactor:F0}× 频闪过驱动" : "常亮即可 (1×)";
        public string DutyCycleText => StrobeResult is null ? "--" : $"{StrobeResult.DutyCyclePercent:F2}%";
        public string StrobeVerdictText => StrobeResult?.Verdict ?? string.Empty;
        public bool IsStrobeDutySafe => StrobeResult?.IsDutyCycleSafe ?? true;

        partial void OnSelectedScenarioPresetChanged(LightingScenarioPreset? value)
        {
            if (_isInitialized && value is not null)
            {
                ApplyScenarioPreset(value);
            }
        }

        partial void OnSelectedLightGeometryChanged(LightGeometryOption value)
        {
            if (!_isInitialized || value is null)
            {
                return;
            }

            if (!_suppressAngleSnap)
            {
                IncidenceAngleDeg = value.DefaultAngleDeg;
            }

            RecalculateSimulation();
        }

        partial void OnSelectedWorkpieceChanged(WorkpieceOption value)
        {
            if (_isInitialized && value is not null)
            {
                RecalculateSimulation();
            }
        }

        partial void OnSelectedWavelengthChanged(WavelengthOption value)
        {
            if (_isInitialized && value is not null)
            {
                RecalculateSimulation();
            }
        }

        partial void OnIncidenceAngleDegChanged(double value)
        {
            if (_isInitialized) RecalculateSimulation();
        }

        partial void OnWorkingDistanceMmChanged(double value)
        {
            if (_isInitialized) RecalculateSimulation();
        }

        partial void OnUseCrossPolarizerChanged(bool value)
        {
            if (_isInitialized) RecalculateSimulation();
        }

        partial void OnUseStrobeOverdriveChanged(bool value)
        {
            if (_isInitialized) RecalculateSimulation();
        }

        partial void OnExposureGainPercentChanged(double value)
        {
            if (_isInitialized) RecalculateSimulation();
        }

        partial void OnSelectedTargetColorChanged(SurfaceColorOption value)
        {
            if (_isInitialized && value is not null) RecalculateColorAdvice();
        }

        partial void OnSelectedBackgroundColorChanged(SurfaceColorOption value)
        {
            if (_isInitialized && value is not null) RecalculateColorAdvice();
        }

        partial void OnLineSpeedMmPerSChanged(double value)
        {
            if (_isInitialized) RecalculateStrobe();
        }

        partial void OnPixelResUmPerPxChanged(double value)
        {
            if (_isInitialized) RecalculateStrobe();
        }

        partial void OnMaxBlurPxChanged(double value)
        {
            if (_isInitialized) RecalculateStrobe();
        }

        partial void OnTriggerFpsChanged(double value)
        {
            if (_isInitialized) RecalculateStrobe();
        }

        [RelayCommand]
        private void ApplyScenarioPreset(LightingScenarioPreset? preset)
        {
            if (preset is null)
            {
                return;
            }

            _suppressAngleSnap = true;
            try
            {
                SelectedScenarioPreset = preset;
                SelectedWorkpiece = WorkpieceOptions.First(w => w.Type == preset.Workpiece);
                SelectedLightGeometry = LightGeometryOptions.First(l => l.Type == preset.RecommendedLight);
                SelectedWavelength = WavelengthOptions.First(w => w.Wavelength == preset.RecommendedWavelength);
                IncidenceAngleDeg = preset.RecommendedAngleDeg;
                UseCrossPolarizer = preset.RecommendedPolarizer;
                WorkingDistanceMm = 120;
                ExposureGainPercent = 100;
                ActiveScenarioHint = $"推荐方案 [{preset.Name} · {preset.ShortGoal}]：{preset.PhysicsReason}";
            }
            finally
            {
                _suppressAngleSnap = false;
            }

            RecalculateSimulation();
        }

        [RelayCommand]
        private void SelectFixtureFromGuide(LightGeometryType lightType)
        {
            var match = LightGeometryOptions.FirstOrDefault(l => l.Type == lightType);
            if (match is not null)
            {
                SelectedLightGeometry = match;
            }
        }

        [RelayCommand]
        private void ApplyRecommendedWavelength()
        {
            var match = WavelengthOptions.FirstOrDefault(w => w.Wavelength == RecommendedPairWavelength);
            if (match is not null)
            {
                SelectedWavelength = match;
            }
        }

        [RelayCommand]
        private void ResetDefaults()
        {
            UseStrobeOverdrive = false;
            ApplyScenarioPreset(ScenarioPresets[0]);
        }

        private void RecalculateSimulation()
        {
            var p = new LightingParameters(
                SelectedLightGeometry.Type,
                SelectedWorkpiece.Type,
                SelectedWavelength.Wavelength,
                IncidenceAngleDeg,
                WorkingDistanceMm,
                UseCrossPolarizer,
                UseStrobeOverdrive,
                ExposureGainPercent);

            SimulationResult = LightingEngine.Simulate(p);
        }

        private void RecalculateColorAdvice()
        {
            var (bestWave, text, _) = LightingEngine.RecommendWavelengthForColorPair(
                SelectedTargetColor.Color, SelectedBackgroundColor.Color);
            RecommendedPairWavelength = bestWave;
            ColorRecommendationText = text;
        }

        private void RecalculateStrobe()
        {
            StrobeResult = LightingEngine.CalculateStrobe(LineSpeedMmPerS, PixelResUmPerPx, MaxBlurPx, TriggerFps);
        }
    }
}
