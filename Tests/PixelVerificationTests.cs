using System.Text.Json;
using OpenCvSharp;
using WOWCV.Models;
using WOWCV.Services;

internal static class PixelVerificationTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var defaults = new PixelVerificationOptions();
        check(defaults.Enabled && defaults.RgbTolerance == 0 && defaults.MinimumMatches == 5, "Verifier defaults are enabled, zero tolerance, five matches");
        check(new DetectionSettings(1, Rectangle.Empty, null, null).ProcessScale == 1, "Service processing scale defaults to 1.00");
        var custom = defaults with { Enabled = false, RgbTolerance = 7, MinimumMatches = 3,
            Center = new(1, 2), Up = new(-1, -17), Down = new(0, 12), Left = new(-13, 1), Right = new(14, 2) };
        var serialized = PixelVerificationSettingsService.Serialize(custom);
        check(PixelVerificationSettingsService.Parse(serialized) == custom, "All verification settings round-trip independently");
        Reject(() => PixelVerificationSettingsService.Parse("{\"data\":[]}"), "Skill flow JSON cannot overwrite recognition settings");
        Reject(() => PixelVerificationSettingsService.Parse(serialized.Replace("\"rgbTolerance\": 7", "\"rgbTolerance\": 256")), "Invalid RGB tolerance is rejected");
        Reject(() => PixelVerificationSettingsService.Parse(serialized.Replace("\"minimumMatches\": 3", "\"minimumMatches\": 0")), "Invalid minimum match count is rejected");
        Reject(() => PixelVerificationSettingsService.Parse(serialized.Replace("\"version\": 1", "\"version\": 2")), "Unsupported settings version is rejected");

        using var template = new Mat(40, 40, MatType.CV_8UC3);
        Cv2.Randu(template, new Scalar(40, 40, 40), new Scalar(210, 210, 210));
        var bounds = new Rectangle(41, 33, 40, 40);
        using var raw = Frame(template, bounds.Location);
        using (var uiFrame = Frame(template, bounds.Location)) Cv2.ImWrite(Path.Combine(root, "pixel-ui.png"), uiFrame);
        var exact = PixelVerificationService.Verify(raw, bounds, template, defaults);
        check(exact.Status == PixelCheckStatus.Passed && exact.MatchCount == 5, "Identical original pixels pass all five points");
        var center = template.At<Vec3b>(20, 20);
        check(exact.Samples[0].Expected == new RgbColor(center.Item2, center.Item1, center.Item0), "BGR is converted correctly to displayed RGB");
        check(exact.Samples[1].TemplatePoint == new System.Drawing.Point(20, 10), "Default cross uses editable center-relative offsets");
        var changed = raw.At<Vec3b>(53, 61);
        changed.Item0 += 3;
        raw.Set(53, 61, changed);
        check(PixelVerificationService.Verify(raw, bounds, template, defaults) is { Status: PixelCheckStatus.Failed, MatchCount: 4 },
            "One altered pixel fails exact five-point verification");
        check(PixelVerificationService.Verify(raw, bounds, template, defaults with { RgbTolerance = 3 }).Status == PixelCheckStatus.Passed,
            "RGB tolerance includes its exact boundary");
        check(PixelVerificationService.Verify(raw, bounds, template, defaults with { RgbTolerance = 2 }).Status == PixelCheckStatus.Failed,
            "A single channel beyond tolerance fails that point");
        check(PixelVerificationService.Verify(raw, bounds, template, defaults with { MinimumMatches = 4 }).Status == PixelCheckStatus.Passed,
            "Minimum passing point count is independent of RGB tolerance");
        check(PixelVerificationService.Verify(raw, bounds, template, defaults with { Center = new(1, 0) }).Status == PixelCheckStatus.Passed,
            "Changing a sampling offset takes effect without hardcoded coordinates");
        check(PixelVerificationService.Verify(raw, bounds, template, defaults with { Right = new(int.MaxValue, 0), MinimumMatches = 1 }).Status == PixelCheckStatus.OutOfBounds,
            "Out-of-range sampling always fails, even with a low match count");
        check(PixelVerificationService.Verify(raw, bounds with { Width = 41 }, template, defaults).Status == PixelCheckStatus.SizeMismatch,
            "Different dimensions are rejected instead of resampling RGB");
        check(PixelVerificationService.Verify(raw, new Rectangle(-1, 0, 40, 40), template, defaults).Status == PixelCheckStatus.OutOfBounds,
            "Out-of-frame candidate is rejected safely");
        check(PixelVerificationService.Verify(raw, bounds, template, defaults with { Enabled = false }).Status == PixelCheckStatus.Disabled,
            "Disabling verification bypasses the pixel gate");

        string folder = Path.Combine(root, "pixel-templates"); Directory.CreateDirectory(folder);
        Cv2.ImWrite(Path.Combine(folder, "skill.png"), template);
        using var matcher = new TemplateMatchingService();
        var settings = new DetectionSettings(1, new Rectangle(-1920, -100, 140, 120), "static", folder,
            Detecting: true, Threshold: 0.8, PixelVerification: defaults);
        using (var frame = Frame(template, bounds.Location))
        {
            var row = matcher.Match(frame, settings, CancellationToken.None).Single();
            check(row.CandidatePassed && row.Found && !row.PositionRefined && row.Bounds == bounds,
                "Full-resolution candidate passes the independent RGB gate");
        }
        using (var frame = Frame(template, bounds.Location))
        {
            var pixel = frame.At<Vec3b>(53, 61); pixel.Item2 = (byte)(255 - pixel.Item2); frame.Set(53, 61, pixel);
            var row = matcher.Match(frame, settings, CancellationToken.None).Single();
            check(row.CandidatePassed && !row.Found && row.PixelVerification.MatchCount == 4,
                "High similarity alone cannot approve a pixel mismatch");
            check(frame.At<Vec3b>(33, 41) == new Vec3b(0, 255, 255), "Rejected candidate is rendered yellow");
        }
        using (var frame = Frame(template, bounds.Location))
        {
            frame.Set(38, 46, new Vec3b(0, 0, 0));
            check(matcher.Match(frame, settings, CancellationToken.None).Single().Found, "Changes away from sample positions leave the pixel gate unaffected");
        }
        foreach (double scale in new[] { 0.5, 0.25 })
        {
            using var frame = Frame(template, bounds.Location);
            var row = matcher.Match(frame, settings with { ProcessScale = scale, ImagePath = null,
                Roi = new Rectangle(20, 10, 99, 99) }, CancellationToken.None).Single();
            check(row.PositionRefined && row.Bounds == bounds && row.Found && row.PixelVerification.MatchCount == 5,
                $"Native refinement at processing scale {scale} restores odd-position samples inside ROI");
            check(row.X == -1859 && row.Y == -47, $"Refinement at scale {scale} preserves absolute desktop coordinates");
        }
        using (var frame = Frame(template, bounds.Location, 1.2))
        {
            var row = matcher.Match(frame, settings with { MultiScale = true, Threshold = 0.9999 }, CancellationToken.None).Single();
            check(row.CandidatePassed && !row.Found && row.PixelVerification.Status == PixelCheckStatus.SizeMismatch,
                "Multiscale candidate cannot bypass a native-size mismatch");
        }
        using (var frame = Frame(template, bounds.Location, 1.2))
            check(matcher.Match(frame, settings with { MultiScale = true, Threshold = 0.9999,
                PixelVerification = defaults with { Enabled = false } }, CancellationToken.None).Single().Found,
                "Multiscale still works as before when the pixel gate is disabled");

        // A later template samples the edge painted by the first candidate. Both must see raw pixels.
        Cv2.ImWrite(Path.Combine(folder, "skill-copy.png"), template);
        using (var frame = Frame(template, bounds.Location))
        {
            var rowSettings = settings with { TemplateRevision = 1, PixelVerification = defaults with { Center = new(-20, -20) } };
            var rows = matcher.Match(frame, rowSettings, CancellationToken.None);
            check(rows.Count == 2 && rows.All(row => row.Found && row.PixelVerification.MatchCount == 5),
                "Overlapping candidates are all sampled before any annotation is painted");
        }
        using (var frame = Frame(template, new System.Drawing.Point(0, 0)))
        {
            var row = matcher.Match(frame, settings with { ProcessScale = 0.5 }, CancellationToken.None).First();
            check(row.Bounds.Location == System.Drawing.Point.Empty && row.Found,
                "Native refinement is safely clipped at image and ROI edges");
        }

        void Reject(Action action, string name)
        {
            try { action(); } catch (JsonException) { check(true, name); return; }
            check(false, name);
        }
    }

    private static Mat Frame(Mat template, System.Drawing.Point location, double scale = 1)
    {
        var frame = new Mat(120, 140, MatType.CV_8UC3, new Scalar(15, 15, 15));
        using var image = new Mat();
        Cv2.Resize(template, image, new OpenCvSharp.Size((int)Math.Round(template.Width * scale), (int)Math.Round(template.Height * scale)),
            0, 0, InterpolationFlags.Area);
        using var region = new Mat(frame, new Rect(location.X, location.Y, image.Width, image.Height));
        image.CopyTo(region); return frame;
    }
}
