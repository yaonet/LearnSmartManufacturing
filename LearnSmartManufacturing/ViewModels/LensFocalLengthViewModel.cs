using CommunityToolkit.Mvvm.ComponentModel;
using LearnSmartManufacturing.Models;

namespace LearnSmartManufacturing.ViewModels
{
    public record LensPreset(string Name, double FocalLength);

    public partial class LensFocalLengthViewModel : ObservableObject
    {
        public LensFocalLengthViewModel()
        {
            Presets =
            [
                new LensPreset("8 mm 广角", 8),
                new LensPreset("16 mm 标准", 16),
                new LensPreset("25 mm 中焦", 25),
                new LensPreset("50 mm 远摄", 50),
            ];
            SelectedPreset = Presets[1];
        }

        public string Heading => "镜头焦距";

        public FocalLengthCalculator Calculator { get; } = new();

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
    }
}
