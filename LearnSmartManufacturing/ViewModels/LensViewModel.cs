using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LearnSmartManufacturing.Models;
using LearnSmartManufacturing.Models.Vision;

namespace LearnSmartManufacturing.ViewModels
{
    public record LensPreset(string Name, double FocalLength);

    public record SensorPreset(string Name, double Width);

    /// <summary>
    /// Industrial Lens Optics Workbench ViewModel:
    /// - Tab 1: Interactive Similar-Triangle Optical Path, Focal-Length Solver, Macro Extension Tube &amp; Standard Lens Catalog
    /// - Tab 2: FA vs Telecentric Perspective Error Lab, Radial Distortion / Undistort Simulator &amp; C/CS/F/M42 Mount Matcher
    /// </summary>
    public partial class LensViewModel : ObservableObject
    {
        private readonly bool _isInitialized;

        public LensViewModel()
        {
            ScenarioPresets = LensEngineeringEngine.ScenarioPresets;
            MountSpecs = LensEngineeringEngine.MountSpecs;

            Presets =
            [
                new LensPreset("8 mm 广角", 8),
                new LensPreset("16 mm 标准", 16),
                new LensPreset("25 mm 中焦", 25),
                new LensPreset("50 mm 远摄", 50),
            ];

            SensorPresets =
            [
                new SensorPreset("1/3″  (4.80 mm)", 4.8),
                new SensorPreset("1/2″  (6.40 mm)", 6.4),
                new SensorPreset("2/3″  (8.45 mm)", 8.45),
                new SensorPreset("1″  (12.80 mm)", 12.8),
            ];

            SelectedPreset = Presets[1];
            SelectedSensorPreset = SensorPresets[2];
            SelectedCameraMount = MountSpecs.Single(m => m.Kind == LensMountKind.CsMount);
            SelectedLensMount = MountSpecs.Single(m => m.Kind == LensMountKind.CMount);
            ActiveScenarioHint = ScenarioPresets[0].Note;

            _isInitialized = true;

            RecalculateLensCatalog();
            RecalculateExtensionTube();
            RecalculateTelecentric();
            RecalculateDistortion();
            RecalculateMount();
        }

        public string Heading => "镜头";

        public IReadOnlyList<LensScenarioPreset> ScenarioPresets { get; }
        public IReadOnlyList<LensPreset> Presets { get; }
        public IReadOnlyList<SensorPreset> SensorPresets { get; }
        public IReadOnlyList<LensMountSpec> MountSpecs { get; }

        public double SensorWidth => TryParse(SensorWidthText, out var sw) ? sw : 8.45;
        public double MinFocalLength => OpticsCalculator.MinFocalLength;
        public double MaxFocalLength => OpticsCalculator.MaxFocalLength;
        public double MinWorkingDistance => OpticsCalculator.MinWorkingDistance;
        public double MaxWorkingDistance => OpticsCalculator.MaxWorkingDistance;

        [ObservableProperty]
        private string activeScenarioHint = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FovWidth), nameof(FovAngleDeg), nameof(Magnification), nameof(LensCategory), nameof(SimilarityRatio))]
        public partial double FocalLength { get; set; } = 16;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FovWidth), nameof(Magnification), nameof(SimilarityRatio))]
        public partial double WorkingDistance { get; set; } = 300;

        [ObservableProperty]
        public partial LensPreset? SelectedPreset { get; set; }

        public double FovWidth => Optics.FovWidth;
        public double FovAngleDeg => Optics.FovAngleDeg;
        public double Magnification => Optics.Magnification;
        public double SimilarityRatio => WorkingDistance / FocalLength;

        public string LensCategory
        {
            get
            {
                var f = Math.Round(FocalLength);
                return FocalLength switch
                {
                    <= 10 => $"广角定焦 ({f} mm) · 大视场角、需注意桶形畸变",
                    <= 20 => $"标准工业定焦 ({f} mm) · 均衡视野与畸变控制",
                    <= 35 => $"中焦镜头 ({f} mm) · 视场角收窄、低畸变",
                    _ => $"远摄长焦镜头 ({f} mm) · 窄视场角、高细节分辨率",
                };
            }
        }

        private OpticsResult Optics => OpticsCalculator.Compute(FocalLength, WorkingDistance, SensorWidth);

        #region Focal length calculator & standard catalog

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasResult), nameof(ResultText), nameof(Substitution), nameof(NearestStandardText), nameof(Hint))]
        public partial string WorkingDistanceText { get; set; } = "300";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasResult), nameof(ResultText), nameof(Substitution), nameof(NearestStandardText), nameof(Hint))]
        public partial string FovWidthText { get; set; } = "160";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SensorWidth), nameof(FovWidth), nameof(FovAngleDeg), nameof(Magnification),
            nameof(HasResult), nameof(ResultText), nameof(Substitution), nameof(NearestStandardText), nameof(Hint))]
        public partial string SensorWidthText { get; set; } = "8.45";

        [ObservableProperty]
        public partial SensorPreset? SelectedSensorPreset { get; set; }

        [ObservableProperty]
        private IReadOnlyList<LensCatalogRow> lensCatalogRows = [];

        public bool HasResult => TryGetInputs(out _, out _, out _);

        public string ResultText => TryGetInputs(out var wd, out var fov, out var sw)
            ? $"{OpticsCalculator.SolveFocalLength(wd, fov, sw):F2} mm"
            : "—";

        public string Substitution => TryGetInputs(out var wd, out var fov, out var sw)
            ? $"f = {Format(sw)} × {Format(wd)} / {Format(fov)} = {OpticsCalculator.SolveFocalLength(wd, fov, sw):F2} mm"
            : "f = Ws × WD / FOV";

        public string NearestStandardText
        {
            get
            {
                if (!TryGetInputs(out var wd, out var fov, out var sw))
                {
                    return string.Empty;
                }

                var f = OpticsCalculator.SolveFocalLength(wd, fov, sw);
                var nearest = LensEngineeringEngine.StandardFocalLengths.MinBy(s => Math.Abs(s - f));
                var actualFov = OpticsCalculator.Compute(nearest, wd, sw).FovWidth;
                return $"最接近的常用定焦镜头：{nearest} mm，此时实际视野宽度约 {actualFov:F1} mm";
            }
        }

        public string Hint => HasResult ? string.Empty : "请输入大于 0 的数值";

        #endregion

        #region Macro extension tube

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MacroWdText))]
        private double macroTargetWdMm = 80;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ExtensionThicknessText))]
        [NotifyPropertyChangedFor(nameof(ExtensionEffFText))]
        private ExtensionTubeResult extensionTubeResult = null!;

        public string MacroWdText => $"{MacroTargetWdMm:0} mm";
        public string ExtensionThicknessText => ExtensionTubeResult is { IsValid: true } r ? $"{r.ExtensionMm:F1} mm" : "—";
        public string ExtensionEffFText => ExtensionTubeResult is { IsValid: true } r ? $"F{r.EffectiveFNumber:F1} (β={r.Magnification:F2}×)" : "—";

        #endregion

        #region Tab 2: Telecentric vs FA, Radial Distortion & Mount Matcher

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TeleDeltaZText))]
        private double teleDeltaZMm = 8.0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TeleDiameterText))]
        private double teleNominalDiameterMm = 50.0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TeleWdText))]
        private double teleWorkingDistanceMm = 200.0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FaErrorSummaryText))]
        [NotifyPropertyChangedFor(nameof(TcErrorSummaryText))]
        private TelecentricComparisonResult telecentricResult = null!;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DistortionSliderText))]
        private double distortionPercent = -3.2;

        [ObservableProperty]
        private bool applyUndistortCalibration;

        [ObservableProperty]
        private DistortionSimulationResult distortionResult = null!;

        [ObservableProperty]
        private LensMountSpec selectedCameraMount = null!;

        [ObservableProperty]
        private LensMountSpec selectedLensMount = null!;

        [ObservableProperty]
        private MountMatchResult mountMatchResult = null!;

        public string TeleDeltaZText => $"{TeleDeltaZMm:+0.0;-0.0;0.0} mm";
        public string TeleDiameterText => $"{TeleNominalDiameterMm:0} mm";
        public string TeleWdText => $"{TeleWorkingDistanceMm:0} mm";
        public string FaErrorSummaryText => TelecentricResult is null ? "--" : $"{TelecentricResult.FaMeasuredMm:F2} mm ({TelecentricResult.FaErrorPercent:+0.00;-0.00;0.00}%)";
        public string TcErrorSummaryText => TelecentricResult is null ? "--" : $"{TelecentricResult.TcMeasuredMm:F2} mm ({TelecentricResult.TcErrorPercent:+0.00;-0.00;0.00}%)";
        public string DistortionSliderText => $"{DistortionPercent:+0.0;-0.0;0.0}%";

        #endregion

        partial void OnFocalLengthChanged(double value)
        {
            SelectedPreset = Presets.FirstOrDefault(p => p.FocalLength == Math.Round(value));
            if (_isInitialized)
            {
                RecalculateExtensionTube();
            }
        }

        partial void OnWorkingDistanceChanged(double value)
        {
            if (_isInitialized)
            {
                RecalculateLensCatalog();
            }
        }

        partial void OnSelectedPresetChanged(LensPreset? value)
        {
            if (value is not null)
            {
                FocalLength = value.FocalLength;
            }
        }

        partial void OnWorkingDistanceTextChanged(string value)
        {
            if (_isInitialized) RecalculateLensCatalog();
        }

        partial void OnFovWidthTextChanged(string value)
        {
            if (_isInitialized) RecalculateLensCatalog();
        }

        partial void OnSensorWidthTextChanged(string value)
        {
            SelectedSensorPreset = TryParse(value, out var width)
                ? SensorPresets.FirstOrDefault(p => Math.Abs(p.Width - width) < 1e-9)
                : null;
            if (_isInitialized) RecalculateLensCatalog();
        }

        partial void OnSelectedSensorPresetChanged(SensorPreset? value)
        {
            if (value is not null)
            {
                SensorWidthText = value.Width.ToString("0.##", CultureInfo.CurrentCulture);
            }
        }

        partial void OnMacroTargetWdMmChanged(double value)
        {
            if (_isInitialized) RecalculateExtensionTube();
        }

        partial void OnTeleDeltaZMmChanged(double value)
        {
            if (_isInitialized) RecalculateTelecentric();
        }

        partial void OnTeleNominalDiameterMmChanged(double value)
        {
            if (_isInitialized) RecalculateTelecentric();
        }

        partial void OnTeleWorkingDistanceMmChanged(double value)
        {
            if (_isInitialized) RecalculateTelecentric();
        }

        partial void OnDistortionPercentChanged(double value)
        {
            if (_isInitialized) RecalculateDistortion();
        }

        partial void OnApplyUndistortCalibrationChanged(bool value)
        {
            if (_isInitialized) RecalculateDistortion();
        }

        partial void OnSelectedCameraMountChanged(LensMountSpec value)
        {
            if (_isInitialized && value is not null) RecalculateMount();
        }

        partial void OnSelectedLensMountChanged(LensMountSpec value)
        {
            if (_isInitialized && value is not null) RecalculateMount();
        }

        [RelayCommand]
        private void ApplyScenarioPreset(LensScenarioPreset? preset)
        {
            if (preset is null)
            {
                return;
            }

            FocalLength = Math.Clamp(preset.FocalLengthMm, MinFocalLength, MaxFocalLength);
            WorkingDistance = Math.Clamp(preset.WorkingDistanceMm, MinWorkingDistance, MaxWorkingDistance);
            WorkingDistanceText = preset.WorkingDistanceMm.ToString("0", CultureInfo.CurrentCulture);
            FovWidthText = preset.TargetFovMm.ToString("0", CultureInfo.CurrentCulture);
            SensorWidthText = preset.SensorWidthMm.ToString("0.##", CultureInfo.CurrentCulture);
            TeleDeltaZMm = preset.DemoDeltaZMm;
            TeleWorkingDistanceMm = preset.WorkingDistanceMm;
            DistortionPercent = preset.DemoDistortionPct;
            ApplyUndistortCalibration = false;
            ActiveScenarioHint = $"[{preset.Name} · {preset.Tag}] {preset.Note}";
        }

        [RelayCommand]
        private void ApplySolvedFocalToDiagram()
        {
            if (!TryGetInputs(out var wd, out var fov, out var sw))
            {
                return;
            }

            var f = OpticsCalculator.SolveFocalLength(wd, fov, sw);
            var nearest = LensEngineeringEngine.StandardFocalLengths.MinBy(s => Math.Abs(s - f));
            FocalLength = Math.Clamp(nearest, MinFocalLength, MaxFocalLength);
            WorkingDistance = Math.Clamp(wd, MinWorkingDistance, MaxWorkingDistance);
        }

        [RelayCommand]
        private void ApplyDistortionPreset(string? valueStr)
        {
            if (double.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
            {
                DistortionPercent = pct;
            }
        }

        [RelayCommand]
        private void DemoCsLensOnCMountTrap()
        {
            SelectedCameraMount = MountSpecs.Single(m => m.Kind == LensMountKind.CMount);
            SelectedLensMount = MountSpecs.Single(m => m.Kind == LensMountKind.CsMount);
        }

        [RelayCommand]
        private void ResetDefaults()
        {
            ApplyScenarioPreset(ScenarioPresets[0]);
            MacroTargetWdMm = 80;
            SelectedCameraMount = MountSpecs.Single(m => m.Kind == LensMountKind.CsMount);
            SelectedLensMount = MountSpecs.Single(m => m.Kind == LensMountKind.CMount);
        }

        private void RecalculateLensCatalog()
        {
            var wd = TryParse(WorkingDistanceText, out var parsedWd) ? parsedWd : WorkingDistance;
            double? solved = TryGetInputs(out var cwt, out var fov, out var sw)
                ? OpticsCalculator.SolveFocalLength(cwt, fov, sw)
                : null;
            LensCatalogRows = LensEngineeringEngine.BuildStandardLensCatalog(wd, SensorWidth, solved);
        }

        private void RecalculateExtensionTube()
        {
            ExtensionTubeResult = LensEngineeringEngine.EvaluateExtensionTube(FocalLength, MacroTargetWdMm, nominalFNumber: 4.0);
        }

        private void RecalculateTelecentric()
        {
            TelecentricResult = LensEngineeringEngine.EvaluateTelecentricVsFa(
                TeleNominalDiameterMm, TeleDeltaZMm, TeleWorkingDistanceMm);
        }

        private void RecalculateDistortion()
        {
            DistortionResult = LensEngineeringEngine.EvaluateDistortion(DistortionPercent, sensorResX: 2448, ApplyUndistortCalibration);
        }

        private void RecalculateMount()
        {
            if (SelectedCameraMount is null || SelectedLensMount is null)
            {
                return;
            }

            MountMatchResult = LensEngineeringEngine.EvaluateMountCompatibility(SelectedCameraMount.Kind, SelectedLensMount.Kind);
        }

        private bool TryGetInputs(out double wd, out double fov, out double sw)
        {
            fov = sw = 0;
            return TryParse(WorkingDistanceText, out wd)
                && TryParse(FovWidthText, out fov)
                && TryParse(SensorWidthText, out sw);
        }

        private static bool TryParse(string? text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && value > 0 && double.IsFinite(value);

        private static string Format(double v) => v.ToString("0.##", CultureInfo.CurrentCulture);
    }
}
