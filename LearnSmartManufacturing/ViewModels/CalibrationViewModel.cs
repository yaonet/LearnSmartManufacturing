using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LearnSmartManufacturing.Models.Vision;

namespace LearnSmartManufacturing.ViewModels
{
    public sealed record HandEyeModeOption(HandEyeMountMode Mode, string Name, string Note);

    public sealed record RotationSampleCountOption(int Count, string Name);

    /// <summary>
    /// Nine-Point Hand-Eye Affine Calibration &amp; Rotation Center / Rotate-Then-Translate Alignment Lab ViewModel.
    /// </summary>
    public partial class CalibrationViewModel : ObservableObject
    {
        private const double TrueRotationCenterX = 100.0;
        private const double TrueRotationCenterY = 60.0;
        private const double TeachBaseAngleDeg = 32.0;

        private readonly bool _isInitialized;

        public CalibrationViewModel()
        {
            MountModes =
            [
                new(HandEyeMountMode.EyeToHand, "Eye-to-Hand 眼在手外 (固定相机)",
                    "相机固定于机架，吸嘴/Mark 随 XY 轴步进九宫格；直接把相机拍到的目标像素转为轴坐标。"),
                new(HandEyeMountMode.EyeInHand, "Eye-in-Hand 眼在手上 (随动相机)",
                    "相机固定在运动轴上，Mark 静止；轴正向移动时目标在图像中反向移动，标定系数自动取反。"),
            ];

            SampleCountOptions =
            [
                new(3, "3 点法 (最小外接圆)"),
                new(5, "5 点法 (最小二乘推荐)"),
                new(7, "7 点法 (高精度抗噪)"),
            ];

            SelectedMountMode = MountModes[0];
            SelectedSampleCount = SampleCountOptions[1]; // 5 points

            _isInitialized = true;

            RecalculateNinePoint();
            RecalculateRotationCenter();
        }

        public string Heading => "标定";

        public IReadOnlyList<HandEyeModeOption> MountModes { get; }
        public IReadOnlyList<RotationSampleCountOption> SampleCountOptions { get; }

        #region Tab 1: Nine-Point Hand-Eye Calibration

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MountModeNote))]
        private HandEyeModeOption selectedMountMode = null!;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ScaleInputText))]
        private double scaleMmPerPx = 0.05;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(InstallAngleText))]
        private double installAngleDeg = 12.5;

        [ObservableProperty]
        private bool flipImageY = true;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PixelNoiseText))]
        private double pixelNoiseSigmaPx = 0.15;

        [ObservableProperty]
        private bool injectOutlierOnPoint5;

        [ObservableProperty]
        private bool enableOutlierRejection;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TestPixelCoordText))]
        [NotifyPropertyChangedFor(nameof(MappedWorldCoordText))]
        [NotifyPropertyChangedFor(nameof(InverseRoundTripText))]
        private double testPixelU = 1480.0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TestPixelCoordText))]
        [NotifyPropertyChangedFor(nameof(MappedWorldCoordText))]
        [NotifyPropertyChangedFor(nameof(InverseRoundTripText))]
        private double testPixelV = 820.0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RmseText))]
        [NotifyPropertyChangedFor(nameof(MaxErrorText))]
        [NotifyPropertyChangedFor(nameof(IsRmseHealthy))]
        [NotifyPropertyChangedFor(nameof(ScaleSolvedText))]
        [NotifyPropertyChangedFor(nameof(RotationSolvedText))]
        [NotifyPropertyChangedFor(nameof(HandednessText))]
        [NotifyPropertyChangedFor(nameof(MatrixRow1Text))]
        [NotifyPropertyChangedFor(nameof(MatrixRow2Text))]
        [NotifyPropertyChangedFor(nameof(CalibrationPoints))]
        [NotifyPropertyChangedFor(nameof(UsedPointsText))]
        [NotifyPropertyChangedFor(nameof(MappedWorldCoordText))]
        [NotifyPropertyChangedFor(nameof(InverseRoundTripText))]
        private AffineCalibrationResult affineResult = null!;

        public string MountModeNote => SelectedMountMode?.Note ?? string.Empty;
        public string ScaleInputText => $"{ScaleMmPerPx:F3} mm/px";
        public string InstallAngleText => $"{InstallAngleDeg:+0.0;-0.0;0.0}°";
        public string PixelNoiseText => $"{PixelNoiseSigmaPx:F2} px";
        public string TestPixelCoordText => $"({TestPixelU:F1}, {TestPixelV:F1}) px";

        public string RmseText => AffineResult is { IsValid: true } r ? $"{r.RmseMm:F4} mm" : "—";
        public string MaxErrorText => AffineResult is { IsValid: true } r ? $"Max {r.MaxErrorMm:F4} mm" : "—";
        public bool IsRmseHealthy => AffineResult is { IsValid: true, RmseMm: <= 0.05 };
        public string ScaleSolvedText => AffineResult is { IsValid: true } r ? $"{r.ScaleXMmPerPx:F4} / {r.ScaleYMmPerPx:F4}" : "—";
        public string RotationSolvedText => AffineResult is { IsValid: true } r ? $"{r.RotationDeg:+0.00;-0.00;0.00}° (非正交 {r.ShearDeg:F2}°)" : "—";
        public string HandednessText => AffineResult is { IsValid: true } r
            ? (r.IsMirroredHandedness ? "det < 0 (图像Y↓与机械Y↑镜像)" : "det > 0 (同手性坐标系)")
            : "奇异矩阵";
        public string UsedPointsText => AffineResult is null ? "0 / 9" : $"{AffineResult.UsedPointCount} / {AffineResult.EvaluatedPoints.Count} 内点";
        public IReadOnlyList<CalibrationPointPair> CalibrationPoints => AffineResult?.EvaluatedPoints ?? [];

        public string MatrixRow1Text => AffineResult is { IsValid: true } r
            ? $"X = {r.A:+0.00000;-0.00000}·u {r.B:+0.00000;-0.00000}·v {r.C:+0.000;-0.000}"
            : "矩阵不可逆";

        public string MatrixRow2Text => AffineResult is { IsValid: true } r
            ? $"Y = {r.D:+0.00000;-0.00000}·u {r.E:+0.00000;-0.00000}·v {r.F:+0.000;-0.000}"
            : string.Empty;

        public string MappedWorldCoordText
        {
            get
            {
                if (AffineResult is not { IsValid: true } r) return "—";
                var (wx, wy) = r.TransformPixelToWorld(TestPixelU, TestPixelV);
                return $"({wx:F3}, {wy:F3}) mm";
            }
        }

        public string InverseRoundTripText
        {
            get
            {
                if (AffineResult is not { IsValid: true } r) return string.Empty;
                var (wx, wy) = r.TransformPixelToWorld(TestPixelU, TestPixelV);
                return r.TryTransformWorldToPixel(wx, wy, out var iu, out var iv)
                    ? $"逆变换回检 M⁻¹(X,Y) = ({iu:F1}, {iv:F1}) px"
                    : "矩阵不可逆";
            }
        }

        #endregion

        #region Tab 2: Rotation Center & Rotate-Then-Translate Alignment

        [ObservableProperty]
        private RotationSampleCountOption selectedSampleCount = null!;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ArmRadiusText))]
        private double armRadiusMm = 22.0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RotationSpanText))]
        private double rotationSpanDeg = 60.0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RotationNoiseText))]
        private double rotationNoiseMm = 0.005;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IncomingOffsetXText))]
        private double incomingOffsetXMm = 3.5;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IncomingOffsetYText))]
        private double incomingOffsetYMm = -2.5;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IncomingAngleErrorText))]
        private double incomingAngleErrorDeg = -12.0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FittedCenterText))]
        [NotifyPropertyChangedFor(nameof(FittedRadiusText))]
        [NotifyPropertyChangedFor(nameof(CircleRmseText))]
        [NotifyPropertyChangedFor(nameof(CenterTrueErrorText))]
        [NotifyPropertyChangedFor(nameof(IsCenterErrorHealthy))]
        [NotifyPropertyChangedFor(nameof(RotationSamples))]
        private CircleFitResult circleResult = null!;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CorrectCommandText))]
        [NotifyPropertyChangedFor(nameof(NaiveResidualText))]
        [NotifyPropertyChangedFor(nameof(AlignmentSummaryText))]
        private AlignmentCompensationResult? alignmentResult;

        public string ArmRadiusText => $"{ArmRadiusMm:F1} mm";
        public string RotationSpanText => $"±{RotationSpanDeg * 0.5:F0}° (跨度 {RotationSpanDeg:F0}°)";
        public string RotationNoiseText => $"{RotationNoiseMm:F3} mm";
        public string IncomingOffsetXText => $"{IncomingOffsetXMm:+0.0;-0.0;0.0} mm";
        public string IncomingOffsetYText => $"{IncomingOffsetYMm:+0.0;-0.0;0.0} mm";
        public string IncomingAngleErrorText => $"{IncomingAngleErrorDeg:+0.0;-0.0;0.0}°";

        public string FittedCenterText => CircleResult is { IsValid: true } c ? $"({c.CenterX:F3}, {c.CenterY:F3}) mm" : "—";
        public string FittedRadiusText => CircleResult is { IsValid: true } c ? $"{c.RadiusMm:F3} mm" : "—";
        public string CircleRmseText => CircleResult is { IsValid: true } c ? $"{c.RadialRmseMm:F4} mm" : "—";
        public string CenterTrueErrorText => CircleResult is { IsValid: true } c ? $"{c.TrueCenterErrorMm:F4} mm" : "—";
        public bool IsCenterErrorHealthy => CircleResult is { IsValid: true, TrueCenterErrorMm: <= 0.15 };
        public IReadOnlyList<RotationSamplePoint> RotationSamples => CircleResult?.Samples ?? [];

        public string CorrectCommandText => AlignmentResult is { } a
            ? $"Δθ={a.DeltaAngleDeg:+0.0;-0.0;0.0}°, ΔX={a.CorrectDeltaX:+0.00;-0.00;0.00}, ΔY={a.CorrectDeltaY:+0.00;-0.00;0.00}"
            : "—";

        public string NaiveResidualText => AlignmentResult is { } a ? $"{a.NaiveResidualErrorMm:F3} mm" : "—";
        public string AlignmentSummaryText => AlignmentResult?.Summary ?? string.Empty;

        #endregion

        partial void OnSelectedMountModeChanged(HandEyeModeOption value) { if (_isInitialized) RecalculateNinePoint(); }
        partial void OnScaleMmPerPxChanged(double value) { if (_isInitialized) RecalculateNinePoint(); }
        partial void OnInstallAngleDegChanged(double value) { if (_isInitialized) RecalculateNinePoint(); }
        partial void OnFlipImageYChanged(bool value) { if (_isInitialized) RecalculateNinePoint(); }
        partial void OnPixelNoiseSigmaPxChanged(double value) { if (_isInitialized) RecalculateNinePoint(); }
        partial void OnInjectOutlierOnPoint5Changed(bool value) { if (_isInitialized) RecalculateNinePoint(); }
        partial void OnEnableOutlierRejectionChanged(bool value) { if (_isInitialized) RecalculateNinePoint(); }

        partial void OnSelectedSampleCountChanged(RotationSampleCountOption value) { if (_isInitialized) RecalculateRotationCenter(); }
        partial void OnArmRadiusMmChanged(double value) { if (_isInitialized) RecalculateRotationCenter(); }
        partial void OnRotationSpanDegChanged(double value) { if (_isInitialized) RecalculateRotationCenter(); }
        partial void OnRotationNoiseMmChanged(double value) { if (_isInitialized) RecalculateRotationCenter(); }
        partial void OnIncomingOffsetXMmChanged(double value) { if (_isInitialized) RecalculateAlignment(); }
        partial void OnIncomingOffsetYMmChanged(double value) { if (_isInitialized) RecalculateAlignment(); }
        partial void OnIncomingAngleErrorDegChanged(double value) { if (_isInitialized) RecalculateAlignment(); }

        [RelayCommand]
        private void DemoOutlierInjection()
        {
            InjectOutlierOnPoint5 = true;
            EnableOutlierRejection = false;
        }

        [RelayCommand]
        private void DemoOutlierRejectionFix()
        {
            InjectOutlierOnPoint5 = true;
            EnableOutlierRejection = true;
        }

        [RelayCommand]
        private void DemoSmallSpanTrap()
        {
            SelectedSampleCount = SampleCountOptions[0]; // 3 points
            RotationSpanDeg = 8.0;                       // Tiny ±4° span magnifies noise massively!
            RotationNoiseMm = 0.05;
        }

        [RelayCommand]
        private void RestoreOptimalRotationCalibration()
        {
            SelectedSampleCount = SampleCountOptions[1]; // 5 points
            RotationSpanDeg = 60.0;
            RotationNoiseMm = 0.005;
        }

        [RelayCommand]
        private void ResetDefaults()
        {
            SelectedMountMode = MountModes[0];
            ScaleMmPerPx = 0.05;
            InstallAngleDeg = 12.5;
            FlipImageY = true;
            PixelNoiseSigmaPx = 0.15;
            InjectOutlierOnPoint5 = false;
            EnableOutlierRejection = false;
            TestPixelU = 1480.0;
            TestPixelV = 820.0;

            SelectedSampleCount = SampleCountOptions[1];
            ArmRadiusMm = 22.0;
            RotationSpanDeg = 60.0;
            RotationNoiseMm = 0.005;
            IncomingOffsetXMm = 3.5;
            IncomingOffsetYMm = -2.5;
            IncomingAngleErrorDeg = -12.0;
        }

        private void RecalculateNinePoint()
        {
            var pts = CalibrationEngine.GenerateNinePointDataset(
                SelectedMountMode.Mode, ScaleMmPerPx, InstallAngleDeg, FlipImageY,
                PixelNoiseSigmaPx, InjectOutlierOnPoint5);
            AffineResult = CalibrationEngine.SolveAffine(pts, EnableOutlierRejection, outlierThresholdMm: 0.25);
        }

        private void RecalculateRotationCenter()
        {
            var samples = CalibrationEngine.GenerateRotationSamples(
                TrueRotationCenterX, TrueRotationCenterY, ArmRadiusMm, TeachBaseAngleDeg,
                RotationSpanDeg, SelectedSampleCount.Count, RotationNoiseMm);
            CircleResult = CalibrationEngine.FitRotationCenter(samples, TrueRotationCenterX, TrueRotationCenterY);
            RecalculateAlignment();
        }

        private void RecalculateAlignment()
        {
            if (CircleResult is not { IsValid: true } c)
            {
                AlignmentResult = null;
                return;
            }

            var rad = TeachBaseAngleDeg * Math.PI / 180.0;
            var teachX = TrueRotationCenterX + ArmRadiusMm * Math.Cos(rad);
            var teachY = TrueRotationCenterY + ArmRadiusMm * Math.Sin(rad);

            var curX = teachX + IncomingOffsetXMm;
            var curY = teachY + IncomingOffsetYMm;
            var curAngle = TeachBaseAngleDeg + IncomingAngleErrorDeg;

            AlignmentResult = CalibrationEngine.ComputeAlignmentCompensation(
                teachX, teachY, TeachBaseAngleDeg, curX, curY, curAngle, c.CenterX, c.CenterY);
        }
    }
}
