using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LearnSmartManufacturing.Models;

namespace LearnSmartManufacturing.ViewModels
{
    public record SensorPreset(string Name, double Width);

    /// <summary>
    /// Solves the focal length from working distance, field-of-view width and sensor width.
    /// Inputs are kept as text so that an empty or partially typed box simply shows a hint instead of a binding error.
    /// </summary>
    public partial class FocalLengthCalculator : ObservableObject
    {
        private static readonly double[] StandardFocalLengths = [6, 8, 12, 16, 25, 35, 50, 75, 100];

        public FocalLengthCalculator()
        {
            SensorPresets =
            [
                new SensorPreset("1/3″  (4.80 mm)", 4.8),
                new SensorPreset("1/2″  (6.40 mm)", 6.4),
                new SensorPreset("2/3″  (8.80 mm)", 8.8),
                new SensorPreset("1″  (12.80 mm)", 12.8),
            ];
            SelectedSensorPreset = SensorPresets[2];
        }

        public IReadOnlyList<SensorPreset> SensorPresets { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasResult), nameof(ResultText), nameof(Substitution), nameof(NearestStandardText), nameof(Hint))]
        public partial string WorkingDistanceText { get; set; } = "300";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasResult), nameof(ResultText), nameof(Substitution), nameof(NearestStandardText), nameof(Hint))]
        public partial string FovWidthText { get; set; } = "160";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasResult), nameof(ResultText), nameof(Substitution), nameof(NearestStandardText), nameof(Hint))]
        public partial string SensorWidthText { get; set; } = "8.8";

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
    }
}
