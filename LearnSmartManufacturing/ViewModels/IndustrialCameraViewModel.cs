using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LearnSmartManufacturing.ViewModels
{
	public record CameraModelSpec(string Name, int Width, int Height)
	{
		public double Megapixels => Width * (double)Height / 1_000_000;
	}

	public partial class IndustrialCameraViewModel : ObservableObject
	{
		private static readonly CameraModelSpec[] StandardCameras =
		[
			new("130 万", 1280, 1024),
			new("200 万", 1600, 1200),
			new("230 万", 1920, 1200),
			new("320 万", 2048, 1536),
			new("500 万", 2448, 2048),
			new("630 万", 3072, 2048),
			new("1200 万", 4096, 3000),
			new("2000 万", 5472, 3648),
			new("2500 万", 5328, 4608),
			new("5000 万", 8192, 6144),
		];

		public string Heading => "工业相机";

		// Inputs are kept as text so that an empty or partially typed box simply shows a hint instead of a binding error.
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(HasResult), nameof(ResolutionText), nameof(PixelCountText), nameof(Substitution),
			nameof(RecommendationText), nameof(Hint))]
		public partial string PixelAccuracyText { get; set; } = "0.05";

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(HasResult), nameof(ResolutionText), nameof(PixelCountText), nameof(Substitution),
			nameof(RecommendationText), nameof(Hint))]
		public partial string TargetLengthText { get; set; } = "100";

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(HasResult), nameof(ResolutionText), nameof(PixelCountText), nameof(Substitution),
			nameof(RecommendationText), nameof(Hint))]
		public partial string TargetWidthText { get; set; } = "80";

		public bool HasResult => TryGetInputs(out _, out _, out _);

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

				return $"Nx = {Format(length)} / {Format(accuracy)} = {nx:N0}\nNy = {Format(width)} / {Format(accuracy)} = {ny:N0}\nN = {nx:N0} × {ny:N0} = {nx * (double)ny / 1_000_000:F2} MP";
			}
		}

		public string RecommendationText
		{
			get
			{
				if (!TryGetResolution(out var nx, out var ny))
				{
					return string.Empty;
				}

				// The sensor may be mounted rotated, so both orientations are accepted.
				var match = StandardCameras.FirstOrDefault(c =>
					(c.Width >= nx && c.Height >= ny) || (c.Width >= ny && c.Height >= nx));

				return match is null
					? "所需分辨率超出常见面阵相机，建议降低精度要求、分区域多相机拼接，或改用线扫描相机"
					: $"推荐相机：{match.Name}像素（{match.Width} × {match.Height}，约 {match.Megapixels:F1} MP）";
			}
		}

		public string Hint => HasResult ? string.Empty : "请输入大于 0 的数值";

		private bool TryGetResolution(out long nx, out long ny)
		{
			nx = ny = 0;
			if (!TryGetInputs(out var accuracy, out var length, out var width))
			{
				return false;
			}

			var x = Math.Ceiling(length / accuracy);
			var y = Math.Ceiling(width / accuracy);
			if (x > int.MaxValue || y > int.MaxValue)
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
