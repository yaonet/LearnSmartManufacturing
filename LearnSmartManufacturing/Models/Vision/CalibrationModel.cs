using System.Globalization;

namespace LearnSmartManufacturing.Models.Vision
{
    public enum HandEyeMountMode
    {
        EyeToHand, // 固定相机（眼在手外）
        EyeInHand, // 移动相机（眼在手上）
    }

    public sealed record CalibrationPointPair(
        int Index,
        double PixelU,
        double PixelV,
        double WorldX,
        double WorldY,
        double PredictedX = 0,
        double PredictedY = 0,
        double ResidualMm = 0,
        bool IsOutlier = false)
    {
        public string PixelText => $"({PixelU:F1}, {PixelV:F1})";
        public string WorldText => $"({WorldX:F3}, {WorldY:F3})";
        public string ResidualText => $"{ResidualMm:F4} mm";
    }

    public sealed record AffineCalibrationResult(
        bool IsValid,
        string ErrorMessage,
        double A,
        double B,
        double C,
        double D,
        double E,
        double F,
        double ScaleXMmPerPx,
        double ScaleYMmPerPx,
        double RotationDeg,
        double ShearDeg,
        bool IsMirroredHandedness,
        double RmseMm,
        double MaxErrorMm,
        int UsedPointCount,
        IReadOnlyList<CalibrationPointPair> EvaluatedPoints)
    {
        public (double WorldX, double WorldY) TransformPixelToWorld(double u, double v) =>
            (A * u + B * v + C, D * u + E * v + F);

        public bool TryTransformWorldToPixel(double x, double y, out double u, out double v)
        {
            var det = A * E - B * D;
            if (Math.Abs(det) < 1e-12)
            {
                u = v = 0;
                return false;
            }

            var dx = x - C;
            var dy = y - F;
            u = (E * dx - B * dy) / det;
            v = (-D * dx + A * dy) / det;
            return true;
        }
    }

    public sealed record RotationSamplePoint(int Index, double AngleDeg, double X, double Y, double RadialResidualMm = 0)
    {
        public string AngleText => $"{AngleDeg:+0.0;-0.0;0.0}°";
        public string CoordText => $"({X:F3}, {Y:F3})";
        public string ResidualText => $"{RadialResidualMm:F4} mm";
    }

    public sealed record CircleFitResult(
        bool IsValid,
        string ErrorMessage,
        double CenterX,
        double CenterY,
        double RadiusMm,
        double RadialRmseMm,
        double TrueCenterErrorMm,
        IReadOnlyList<RotationSamplePoint> Samples);

    public sealed record AlignmentCompensationResult(
        double TeachX,
        double TeachY,
        double TeachAngleDeg,
        double CurrentX,
        double CurrentY,
        double CurrentAngleDeg,
        double CenterX,
        double CenterY,
        double DeltaAngleDeg,
        double RotatedX,
        double RotatedY,
        double CorrectDeltaX,
        double CorrectDeltaY,
        double NaiveDeltaX,
        double NaiveDeltaY,
        double NaiveFinalX,
        double NaiveFinalY,
        double NaiveResidualErrorMm,
        string Summary);

    /// <summary>
    /// Pure linear-algebra engine for industrial vision-motion calibration:
    /// - 9-Point 2D Affine Hand-Eye Calibration (Ordinary Least Squares via 3x3 normal equations + outlier rejection)
    /// - Multi-point Rotation Center Calibration (Kåsa algebraic least-squares circle fit)
    /// - Rotate-then-Translate visual alignment compensation &amp; naive-offset error comparison
    /// </summary>
    public static class CalibrationEngine
    {
        /// <summary>
        /// Generates a realistic 3×3 calibration point grid in pixel (2448×2048 sensor) and mechanical world coordinates.
        /// </summary>
        public static IReadOnlyList<CalibrationPointPair> GenerateNinePointDataset(
            HandEyeMountMode mode,
            double scaleMmPerPx,
            double installAngleDeg,
            bool flipImageY,
            double pixelNoiseSigmaPx,
            bool injectOutlierAtPoint5,
            int seed = 9001)
        {
            var rng = new Random(seed);
            const double cx = 1224.0;
            const double cy = 1024.0;
            const double worldOriginX = 120.0;
            const double worldOriginY = 85.0;

            var rad = installAngleDeg * Math.PI / 180.0;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);
            var ySign = flipImageY ? -1.0 : 1.0;
            var dirSign = mode == HandEyeMountMode.EyeInHand ? -1.0 : 1.0;

            double[] gridSteps = [-520.0, 0.0, 520.0];
            var list = new List<CalibrationPointPair>(9);
            var idx = 1;

            foreach (var dv in gridSteps)
            {
                foreach (var du in gridSteps)
                {
                    var trueU = cx + du;
                    var trueV = cy + dv;

                    // Apply true scale, Y-axis flip, installation rotation, and Eye-to-Hand / Eye-in-Hand sign
                    var sx = du * scaleMmPerPx * dirSign;
                    var sy = dv * scaleMmPerPx * ySign * dirSign;
                    var wx = worldOriginX + sx * cos - sy * sin;
                    var wy = worldOriginY + sx * sin + sy * cos;

                    // Add deterministic pseudo-Gaussian sub-pixel extraction noise
                    var noiseU = NextNormal(rng) * pixelNoiseSigmaPx;
                    var noiseV = NextNormal(rng) * pixelNoiseSigmaPx;

                    var measuredU = trueU + noiseU;
                    var measuredV = trueV + noiseV;

                    if (injectOutlierAtPoint5 && idx == 5)
                    {
                        // Simulate wrong blob match / dust interference on center point #5
                        measuredU += 28.0;
                        measuredV -= 22.0;
                    }

                    list.Add(new CalibrationPointPair(idx, Math.Round(measuredU, 2), Math.Round(measuredV, 2),
                        Math.Round(wx, 4), Math.Round(wy, 4)));
                    idx++;
                }
            }

            return list;
        }

        /// <summary>
        /// Solves 6-DOF 2D Affine transformation [X; Y] = [a b c; d e f] * [u; v; 1] using Ordinary Least Squares.
        /// Optionally performs 1-pass iterative outlier rejection (RANSAC-lite residual trimming) when <paramref name="rejectOutliers"/> is true.
        /// </summary>
        public static AffineCalibrationResult SolveAffine(
            IReadOnlyList<CalibrationPointPair> points,
            bool rejectOutliers = false,
            double outlierThresholdMm = 0.25)
        {
            if (points.Count < 3)
            {
                return InvalidAffine("至少需要 3 个不共线标定点才能求解 6 自由度仿射矩阵。");
            }

            var initial = SolveAffineSubset(points, Enumerable.Repeat(true, points.Count).ToArray());
            if (!initial.IsValid || !rejectOutliers)
            {
                return initial;
            }

            // Reject points exceeding outlierThresholdMm (as long as >= 3 inliers remain)
            var inlierMask = new bool[points.Count];
            var inlierCount = 0;
            for (var i = 0; i < initial.EvaluatedPoints.Count; i++)
            {
                var isInlier = initial.EvaluatedPoints[i].ResidualMm <= outlierThresholdMm;
                inlierMask[i] = isInlier;
                if (isInlier) inlierCount++;
            }

            if (inlierCount >= 3 && inlierCount < points.Count)
            {
                return SolveAffineSubset(points, inlierMask);
            }

            return initial;
        }

        private static AffineCalibrationResult SolveAffineSubset(IReadOnlyList<CalibrationPointPair> allPoints, bool[] useMask)
        {
            // Normal equations for A * [a, b, c]^T = X and A * [d, e, f]^T = Y where row_i = [u_i, v_i, 1]
            double sUU = 0, sUV = 0, sU = 0, sVV = 0, sV = 0;
            double sUX = 0, sVX = 0, sX = 0;
            double sUY = 0, sVY = 0, sY = 0;
            var n = 0;

            for (var i = 0; i < allPoints.Count; i++)
            {
                if (!useMask[i]) continue;
                var p = allPoints[i];
                sUU += p.PixelU * p.PixelU;
                sUV += p.PixelU * p.PixelV;
                sU += p.PixelU;
                sVV += p.PixelV * p.PixelV;
                sV += p.PixelV;

                sUX += p.PixelU * p.WorldX;
                sVX += p.PixelV * p.WorldX;
                sX += p.WorldX;

                sUY += p.PixelU * p.WorldY;
                sVY += p.PixelV * p.WorldY;
                sY += p.WorldY;
                n++;
            }

            if (n < 3)
            {
                return InvalidAffine("有效内点数量少于 3 个，无法构成满秩方程。");
            }

            double[,] ata =
            {
                { sUU, sUV, sU },
                { sUV, sVV, sV },
                { sU, sV, n },
            };

            if (!TrySolveLinear3x3(ata, [sUX, sVX, sX], out var solX) ||
                !TrySolveLinear3x3(ata, [sUY, sVY, sY], out var solY))
            {
                return InvalidAffine("标定点共线或坐标奇异（矩阵不可逆），请确保标定点在视野内呈 2D 网格分布。");
            }

            double a = solX[0], b = solX[1], c = solX[2];
            double d = solY[0], e = solY[1], f = solY[2];

            var scaleX = double.Hypot(a, d);
            var scaleY = double.Hypot(b, e);
            var rotDeg = Math.Atan2(d, a) * (180.0 / Math.PI);
            var det = a * e - b * d;
            var mirrored = det < 0;

            // Orthogonality / shear deviation from 90 deg between transformed U and V basis vectors
            var dot = a * b + d * e;
            var cosAngle = Math.Clamp(dot / Math.Max(1e-12, scaleX * scaleY), -1.0, 1.0);
            var basisAngleDeg = Math.Acos(cosAngle) * (180.0 / Math.PI);
            var shearDeg = Math.Abs(90.0 - basisAngleDeg);

            var evaluated = new List<CalibrationPointPair>(allPoints.Count);
            double sumSq = 0;
            double maxErr = 0;
            var usedCount = 0;

            for (var i = 0; i < allPoints.Count; i++)
            {
                var pt = allPoints[i];
                var px = a * pt.PixelU + b * pt.PixelV + c;
                var py = d * pt.PixelU + e * pt.PixelV + f;
                var res = double.Hypot(px - pt.WorldX, py - pt.WorldY);
                var isOutlier = !useMask[i] || res > 0.25;

                evaluated.Add(pt with
                {
                    PredictedX = Math.Round(px, 4),
                    PredictedY = Math.Round(py, 4),
                    ResidualMm = Math.Round(res, 5),
                    IsOutlier = isOutlier,
                });

                if (useMask[i])
                {
                    sumSq += res * res;
                    maxErr = Math.Max(maxErr, res);
                    usedCount++;
                }
            }

            var rmse = Math.Sqrt(sumSq / Math.Max(1, usedCount));

            return new AffineCalibrationResult(
                true, string.Empty,
                a, b, c, d, e, f,
                scaleX, scaleY, rotDeg, shearDeg, mirrored,
                rmse, maxErr, usedCount, evaluated);
        }

        /// <summary>Generates N rotation-center calibration samples around a true mechanical center.</summary>
        public static IReadOnlyList<RotationSamplePoint> GenerateRotationSamples(
            double trueCenterX,
            double trueCenterY,
            double armRadiusMm,
            double baseAngleDeg,
            double spanDeg,
            int sampleCount,
            double measurementNoiseMm,
            int seed = 3601)
        {
            var count = Math.Clamp(sampleCount, 3, 12);
            var rng = new Random(seed);
            var list = new List<RotationSamplePoint>(count);

            for (var i = 0; i < count; i++)
            {
                var t = count == 1 ? 0.0 : -0.5 + i / (double)(count - 1);
                var angleDeg = t * spanDeg;
                var totalRad = (baseAngleDeg + angleDeg) * Math.PI / 180.0;
                var x = trueCenterX + armRadiusMm * Math.Cos(totalRad) + NextNormal(rng) * measurementNoiseMm;
                var y = trueCenterY + armRadiusMm * Math.Sin(totalRad) + NextNormal(rng) * measurementNoiseMm;
                list.Add(new RotationSamplePoint(i + 1, Math.Round(angleDeg, 2), Math.Round(x, 4), Math.Round(y, 4)));
            }

            return list;
        }

        /// <summary>
        /// Fits a circle (rotation center Cx, Cy and radius R) via exact algebraic least squares (Kåsa method):
        /// 2*Cx*x + 2*Cy*y + (R^2 - Cx^2 - Cy^2) = x^2 + y^2.
        /// </summary>
        public static CircleFitResult FitRotationCenter(
            IReadOnlyList<RotationSamplePoint> samples,
            double? groundTruthCx = null,
            double? groundTruthCy = null)
        {
            if (samples.Count < 3)
            {
                return new CircleFitResult(false, "旋转中心标定至少需要 3 个不同旋转角度的采点。", 0, 0, 0, 0, 0, samples);
            }

            // Mean-center coordinates before forming normal equations for optimal numerical conditioning
            double meanX = 0, meanY = 0;
            foreach (var p in samples)
            {
                meanX += p.X;
                meanY += p.Y;
            }

            meanX /= samples.Count;
            meanY /= samples.Count;

            double sXX = 0, sXY = 0, sX = 0, sYY = 0, sY = 0;
            double sXR = 0, sYR = 0, sR = 0;
            var n = samples.Count;

            foreach (var p in samples)
            {
                var ux = p.X - meanX;
                var uy = p.Y - meanY;
                var r2 = ux * ux + uy * uy;
                sXX += ux * ux;
                sXY += ux * uy;
                sX += ux;
                sYY += uy * uy;
                sY += uy;
                sXR += ux * r2;
                sYR += uy * r2;
                sR += r2;
            }

            double[,] m =
            {
                { sXX, sXY, sX },
                { sXY, sYY, sY },
                { sX, sY, n },
            };

            if (!TrySolveLinear3x3(m, [sXR, sYR, sR], out var sol))
            {
                return new CircleFitResult(false, "旋转采样点近似共线（旋转跨度过小或偏心半径为 0），无法唯一确定圆心。", 0, 0, 0, 0, 0, samples);
            }

            var ucx = sol[0] * 0.5;
            var ucy = sol[1] * 0.5;
            var rSq = sol[2] + ucx * ucx + ucy * ucy;
            var cx = meanX + ucx;
            var cy = meanY + ucy;
            if (!double.IsFinite(rSq) || rSq <= 0)
            {
                return new CircleFitResult(false, "拟合半径无效，请增大旋转角度跨度或检查采点坐标。", 0, 0, 0, 0, 0, samples);
            }

            var radius = Math.Sqrt(rSq);
            var evaluated = new List<RotationSamplePoint>(n);
            double sumSq = 0;
            foreach (var p in samples)
            {
                var dist = double.Hypot(p.X - cx, p.Y - cy);
                var err = Math.Abs(dist - radius);
                sumSq += err * err;
                evaluated.Add(p with { RadialResidualMm = Math.Round(err, 5) });
            }

            var rmse = Math.Sqrt(sumSq / n);
            var trueErr = groundTruthCx is double tcx && groundTruthCy is double tcy
                ? double.Hypot(cx - tcx, cy - tcy)
                : 0.0;

            return new CircleFitResult(true, string.Empty, cx, cy, radius, rmse, trueErr, evaluated);
        }

        /// <summary>
        /// Computes exact Rotate-then-Translate visual alignment compensation vs naive offset ignoring eccentric rotation center.
        /// </summary>
        public static AlignmentCompensationResult ComputeAlignmentCompensation(
            double teachX,
            double teachY,
            double teachAngleDeg,
            double currentX,
            double currentY,
            double currentAngleDeg,
            double centerX,
            double centerY)
        {
            var deltaTheta = teachAngleDeg - currentAngleDeg;
            var rad = deltaTheta * Math.PI / 180.0;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);

            // Step 1: Rotate current mark around mechanical rotation center (Cx, Cy) by deltaTheta
            var dx0 = currentX - centerX;
            var dy0 = currentY - centerY;
            var rotX = centerX + dx0 * cos - dy0 * sin;
            var rotY = centerY + dx0 * sin + dy0 * cos;

            // Step 2: Translate from rotated position to teach position -> 0.000 mm residual!
            var corrDx = teachX - rotX;
            var corrDy = teachY - rotY;

            // Contrast with NAIVE mistake: translating by (Teach - Current) without accounting for rotation swing
            var naiveDx = teachX - currentX;
            var naiveDy = teachY - currentY;
            var naiveFinalX = rotX + naiveDx;
            var naiveFinalY = rotY + naiveDy;
            var naiveErr = double.Hypot(naiveFinalX - teachX, naiveFinalY - teachY);

            var summary = string.Create(CultureInfo.InvariantCulture,
                $"① 先转 R 轴 {deltaTheta:+0.0;-0.0;0.0}° → Mark 绕圆心摆动至 ({rotX:F2}, {rotY:F2})；② 再补平移 ({corrDx:+0.00;-0.00;0.00}, {corrDy:+0.00;-0.00;0.00}) mm 精准重合！若忽略旋转中心直接补差，将残余 {naiveErr:F2} mm 严重贴偏！");

            return new AlignmentCompensationResult(
                teachX, teachY, teachAngleDeg, currentX, currentY, currentAngleDeg,
                centerX, centerY, deltaTheta, rotX, rotY,
                corrDx, corrDy, naiveDx, naiveDy, naiveFinalX, naiveFinalY, naiveErr, summary);
        }

        /// <summary>Solves 3×3 linear system M * x = rhs via Gaussian elimination with partial pivoting.</summary>
        private static bool TrySolveLinear3x3(double[,] m, double[] rhs, out double[] solution)
        {
            solution = new double[3];
            var a = new double[3, 4];
            for (var r = 0; r < 3; r++)
            {
                for (var c = 0; c < 3; c++) a[r, c] = m[r, c];
                a[r, 3] = rhs[r];
            }

            for (var col = 0; col < 3; col++)
            {
                var pivot = col;
                var maxAbs = Math.Abs(a[col, col]);
                for (var r = col + 1; r < 3; r++)
                {
                    if (Math.Abs(a[r, col]) > maxAbs)
                    {
                        maxAbs = Math.Abs(a[r, col]);
                        pivot = r;
                    }
                }

                if (maxAbs < 1e-10)
                {
                    return false;
                }

                if (pivot != col)
                {
                    for (var c = col; c < 4; c++)
                    {
                        (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
                    }
                }

                for (var r = col + 1; r < 3; r++)
                {
                    var factor = a[r, col] / a[col, col];
                    for (var c = col; c < 4; c++)
                    {
                        a[r, c] -= factor * a[col, c];
                    }
                }
            }

            for (var r = 2; r >= 0; r--)
            {
                var sum = a[r, 3];
                for (var c = r + 1; c < 3; c++) sum -= a[r, c] * solution[c];
                solution[r] = sum / a[r, r];
                if (!double.IsFinite(solution[r])) return false;
            }

            return true;
        }

        private static double NextNormal(Random rng)
        {
            // Box-Muller transform
            var u1 = Math.Max(1e-12, 1.0 - rng.NextDouble());
            var u2 = 1.0 - rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        private static AffineCalibrationResult InvalidAffine(string msg) => new(
            false, msg, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, 0, []);
    }
}
