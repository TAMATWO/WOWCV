using OpenCvSharp;
using WOWCV.Models;
using DrawingPoint = System.Drawing.Point;

namespace WOWCV.Services;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    /// <summary>
    /// 將 RGB 色彩轉換為可讀的三通道數值文字。
    /// </summary>
    public override string ToString() => $"({R}, {G}, {B})";
}

public enum PixelCheckStatus
{
    NotRun,
    Disabled,
    Passed,
    Failed,
    SizeMismatch,
    OutOfBounds
}

public sealed record PixelSampleResult(int Number, DrawingPoint TemplatePoint,
    DrawingPoint FramePoint, RgbColor? Expected, RgbColor? Actual, bool Matches);
public sealed record PixelVerificationResult(PixelCheckStatus Status, int MatchCount,
    IReadOnlyList<PixelSampleResult> Samples, string Reason)
{
    public bool AllowsHit => Status is PixelCheckStatus.Passed or PixelCheckStatus.Disabled;
    public static PixelVerificationResult NotRun { get; } = new(PixelCheckStatus.NotRun, 0,
        Array.Empty<PixelSampleResult>(), "候選未達門檻，未執行五點驗證");
}

/// <summary>Reads unmodified BGR pixels. It never resizes, clamps sample coordinates, or
/// draws.</summary>
public static class PixelVerificationService
{
    /// <summary>
    /// 在未縮放原圖與樣版上完整讀取五點，再套用 RGB 容差及最低通過點數。
    /// </summary>
    public static PixelVerificationResult Verify(Mat frame, Rectangle bounds, Mat template,
        PixelVerificationOptions options)
    {
        options.Validate();
        if (!options.Enabled)
            return new(PixelCheckStatus.Disabled, 0, [], "五點驗證已關閉");
        if (bounds.Width != template.Width || bounds.Height != template.Height)
            return new(
                PixelCheckStatus.SizeMismatch,
                0,
                [],
                $"尺寸不符：候選 {bounds.Width}×{bounds.Height}，樣版 {template.Width}×{template.Height}"
            );
        if (frame.Type() != MatType.CV_8UC3 || template.Type() != MatType.CV_8UC3)
            throw new ArgumentException("五點驗證需要原始 8-bit 三通道彩色影像。");
        if (!new Rectangle(0, 0, frame.Width, frame.Height).Contains(bounds))
            return new(PixelCheckStatus.OutOfBounds, 0, [], "候選框超出原始畫面");
        var samples = new List<PixelSampleResult>(5);
        bool invalid = false;
        foreach (var offset in options.Offsets)
        {
            // Use long arithmetic so arbitrary imported offsets cannot wrap into a valid
            // coordinate.
            long tx = template.Width / 2L + offset.X, ty = template.Height / 2L + offset.Y;
            if (tx < 0 || ty < 0 || tx >= template.Width || ty >= template.Height)
            {
                invalid = true;
                samples.Add(new(
                    samples.Count + 1,
                    new(Saturate(tx), Saturate(ty)),
                    new(Saturate(bounds.X + tx), Saturate(bounds.Y + ty)),
                    null,
                    null,
                    false
                ));
                continue;
            }

            var point = new DrawingPoint((int)tx, (int)ty);
            var framePoint = new DrawingPoint(bounds.X + point.X, bounds.Y + point.Y);
            var expected = Read(template, point);
            var actual = Read(frame, framePoint);
            bool matches = Math.Abs(expected.R - actual.R) <= options.RgbTolerance
                && Math.Abs(expected.G - actual.G) <= options.RgbTolerance
                && Math.Abs(expected.B - actual.B) <= options.RgbTolerance;
            samples.Add(new(samples.Count + 1, point, framePoint, expected, actual, matches));
        }

        int count = samples.Count(point => point.Matches);
        var status = invalid ? PixelCheckStatus.OutOfBounds : count >= options.MinimumMatches
            ? PixelCheckStatus.Passed : PixelCheckStatus.Failed;
        string reason = invalid ? "取樣點超出範圍，請調整五點位置"
            : $"RGB 符合 {count}/5；需要 {options.MinimumMatches}/5，容差 ±{options.RgbTolerance}";
        return new(status, count, samples.AsReadOnly(), reason);
    }

    /// <summary>
    /// 將計算後的座標限制在 32 位整數範圍，避免無效位置轉型溢位。
    /// </summary>
    private static int Saturate(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);

    /// <summary>
    /// 在呼叫端完成邊界檢查後，從原始彩色圖片讀取指定位置的 RGB。
    /// </summary>
    private static RgbColor Read(Mat image, DrawingPoint point)
    {
        var bgr = image.At<Vec3b>(point.Y, point.X);
        return new(bgr.Item2, bgr.Item1, bgr.Item0);
    }
}
