using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LearnSmartManufacturing.Models;

namespace LearnSmartManufacturing.ViewModels
{
    public record CameraSensor(string Name, double WidthMm, double PixelSizeUm, int ResX)
    {
        public string Display => $"{Name} (宽 {WidthMm:F2} mm, 像元 {PixelSizeUm:F2} μm, {ResX} px)";
    }

    public record ValuePreset(string Label, double Value, string? Caption = null);

    public partial class SimulationViewModel : ObservableObject
    {
        public const double DeltaZLimitMm = 60;

        private const double DefaultFocalMm = 16;
        private const double DefaultWdMm = 300;
        private const double DefaultFNumber = 5.6;
        private const double DefaultCocMultiplier = 2;

        // Every property derived from Result; raised together whenever the simulation is recalculated.
        private static readonly string[] DerivedProperties =
        [
            nameof(Result), nameof(IsInDof),
            nameof(SensorSpecText), nameof(FocalText), nameof(WdText), nameof(FNumberText), nameof(CocLimitText),
            nameof(DeltaZText), nameof(ShowDiffractionWarning),
            nameof(FovText), nameof(AngleText), nameof(HyperfocalText), nameof(TotalDofText), nameof(FrontRearDofText),
            nameof(PixelResText), nameof(MagnificationText), nameof(ApertureText),
            nameof(AiryText), nameof(CocActualText), nameof(CocStatusText), nameof(CocCompareText),
            nameof(ProfileSpanText), nameof(PreviewBlurRadius), nameof(PreviewBlurTag), nameof(PreviewBadgeText),
        ];

        private DofResult _result = null!;
        private double _blurPx;
        private bool _isBatchUpdating;

        public SimulationViewModel()
        {
            Sensors =
            [
                new CameraSensor("1/2 英寸", 6.40, 3.45, 1856),
                new CameraSensor("2/3 英寸 IMX250", 8.45, 3.45, 2448),
                new CameraSensor("1 英寸", 12.80, 4.50, 2844),
                new CameraSensor("1.1 英寸", 14.10, 3.45, 4096),
            ];
            FocalPresets = [.. new double[] { 8, 12, 16, 25, 35, 50 }.Select(v => new ValuePreset($"{v}mm", v))];
            FNumberPresets = [.. new[] { 1.4, 2.0, 2.8, 4.0, 5.6, 8.0, 11, 16 }.Select(v => new ValuePreset(v >= 10 ? $"F{v:0}" : $"F{v:0.0}", v))];
            CocPresets =
            [
                new ValuePreset("1× 像元", 1, "严苛精密"),
                new ValuePreset("2× 像元", 2, "常规检测"),
                new ValuePreset("3× 像元", 3, "宽松容差"),
            ];

            ApplyDefaults();
        }

        public IReadOnlyList<CameraSensor> Sensors { get; }
        public IReadOnlyList<ValuePreset> FocalPresets { get; }
        public IReadOnlyList<ValuePreset> FNumberPresets { get; }
        public IReadOnlyList<ValuePreset> CocPresets { get; }

        public double MinFocalMm => 6;
        public double MaxFocalMm => 50;
        public double MinWdMm => 100;
        public double MaxWdMm => 500;
        public double MinFNumber => 1.4;
        public double MaxFNumber => 16;
        public double DeltaZMinMm => -DeltaZLimitMm;
        public double DeltaZMaxMm => DeltaZLimitMm;

        [ObservableProperty]
        public partial CameraSensor? SelectedSensor { get; set; }

        [ObservableProperty]
        public partial double FocalMm { get; set; }

        [ObservableProperty]
        public partial double WdMm { get; set; }

        [ObservableProperty]
        public partial double FNumber { get; set; }

        [ObservableProperty]
        public partial double CocMultiplier { get; set; }

        [ObservableProperty]
        public partial double DeltaZMm { get; set; }

        [ObservableProperty]
        public partial ValuePreset? SelectedFocalPreset { get; set; }

        [ObservableProperty]
        public partial ValuePreset? SelectedFNumberPreset { get; set; }

        [ObservableProperty]
        public partial ValuePreset? SelectedCocPreset { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(InsetToggleText))]
        public partial bool IsInsetExpanded { get; set; } = false;

        public string Heading => "仿真";

        public string InsetToggleText => IsInsetExpanded ? "收起" : "展开";

        public DofResult Result => _result;
        public bool IsInDof => _result.IsInDof;

        public string SensorSpecText => $"{_result.PixelSizeUm:F2}μm · {SelectedSensor?.ResX}px";
        public string FocalText => $"{_result.FocalMm:F1} mm";
        public string WdText => $"{_result.WdMm:F0} mm";
        public string FNumberText => $"F{_result.FNumber:F1}";
        public string CocLimitText => $"{_result.CocLimitUm:F2} μm";
        public string DeltaZText => $"{(DeltaZMm > 0 ? "+" : string.Empty)}{DeltaZMm:F1} mm";
        public bool ShowDiffractionWarning => _result.FNumber >= 11;

        public string FovText => $"{_result.FovMm:F1} mm";
        public string AngleText => $"{_result.FovAngleDeg:F1}°";

        public string HyperfocalText => _result.HyperfocalMm >= 1000
            ? $"超焦距 H = {_result.HyperfocalMm / 1000:F2} m"
            : $"超焦距 H = {_result.HyperfocalMm:F0} mm";

        public string TotalDofText => _result.IsHyperfocal ? "∞ (超焦距)" : $"{_result.TotalDofMm:F2} mm";

        public string FrontRearDofText => _result.IsHyperfocal
            ? $"前景深 {_result.FrontDofMm:F1} mm | 后景深 ∞"
            : $"前景深 {_result.FrontDofMm:F2} | 后景深 {_result.RearDofMm:F2} mm";

        public string PixelResText => $"{_result.PixelResUm:F1} μm/px";
        public string MagnificationText => $"{_result.Magnification:F3}×";
        public string ApertureText => $"物理通光孔径 D = {_result.ApertureDiameterMm:F2} mm";

        public string AiryText => $"艾里斑 {_result.AiryDiskUm:F2} μm";
        public string CocActualText => _result.IsVirtualImage ? "∞ (s≤f)" : $"{_result.ActualCocUm:F2} μm";
        public string CocStatusText => _result.IsInDof ? "✓ 处于景深清晰带内" : "✗ 超出景深已离焦";
        public string CocCompareText => $"阈值 c = {_result.CocLimitUm:F2} μm · 工件物距 s = {_result.ObjectDistanceMm:F1} mm";

        public string ProfileSpanText => $"跨度 ±{_result.ProfileHalfSpanMm:F1} mm";

        /// <summary>Gaussian blur radius for the sensor preview, scaled from the CSS-style blur used by the reference page.</summary>
        public double PreviewBlurRadius => _blurPx * 2;

        public string PreviewBlurTag => _result.IsInDof
            ? (DiffractionBlurPx > 0.35 ? "合焦 (轻度衍射)" : "锐利合焦")
            : $"已离焦 (模糊 {_blurPx:F1}px)";

        public string PreviewBadgeText =>
            $"CoC {(_result.IsVirtualImage ? "∞" : _result.ActualCocUm.ToString("F1"))}μm / c {_result.CocLimitUm:F1}μm";

        private double DiffractionBlurPx
        {
            get
            {
                if (_result.FNumber < 11 || SelectedSensor is null)
                {
                    return 0;
                }

                var excess = Math.Max(0, (_result.AiryDiskUm - _result.CocLimitUm) / SelectedSensor.PixelSizeUm);
                return Math.Min(1.6, excess * 0.25);
            }
        }

        [RelayCommand]
        private void Reset() => ApplyDefaults();

        [RelayCommand]
        private void ZeroDeltaZ() => DeltaZMm = 0;

        [RelayCommand]
        private void ToggleInset() => IsInsetExpanded = !IsInsetExpanded;

        partial void OnSelectedSensorChanged(CameraSensor? value) => Recompute();

        partial void OnFocalMmChanged(double value)
        {
            SelectedFocalPreset = FocalPresets.FirstOrDefault(p => Math.Abs(p.Value - value) < 0.05);
            Recompute();
        }

        partial void OnWdMmChanged(double value) => Recompute();

        partial void OnFNumberChanged(double value)
        {
            SelectedFNumberPreset = FNumberPresets.FirstOrDefault(p => Math.Abs(p.Value - value) < 0.05);
            Recompute();
        }

        partial void OnCocMultiplierChanged(double value)
        {
            SelectedCocPreset = CocPresets.FirstOrDefault(p => Math.Abs(p.Value - value) < 1e-9);
            Recompute();
        }

        partial void OnDeltaZMmChanged(double value) => Recompute();

        partial void OnSelectedFocalPresetChanged(ValuePreset? value)
        {
            if (value is not null)
            {
                FocalMm = value.Value;
            }
        }

        partial void OnSelectedFNumberPresetChanged(ValuePreset? value)
        {
            if (value is not null)
            {
                FNumber = value.Value;
            }
        }

        partial void OnSelectedCocPresetChanged(ValuePreset? value)
        {
            if (value is not null)
            {
                CocMultiplier = value.Value;
            }
        }

        private void ApplyDefaults()
        {
            _isBatchUpdating = true;
            try
            {
                SelectedSensor = Sensors[1];
                FocalMm = DefaultFocalMm;
                WdMm = DefaultWdMm;
                FNumber = DefaultFNumber;
                CocMultiplier = DefaultCocMultiplier;
                DeltaZMm = 0;
            }
            finally
            {
                _isBatchUpdating = false;
            }

            Recompute();
        }

        private void Recompute()
        {
            if (_isBatchUpdating || SelectedSensor is null)
            {
                return;
            }

            _result = DofEngine.Compute(new DofParameters(
                SelectedSensor.WidthMm, SelectedSensor.PixelSizeUm, SelectedSensor.ResX,
                FocalMm, WdMm, FNumber, CocMultiplier, DeltaZMm));

            _blurPx = ComputeBlurPx();

            foreach (var name in DerivedProperties)
            {
                OnPropertyChanged(name);
            }
        }

        private double ComputeBlurPx()
        {
            var r = _result;
            double defocus;
            if (r.IsVirtualImage)
            {
                defocus = 12;
            }
            else if (r.ActualCocUm > r.CocLimitUm)
            {
                // Beyond the DOF the blur grows with the excess circle of confusion measured in pixels.
                var excessPixels = (r.ActualCocUm - r.CocLimitUm) / r.PixelSizeUm;
                defocus = Math.Min(10, 0.45 + excessPixels * 0.38);
            }
            else
            {
                defocus = r.ActualCocUm / r.CocLimitUm * 0.22;
            }

            return defocus + DiffractionBlurPx;
        }
    }
}
