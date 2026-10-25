using WOWCV.Models;

namespace WOWCV.Services;

public enum SkillTrackingState { WaitingForLocation, Matched, NotMatched, Paused, InvalidBinding }

public sealed record SkillObservation(SkillImageBinding Binding, Rectangle? Bounds, SkillTrackingState State,
    PixelVerificationResult Verification, string Reason)
{
    public bool Found => State == SkillTrackingState.Matched;
}

public sealed record SkillObservationSnapshot(long SettingsVersion, long FrameId, long CaptureStartedTicks,
    DateTimeOffset CapturedAt, long AnalysisCompletedTicks, IReadOnlyList<SkillObservation> Items, Point Origin = default)
{
    public static SkillObservationSnapshot Empty { get; } = new(0, 0, 0, default, 0, Array.Empty<SkillObservation>());
}

/// <summary>Image-worker-only position memory. No timers, native resources, casting rules, or input output.</summary>
public sealed class SkillTrackingService
{
    private sealed record Context(string? SourceId, string? ImagePath, Rectangle ScreenBounds, Size Size,
        Rectangle? Roi, string? Folder, int TemplateRevision, long FlowRevision, long ResetRevision);
    private Context? context;
    private readonly Dictionary<string, Rectangle> anchors = new(StringComparer.Ordinal);

    public IReadOnlyList<SkillObservation> Update(Size imageSize, DetectionSettings settings, IReadOnlyList<DetectionRow> candidates,
        IReadOnlySet<string> loadedTemplates, Func<string, Rectangle, PixelVerificationOptions, PixelVerificationResult> verify,
        CancellationToken token = default)
    {
        var bindings = settings.SkillBindings ?? SkillBindingSnapshot.Empty;
        var current = new Context(settings.SourceIdentity, settings.ImagePath, settings.ScreenBounds, imageSize,
            settings.Roi, settings.TemplateFolder, settings.TemplateRevision, bindings.Revision, settings.TrackingResetRevision);
        if (current != context) { anchors.Clear(); context = current; }
        var options = settings.PixelVerification ?? new();
        var observations = new List<SkillObservation>();
        foreach (var binding in bindings.Items)
        {
            token.ThrowIfCancellationRequested();
            anchors.TryGetValue(binding.SkillName, out var saved);
            Rectangle? bounds = anchors.ContainsKey(binding.SkillName) ? saved : null;
            string? issue = binding.Issue ?? (!loadedTemplates.Contains(binding.TemplateFile) ? "樣版不存在或無法讀取，請載入正確的樣版資料夾" : null);
            if (issue is not null)
            {
                observations.Add(new(binding, null, SkillTrackingState.InvalidBinding, PixelVerificationResult.NotRun, issue)); continue;
            }
            if (!settings.Detecting || !options.Enabled)
            {
                string reason = !settings.Detecting ? "偵測已暫停" : "五點驗證已關閉，定位與持續驗證暫停";
                observations.Add(new(binding, bounds, SkillTrackingState.Paused,
                    new(PixelCheckStatus.NotRun, 0, [], reason), reason)); continue;
            }
            if (bounds is null)
            {
                var hit = candidates.FirstOrDefault(row => row.Found && row.PixelVerification.Status == PixelCheckStatus.Passed
                    && row.Template.Equals(binding.TemplateFile, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) { bounds = hit.Bounds; anchors[binding.SkillName] = hit.Bounds; }
            }
            if (bounds is null)
            {
                observations.Add(new(binding, null, SkillTrackingState.WaitingForLocation, PixelVerificationResult.NotRun,
                    "等待 OpenCV 與五點驗證首次命中")); continue;
            }
            var pixels = verify(binding.TemplateFile, bounds.Value, options);
            bool found = pixels.Status == PixelCheckStatus.Passed;
            observations.Add(new(binding, bounds, found ? SkillTrackingState.Matched : SkillTrackingState.NotMatched,
                pixels, pixels.Reason));
        }
        return observations.AsReadOnly();
    }
}
