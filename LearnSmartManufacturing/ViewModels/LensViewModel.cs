using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LearnSmartManufacturing.Models;

namespace LearnSmartManufacturing.ViewModels
{
    public record LensPreset(string Name, double FocalLength);

    public record SensorPreset(string Name, double Width);

    public partial class LensViewModel : ObservableObject
    {
        private static readonly double[] StandardFocalLengths = [6, 8, 12, 16, 25, 35, 50, 75, 100];

        public LensViewModel()
        {
            Presets =
            [
                new LensPreset("8 mm 广角", 8),
                new LensPreset("16 mm 标准", 16),
                new LensPreset("25 mm 中焦", 25),
                new LensPreset("50 mm 远摄", 50),
            ];
            SelectedPreset = Presets[1];

            SensorPresets =
            [
                new SensorPreset("1/3″  (4.80 mm)", 4.8),
                new SensorPreset("1/2″  (6.40 mm)", 6.4),
                new SensorPreset("2/3″  (8.45 mm)", 8.45),
                new SensorPreset("1″  (12.80 mm)", 12.8),
            ];
            SelectedSensorPreset = SensorPresets[2];
        }

        public string Heading => "镜头";

        // 2/3" sensor width (mm)
        public double SensorWidth => 8.45;

        public double MinFocalLength => OpticsCalculator.MinFocalLength;
        public double MaxFocalLength => OpticsCalculator.MaxFocalLength;
        public double MinWorkingDistance => OpticsCalculator.MinWorkingDistance;
        public double MaxWorkingDistance => OpticsCalculator.MaxWorkingDistance;

        public IReadOnlyList<LensPreset> Presets { get; }

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
                    <= 10 => $"广角定焦 ({f} mm) · 大视场角、大视野范围",
                    <= 20 => $"标准工业定焦 ({f} mm) · 均衡视野与畸变控制",
                    <= 35 => $"中焦镜头 ({f} mm) · 视场角收窄、局部放大",
                    _ => $"远摄长焦镜头 ({f} mm) · 窄视场角、高细节分辨率",
                };
            }
        }

        private OpticsResult Optics => OpticsCalculator.Compute(FocalLength, WorkingDistance, SensorWidth);

        partial void OnFocalLengthChanged(double value)
        {
            SelectedPreset = Presets.FirstOrDefault(p => p.FocalLength == Math.Round(value));
        }

        partial void OnSelectedPresetChanged(LensPreset? value)
        {
            if (value is not null)
            {
                FocalLength = value.FocalLength;
            }
        }

        #region Focal length calculator

        // Inputs are kept as text so that an empty or partially typed box simply shows a hint instead of a binding error.
        public IReadOnlyList<SensorPreset> SensorPresets { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasResult), nameof(ResultText), nameof(Substitution), nameof(NearestStandardText), nameof(Hint))]
        public partial string WorkingDistanceText { get; set; } = "300";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasResult), nameof(ResultText), nameof(Substitution), nameof(NearestStandardText), nameof(Hint))]
        public partial string FovWidthText { get; set; } = "160";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasResult), nameof(ResultText), nameof(Substitution), nameof(NearestStandardText), nameof(Hint))]
        public partial string SensorWidthText { get; set; } = "8.45";

        [ObservableProperty]
        public partial SensorPreset? SelectedSensorPreset { get; set; }

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
                var nearest = StandardFocalLengths.MinBy(s => Math.Abs(s - f));
                var actualFov = OpticsCalculator.Compute(nearest, wd, sw).FovWidth;
                return $"最接近的常用定焦镜头：{nearest} mm，此时实际视野宽度约 {actualFov:F1} mm";
            }
        }

        public string Hint => HasResult ? string.Empty : "请输入大于 0 的数值";

        partial void OnSensorWidthTextChanged(string value)
        {
            SelectedSensorPreset = TryParse(value, out var width)
                ? SensorPresets.FirstOrDefault(p => Math.Abs(p.Width - width) < 1e-9)
                : null;
        }

        partial void OnSelectedSensorPresetChanged(SensorPreset? value)
        {
            if (value is not null)
            {
                SensorWidthText = value.Width.ToString("0.##", CultureInfo.CurrentCulture);
            }
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

        #endregion
    }
}
