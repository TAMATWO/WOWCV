using OpenCvSharp;
using WOWCV.Models;
using CvRect = OpenCvSharp.Rect;
using CvSize = OpenCvSharp.Size;

namespace WOWCV.Services;

public sealed record DetectionSettings(long Version, Rectangle ScreenBounds, string? ImagePath, string? TemplateFolder,
    double ProcessScale = 1.0, double Threshold = 0.8, bool MultiScale = false,
    bool Detecting = false, Rectangle? Roi = null, int TemplateRevision = 0,
    PixelVerificationOptions? PixelVerification = null);

public sealed record DetectionRow(string Template, bool Found, double Similarity, double Difference,
    double Scale, int X, int Y, int Width, int Height, double Mse, bool CandidatePassed,
    PixelVerificationResult PixelVerification, Rectangle Bounds, bool PositionRefined);

/// <summary>Used exclusively by the image worker. Native images never escape this service.</summary>
public sealed class TemplateMatchingService : IDisposable
{
    private sealed record Variant(double Scale, Mat Image);
    private sealed record Template(string Name, Mat Color, Mat Gray, List<Variant> Variants);
    private readonly List<Template> templates = [];
    private (string?, double, bool, int)? cacheKey;
    public string TemplateStatus { get; private set; } = "尚未載入樣版";

    private void Prepare(DetectionSettings settings, CancellationToken token)
    {
        var key = (settings.TemplateFolder, settings.ProcessScale, settings.MultiScale, settings.TemplateRevision);
        if (cacheKey == key) return;
        Clear();
        if (settings.TemplateFolder is null) { TemplateStatus = "尚未載入樣版"; cacheKey = key; return; }
        int skipped = 0;
        foreach (var path in Directory.EnumerateFiles(settings.TemplateFolder).Order(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            if (!new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" }.Contains(Path.GetExtension(path).ToLowerInvariant())) continue;
            try
            {
                using var color = Cv2.ImRead(path, ImreadModes.Color);
                if (color.Empty()) { skipped++; continue; }
                using var image = new Mat();
                // Use the same conversion as captured frames; decoder grayscale rounding can differ.
                Cv2.CvtColor(color, image, ColorConversionCodes.BGR2GRAY);
                var variants = new List<Variant>();
                templates.Add(new(Path.GetFileName(path), color.Clone(), image.Clone(), variants));
                Add(1);
                if (settings.MultiScale)
                    for (int i = 16; i <= 24; i++) if (i != 20) Add(i / 20.0);
                void Add(double scale)
                {
                    var resized = new Mat();
                    try
                    {
                        Cv2.Resize(image, resized, new CvSize(
                            Math.Max(1, (int)Math.Round(image.Width * scale * settings.ProcessScale)),
                            Math.Max(1, (int)Math.Round(image.Height * scale * settings.ProcessScale))),
                            0, 0, InterpolationFlags.Area);
                        variants.Add(new(scale, resized));
                    }
                    catch { resized.Dispose(); throw; }
                }
            }
            catch (OpenCVException) { skipped++; }
        }
        TemplateStatus = $"已載入 {templates.Count} 張樣版，略過 {skipped} 張";
        cacheKey = key;
    }

    public IReadOnlyList<DetectionRow> Match(Mat frame, DetectionSettings settings, CancellationToken token)
    {
        if (settings.ProcessScale is < 0.1 or > 1 || !double.IsFinite(settings.ProcessScale)
            || settings.Threshold is < 0 or > 1 || !double.IsFinite(settings.Threshold))
            throw new ArgumentException("處理倍率需為 0.1～1；門檻需為 0～1。");
        var pixelOptions = settings.PixelVerification ?? new PixelVerificationOptions();
        pixelOptions.Validate();
        Prepare(settings, token);
        if (!settings.Detecting) return [];
        var full = new Rectangle(0, 0, frame.Width, frame.Height);
        var roi = settings.Roi is { } selected ? Rectangle.Intersect(full, selected) : full;
        if (roi.Width < 1 || roi.Height < 1) return [];
        using var region = new Mat(frame, new CvRect(roi.X, roi.Y, roi.Width, roi.Height));
        using var gray = new Mat();
        using var small = new Mat();
        Cv2.CvtColor(region, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.Resize(gray, small, new CvSize(
            Math.Max(1, (int)Math.Round(roi.Width * settings.ProcessScale)),
            Math.Max(1, (int)Math.Round(roi.Height * settings.ProcessScale))), 0, 0, InterpolationFlags.Area);
        double sx = (double)small.Width / roi.Width, sy = (double)small.Height / roi.Height;
        var rows = new List<DetectionRow>();
        foreach (var template in templates)
        {
            token.ThrowIfCancellationRequested();
            (Variant Variant, OpenCvSharp.Point Point, double Difference)? best = null;
            foreach (var variant in template.Variants)
            {
                token.ThrowIfCancellationRequested();
                if (variant.Image.Width > small.Width || variant.Image.Height > small.Height) continue;
                using var scores = new Mat();
                Cv2.MatchTemplate(small, variant.Image, scores, TemplateMatchModes.SqDiffNormed);
                Cv2.MinMaxLoc(scores, out var min, out _, out var location, out _);
                if (!double.IsFinite(min)) continue;
                if (best is null || min < best.Value.Difference) best = (variant, location, min);
                // First variant is always 1:1; the other scales are only a fallback.
                if (variant.Scale == 1 && 1 - min >= settings.Threshold) break;
            }
            if (best is not { } match) continue;
            using var matched = new Mat(small, new CvRect(match.Point, match.Variant.Image.Size()));
            double mse = CalculateMse(matched, match.Variant.Image);
            // Native dimensions come from the original template, not rounded downsampled dimensions.
            int width = Math.Max(1, (int)Math.Round(template.Color.Width * match.Variant.Scale));
            int height = Math.Max(1, (int)Math.Round(template.Color.Height * match.Variant.Scale));
            int localX = (int)Math.Round(match.Point.X / sx), localY = (int)Math.Round(match.Point.Y / sy);
            bool refined = false;
            if (settings.ProcessScale < 1 && width <= roi.Width && height <= roi.Height)
            {
                token.ThrowIfCancellationRequested();
                // One downsampled pixel covers multiple source pixels. Search a bounded native neighborhood.
                int radiusX = (int)Math.Ceiling(1 / sx) + 1, radiusY = (int)Math.Ceiling(1 / sy) + 1;
                int left = Math.Max(0, localX - radiusX), top = Math.Max(0, localY - radiusY);
                int right = Math.Min(roi.Width - width, localX + radiusX);
                int bottom = Math.Min(roi.Height - height, localY + radiusY);
                if (right >= left && bottom >= top)
                {
                    using var nativeTemplate = new Mat();
                    Cv2.Resize(template.Gray, nativeTemplate, new CvSize(width, height), 0, 0, InterpolationFlags.Area);
                    using var neighborhood = new Mat(gray, new CvRect(left, top, right - left + width, bottom - top + height));
                    using var nativeScores = new Mat();
                    Cv2.MatchTemplate(neighborhood, nativeTemplate, nativeScores, TemplateMatchModes.SqDiffNormed);
                    Cv2.MinMaxLoc(nativeScores, out _, out _, out var nativeLocation, out _);
                    localX = left + nativeLocation.X; localY = top + nativeLocation.Y;
                    refined = true;
                }
            }
            int x = roi.X + localX, y = roi.Y + localY;
            var bounds = new Rectangle(x, y, width, height);
            double similarity = Math.Clamp(1 - match.Difference, 0, 1);
            bool candidatePassed = similarity >= settings.Threshold;
            var verification = !candidatePassed ? PixelVerificationResult.NotRun
                : !roi.Contains(bounds) ? new PixelVerificationResult(PixelCheckStatus.OutOfBounds, 0, [], "候選框超出 ROI")
                : PixelVerificationService.Verify(frame, bounds, template.Color, pixelOptions);
            bool found = candidatePassed && verification.AllowsHit;
            int ox = settings.ImagePath is null ? settings.ScreenBounds.X : 0;
            int oy = settings.ImagePath is null ? settings.ScreenBounds.Y : 0;
            rows.Add(new(template.Name, found, similarity, match.Difference, match.Variant.Scale,
                ox + x + width / 2, oy + y + height / 2, width, height, mse,
                candidatePassed, verification, bounds, refined));
        }
        // Finish all sampling before adding any rectangles/text, including overlapping templates.
        foreach (var row in rows.Where(row => row.CandidatePassed && full.Contains(row.Bounds)))
        {
            var color = row.Found ? Scalar.LimeGreen : Scalar.Yellow;
            Cv2.Rectangle(frame, new CvRect(row.Bounds.X, row.Bounds.Y, row.Width, row.Height), color, 2);
            Cv2.PutText(frame, $"{row.Template} {row.Similarity:0.000}",
                new OpenCvSharp.Point(row.Bounds.X, Math.Max(16, row.Bounds.Y - 6)), HersheyFonts.HersheySimplex, 0.5, color);
        }
        return rows.OrderByDescending(row => row.Similarity).ToArray();
    }

    public static double CalculateMse(Mat first, Mat second)
    {
        if (first.Size() != second.Size() || first.Type() != second.Type() || first.Empty())
            throw new ArgumentException("MSE 需要相同尺寸、型別的非空影像。");
        using var difference = new Mat(); using var floating = new Mat(); using var squared = new Mat();
        Cv2.Absdiff(first, second, difference);
        difference.ConvertTo(floating, MatType.CV_32F);
        Cv2.Multiply(floating, floating, squared);
        var sum = Cv2.Sum(squared);
        return (sum.Val0 + sum.Val1 + sum.Val2 + sum.Val3) / (first.Total() * first.Channels());
    }
    private void Clear()
    {
        foreach (var template in templates)
        {
            template.Color.Dispose(); template.Gray.Dispose();
            foreach (var variant in template.Variants) variant.Image.Dispose();
        }
        templates.Clear(); cacheKey = null;
    }
    public void Dispose() => Clear();
}
