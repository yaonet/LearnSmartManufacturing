using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LearnSmartManufacturing.Models.Vision;

namespace LearnSmartManufacturing.ViewModels
{
    public record CameraModelSpec(string Name, int Width, int Height)
    {
        public double Megapixels => Width * (double)Height / 1_000_000;
    }

    public sealed record PixelFormatOption(PixelFormatKind Kind, string Name);

    public sealed record CameraCatalogRow(IndustrialCameraSpec Spec, bool IsRecommended, bool FitsRequirement);

    /// <summary>
    /// Industrial Camera Selection, Sensor/Lens Image-Circle Matching, Global vs Rolling Shutter Lab,
    /// and Interface Bandwidth Workbench ViewModel.
    /// </summary>
    public partial class CameraViewModel : ObservableObject
    {
        private readonly bool _isInitialized;
        private bool _syncingBandwidthCamera;

        public CameraViewModel()
        {
            ScenarioPresets = CameraSelectionEngine.ScenarioPresets;
            StandardCameras = CameraSelectionEngine.StandardCameras;
            LensImageCircles = CameraSelectionEngine.LensImageCircles;

            PixelFormatOptions =
            [
                new(PixelFormatKind.Mono8, CameraSelectionEngine.FormatPixelFormat(PixelFormatKind.Mono8)),
                new(PixelFormatKind.Mono12Packed, CameraSelectionEngine.FormatPixelFormat(PixelFormatKind.Mono12Packed)),
                new(PixelFormatKind.Mono12Unpacked, CameraSelectionEngine.FormatPixelFormat(PixelFormatKind.Mono12Unpacked)),
                new(PixelFormatKind.Rgb8, CameraSelectionEngine.FormatPixelFormat(PixelFormatKind.Rgb8)),
            ];

            // Default selections before enabling reactive hooks
            SelectedLensCircle = LensImageCircles.First(l => l.Name.StartsWith("2/3″", StringComparison.Ordinal));
            SelectedBandwidthCamera = StandardCameras[4]; // 500万 IMX250
            SelectedPixelFormat = PixelFormatOptions[0];  // Mono8
            ActiveScenarioHint = ScenarioPresets[0].Note;

            _isInitialized = true;

            RecalculateSelection(syncBandwidthCamera: true);
            RecalculateShutter();
            RecalculateBandwidth();
        }

        public string Heading => "相机";

        public IReadOnlyList<CameraScenarioPreset> ScenarioPresets { get; }
        public IReadOnlyList<IndustrialCameraSpec> StandardCameras { get; }
        public IReadOnlyList<LensImageCircleSpec> LensImageCircles { get; }
        public IReadOnlyList<PixelFormatOption> PixelFormatOptions { get; }

        // Core resolution inputs (kept as text to preserve graceful partial-input validation)
        [ObservableProperty]
        private string pixelAccuracyText = "0.05";

        [ObservableProperty]
        private string targetLengthText = "100";

        [ObservableProperty]
        private string targetWidthText = "80";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FovMarginText))]
        private double fovMarginPercent;

        [ObservableProperty]
        private LensImageCircleSpec selectedLensCircle = null!;

        [ObservableProperty]
        private string activeScenarioHint = string.Empty;

        // Computed selection visual & catalog state
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasResult))]
        [NotifyPropertyChangedFor(nameof(ResolutionText))]
        [NotifyPropertyChangedFor(nameof(PixelCountText))]
        [NotifyPropertyChangedFor(nameof(Substitution))]
        [NotifyPropertyChangedFor(nameof(RecommendationText))]
        [NotifyPropertyChangedFor(nameof(RecommendedCardText))]
        [NotifyPropertyChangedFor(nameof(RecommendedSubText))]
        [NotifyPropertyChangedFor(nameof(LensCompatibilityText))]
        [NotifyPropertyChangedFor(nameof(HasVignetting))]
        [NotifyPropertyChangedFor(nameof(Hint))]
        private CameraSelectionVisualData visualData = null!;

        [ObservableProperty]
        private IReadOnlyList<CameraCatalogRow> catalogRows = [];

        // Tab 2: Global vs Rolling Shutter Lab
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShutterSpeedText))]
        private double shutterLineSpeedMmPerS = 400;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShutterExposureText))]
        private double shutterExposureUs = 100;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RollingReadoutText))]
        private double rollingReadoutMs = 12.0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MotionBlurBadgeText))]
        [NotifyPropertyChangedFor(nameof(RollingSkewBadgeText))]
        [NotifyPropertyChangedFor(nameof(IsRollingAcceptable))]
        private ShutterSimulationResult shutterResult = null!;

        // Tab 2: Interface Bandwidth Calculator
        [ObservableProperty]
        private IndustrialCameraSpec selectedBandwidthCamera = null!;

        [ObservableProperty]
        private PixelFormatOption selectedPixelFormat = null!;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TargetFpsText))]
        private double targetFps = 30;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RequiredMBpsText))]
        [NotifyPropertyChangedFor(nameof(RequiredGbpsText))]
        [NotifyPropertyChangedFor(nameof(RecommendedInterfaceText))]
        [NotifyPropertyChangedFor(nameof(BandwidthFormulaText))]
        [NotifyPropertyChangedFor(nameof(InterfaceRows))]
        private BandwidthEvaluation bandwidthResult = null!;

        public string FovMarginText => FovMarginPercent <= 0.1 ? "0% (理论最小)" : $"+{FovMarginPercent:0}%";
        public string ShutterSpeedText => $"{ShutterLineSpeedMmPerS:0} mm/s";
        public string ShutterExposureText => $"{ShutterExposureUs:0} µs";
        public string RollingReadoutText => $"{RollingReadoutMs:F1} ms";
        public string TargetFpsText => $"{TargetFps:0} fps";

        public bool HasResult => TryGetResolution(out _, out _);

        public string ResolutionText => TryGetResolution(out var nx, out var ny)
            ? $"{nx:N0} × {ny:N0} px"
            : "—";

        public string PixelCountText => TryGetResolution(out var nx, out var ny)
            ? $"{nx * (double)ny / 1_000_000:F2} MP（共 {nx * ny:N0} 像素）"
            : "—";

        public string Substitution
        {
            get
            {
                if (!TryGetInputs(out var accuracy, out var length, out var width) || !TryGetResolution(out var nx, out var ny))
                {
                    return string.Empty;
                }

                var scale = 1.0 + FovMarginPercent / 100.0;
                var effL = length * scale;
                var effW = width * scale;
                var prefix = FovMarginPercent > 0.1 ? $"（含 +{FovMarginPercent:0}% 余量后 {Format(effL)} × {Format(effW)} mm）\n" : string.Empty;
                return $"{prefix}Nx = {Format(effL)} / {Format(accuracy)} = {nx:N0}\nNy = {Format(effW)} / {Format(accuracy)} = {ny:N0}\nN = {nx:N0} × {ny:N0} = {nx * (double)ny / 1_000_000:F2} MP";
            }
        }

        public string RecommendationText
        {
            get
            {
                if (!TryGetResolution(out _, out _) || VisualData is null)
                {
                    return string.Empty;
                }

                if (VisualData.RecommendedCamera is not { } match)
                {
                    return $"所需分辨率超出常见面阵相机，建议降低精度要求、分区域多相机拼接，或改用线扫描相机（按当前线速约需 {VisualData.LineScanSuggestedRateKHz:F1} kHz 行频）";
                }

                return $"推荐相机：{match.Name}像素（{match.Width} × {match.Height}，约 {match.Megapixels:F1} MP · {match.SensorModel} · {match.SensorFormat} · {match.ShutterText}）";
            }
        }

        public string RecommendedCardText => !HasResult
            ? "—"
            : VisualData?.RecommendedCamera is { } cam ? $"{cam.Name} ({cam.SensorFormat})" : "需线阵 / 多相机";

        public string RecommendedSubText => !HasResult
            ? string.Empty
            : VisualData?.RecommendedCamera is { } cam
                ? $"{cam.SensorModel} · {cam.ShutterText}"
                : $"行频需求 ≈ {VisualData?.LineScanSuggestedRateKHz:F1} kHz";

        public bool HasVignetting => VisualData?.HasVignetting ?? false;

        public string LensCompatibilityText => !HasResult || VisualData is null
            ? "—"
            : VisualData.RecommendedCamera is null
                ? "大像圆 F 口 / 线阵镜头"
                : VisualData.HasVignetting
                    ? $"暗角截断 (Ø{VisualData.SelectedLensCircle.DiameterMm:F1} < Ø{VisualData.RecommendedCamera.DiagonalMm:F1}mm)"
                    : $"完全覆盖 (Ø{VisualData.SelectedLensCircle.DiameterMm:F1} ≥ Ø{VisualData.RecommendedCamera.DiagonalMm:F1}mm)";

        public string Hint
        {
            get
            {
                if (!TryGetInputs(out _, out _, out _))
                {
                    return "请输入大于 0 的数值";
                }

                if (!TryGetResolution(out _, out _))
                {
                    return "所需像素数超出可计算范围，请检查像素精度与目标尺寸";
                }

                return string.Empty;
            }
        }

        public string MotionBlurBadgeText => ShutterResult is null ? "--" : $"{ShutterResult.MotionBlurPx:F2} px";
        public string RollingSkewBadgeText => ShutterResult is null ? "--" : $"{ShutterResult.RollingSkewPx:F1} px ({ShutterResult.RollingSkewAngleDeg:F1}°)";
        public bool IsRollingAcceptable => ShutterResult?.IsRollingAcceptable ?? true;

        public string RequiredMBpsText => BandwidthResult is null ? "--" : $"{BandwidthResult.RequiredMBps:F1} MB/s";
        public string RequiredGbpsText => BandwidthResult is null ? "--" : $"{BandwidthResult.RequiredGbps:F2} Gbps";
        public string RecommendedInterfaceText => BandwidthResult?.RecommendedInterface ?? "--";
        public string BandwidthFormulaText => BandwidthResult?.FormulaText ?? string.Empty;
        public IReadOnlyList<InterfaceBandwidthRow> InterfaceRows => BandwidthResult?.Interfaces ?? [];

        partial void OnPixelAccuracyTextChanged(string value)
        {
            if (_isInitialized)
            {
                RecalculateSelection(syncBandwidthCamera: true);
                RecalculateShutter();
            }
        }

        partial void OnTargetLengthTextChanged(string value)
        {
            if (_isInitialized) RecalculateSelection(syncBandwidthCamera: true);
        }

        partial void OnTargetWidthTextChanged(string value)
        {
            if (_isInitialized)
            {
                RecalculateSelection(syncBandwidthCamera: true);
                RecalculateShutter();
            }
        }

        partial void OnFovMarginPercentChanged(double value)
        {
            if (_isInitialized) RecalculateSelection(syncBandwidthCamera: true);
        }

        partial void OnSelectedLensCircleChanged(LensImageCircleSpec value)
        {
            if (_isInitialized && value is not null) RecalculateSelection(syncBandwidthCamera: false);
        }

        partial void OnShutterLineSpeedMmPerSChanged(double value)
        {
            if (_isInitialized)
            {
                RecalculateShutter();
                if (VisualData?.ExceedsAreaScan == true)
                {
                    RecalculateSelection(syncBandwidthCamera: false);
                }
            }
        }

        partial void OnShutterExposureUsChanged(double value)
        {
            if (_isInitialized) RecalculateShutter();
        }

        partial void OnRollingReadoutMsChanged(double value)
        {
            if (_isInitialized) RecalculateShutter();
        }

        partial void OnSelectedBandwidthCameraChanged(IndustrialCameraSpec value)
        {
            if (_isInitialized && value is not null && !_syncingBandwidthCamera)
            {
                RecalculateBandwidth();
            }
        }

        partial void OnSelectedPixelFormatChanged(PixelFormatOption value)
        {
            if (_isInitialized && value is not null) RecalculateBandwidth();
        }

        partial void OnTargetFpsChanged(double value)
        {
            if (_isInitialized) RecalculateBandwidth();
        }

        [RelayCommand]
        private void ApplyScenarioPreset(CameraScenarioPreset? preset)
        {
            if (preset is null)
            {
                return;
            }

            PixelAccuracyText = preset.PixelAccuracyText;
            TargetLengthText = preset.TargetLengthText;
            TargetWidthText = preset.TargetWidthText;
            FovMarginPercent = preset.FovMarginPercent;

            var lens = LensImageCircles.FirstOrDefault(l => l.Name == preset.RecommendedLensCircleName);
            if (lens is not null)
            {
                SelectedLensCircle = lens;
            }

            ShutterLineSpeedMmPerS = preset.DemoLineSpeedMmPerS;
            TargetFps = preset.DemoFps;
            ActiveScenarioHint = $"[{preset.Name} · {preset.Tag}] {preset.Note}";

            RecalculateSelection(syncBandwidthCamera: true);
            RecalculateShutter();
            RecalculateBandwidth();
        }

        [RelayCommand]
        private void SelectCameraForBandwidth(IndustrialCameraSpec? spec)
        {
            if (spec is null)
            {
                return;
            }

            SelectedBandwidthCamera = spec;
            RecalculateBandwidth();
        }

        [RelayCommand]
        private void ResetDefaults()
        {
            ApplyScenarioPreset(ScenarioPresets[0]);
            SelectedPixelFormat = PixelFormatOptions[0];
            ShutterExposureUs = 100;
            RollingReadoutMs = 12.0;
        }

        private void RecalculateSelection(bool syncBandwidthCamera)
        {
            if (!TryGetInputs(out var accuracy, out var length, out var width))
            {
                VisualData = new CameraSelectionVisualData(false, false, 0, 0, 0, 0, 0, 0,
                    FovMarginPercent, null, false, 0, SelectedLensCircle, false, 1.0, 0);
                CatalogRows = [.. StandardCameras.Select(c => new CameraCatalogRow(c, false, false))];
                return;
            }

            var data = CameraSelectionEngine.EvaluateSelection(
                accuracy, length, width, FovMarginPercent, SelectedLensCircle, ShutterLineSpeedMmPerS);
            VisualData = data;

            var rec = data.RecommendedCamera;
            CatalogRows = [.. StandardCameras.Select(c =>
            {
                var fits = data.IsValid && !data.ExceedsAreaScan &&
                    ((c.Width >= data.RequiredNx && c.Height >= data.RequiredNy) ||
                     (c.Width >= data.RequiredNy && c.Height >= data.RequiredNx));
                return new CameraCatalogRow(c, ReferenceEquals(c, rec), fits);
            })];

            if (syncBandwidthCamera && rec is not null)
            {
                _syncingBandwidthCamera = true;
                try
                {
                    SelectedBandwidthCamera = rec;
                }
                finally
                {
                    _syncingBandwidthCamera = false;
                }

                RecalculateBandwidth();
            }
        }

        private void RecalculateShutter()
        {
            var acc = TryParse(PixelAccuracyText, out var parsedAcc) ? parsedAcc : 0.05;
            var fovH = TryParse(TargetWidthText, out var parsedW) ? parsedW : 80.0;
            ShutterResult = CameraSelectionEngine.EvaluateShutter(
                ShutterLineSpeedMmPerS, ShutterExposureUs, RollingReadoutMs, acc, fovH);
        }

        private void RecalculateBandwidth()
        {
            var cam = SelectedBandwidthCamera ?? StandardCameras[4];
            var fmt = SelectedPixelFormat?.Kind ?? PixelFormatKind.Mono8;
            BandwidthResult = CameraSelectionEngine.EvaluateBandwidth(cam.Width, cam.Height, fmt, TargetFps);
        }

        private bool TryGetResolution(out long nx, out long ny)
        {
            nx = ny = 0;
            if (!TryGetInputs(out var accuracy, out var length, out var width))
            {
                return false;
            }

            var scale = 1.0 + FovMarginPercent / 100.0;
            var x = Math.Ceiling((length * scale) / accuracy);
            var y = Math.Ceiling((width * scale) / accuracy);
            if (!double.IsFinite(x) || !double.IsFinite(y) || x > int.MaxValue || y > int.MaxValue)
            {
                return false;
            }

            nx = (long)x;
            ny = (long)y;
            return true;
        }

        private bool TryGetInputs(out double accuracy, out double length, out double width)
        {
            length = width = 0;
            return TryParse(PixelAccuracyText, out accuracy)
                && TryParse(TargetLengthText, out length)
                && TryParse(TargetWidthText, out width);
        }

        private static bool TryParse(string? text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && value > 0 && double.IsFinite(value);

        private static string Format(double v) => v.ToString("0.####", CultureInfo.CurrentCulture);
    }
}
