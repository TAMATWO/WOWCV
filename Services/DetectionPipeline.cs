using OpenCvSharp;
using OpenCvSharp.Extensions;
using Size = System.Drawing.Size;

namespace WOWCV.Services;

/// <summary>Caller owns Preview after taking an output.</summary>
public sealed record DetectionOutput(long Version, Bitmap Preview, IReadOnlyList<DetectionRow> Rows,
    string Status, int SourcePixels, int ProcessPixels, double ElapsedMs, SkillObservationSnapshot Skills) : IDisposable
{ public void Dispose() => Preview.Dispose(); }

/// <summary>Two dedicated workers, a single pending frame and a single pending preview.
/// Ownership transfers on handoff; overwritten frames are disposed immediately.</summary>
public sealed class DetectionPipeline : IDisposable
{
    private sealed record Frame(Mat Image, DetectionSettings Settings, long Id, long CaptureStartedTicks, DateTimeOffset CapturedAt) : IDisposable
    { public void Dispose() => Image.Dispose(); }
    private readonly object gate = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly AutoResetEvent available = new(false);
    private readonly Thread captureThread, imageThread;
    private DetectionSettings settings = new(0, Rectangle.Empty, null, null);
    private Frame? pending;
    private DetectionOutput? output;
    private string? error;
    private bool started, disposed;
    private IReadOnlyList<DetectionRow> latestResults = Array.Empty<DetectionRow>();
    public IReadOnlyList<DetectionRow> LatestResults => Volatile.Read(ref latestResults);
    private SkillObservationSnapshot latestSkills = SkillObservationSnapshot.Empty;
    public SkillObservationSnapshot LatestSkills => Volatile.Read(ref latestSkills);
    /// <summary>Image-thread completion or invalidation notification. Handlers must return promptly.</summary>
    public event Action<SkillObservationSnapshot>? SkillsUpdated;
    public event Action<string>? Faulted;

    public DetectionPipeline()
    {
        captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "WOWCV Capture" };
        imageThread = new Thread(ProcessLoop) { IsBackground = true, Name = "WOWCV Image" };
    }
    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (started) return;
        started = true;
        captureThread.Start(); imageThread.Start();
    }
    public void Configure(DetectionSettings value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        value.PixelVerification?.Validate();
        lock (gate)
        {
            Volatile.Write(ref settings, value);
            pending?.Dispose(); pending = null; output?.Dispose(); output = null;
            Volatile.Write(ref latestResults, Array.Empty<DetectionRow>());
            Volatile.Write(ref latestSkills, SkillObservationSnapshot.Empty);
        }
        SkillsUpdated?.Invoke(SkillObservationSnapshot.Empty with { SettingsVersion = value.Version });
    }
    public DetectionOutput? TakeOutput() { lock (gate) { var value = output; output = null; return value; } }
    public string? TakeError() => Interlocked.Exchange(ref error, null);

    private void CaptureLoop()
    {
        long staticVersion = -1;
        long frameId = 0;
        while (!cancellation.IsCancellationRequested)
        {
            var current = Volatile.Read(ref settings);
            try
            {
                if (current.ImagePath is null || current.Version != staticVersion)
                {
                    long captureStartedTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                    var capturedAt = DateTimeOffset.UtcNow;
                    Mat image = current.ImagePath is { } path ? Cv2.ImRead(path, ImreadModes.Color)
                        : ScreenCaptureService.Capture(current.ScreenBounds);
                    if (image.Empty()) { image.Dispose(); throw new IOException("無法讀取來源圖片。"); }
                    var frame = new Frame(image, current, ++frameId, captureStartedTicks, capturedAt);
                    lock (gate)
                    {
                        if (cancellation.IsCancellationRequested || current.Version != Volatile.Read(ref settings).Version) frame.Dispose();
                        else { pending?.Dispose(); pending = frame; available.Set(); }
                    }
                    staticVersion = current.Version;
                }
            }
            catch (Exception ex)
            {
                string message = $"擷取失敗：{ex.Message}";
                bool currentFailure;
                lock (gate)
                {
                    currentFailure = current.Version == Volatile.Read(ref settings).Version;
                    if (currentFailure)
                    {
                        pending?.Dispose(); pending = null;
                        Volatile.Write(ref latestSkills, SkillObservationSnapshot.Empty);
                    }
                }
                if (currentFailure)
                {
                    Interlocked.Exchange(ref error, message);
                    SkillsUpdated?.Invoke(SkillObservationSnapshot.Empty with { SettingsVersion = current.Version });
                    Faulted?.Invoke(message);
                }
            }
            cancellation.Token.WaitHandle.WaitOne(100);
        }
    }

    private void ProcessLoop()
    {
        using var matcher = new TemplateMatchingService();
        var tracker = new SkillTrackingService();
        while (!cancellation.IsCancellationRequested)
        {
            available.WaitOne();
            if (cancellation.IsCancellationRequested) break;
            Frame? frame;
            lock (gate) { frame = pending; pending = null; }
            if (frame is null) continue;
            using (frame)
            {
                try
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var rows = matcher.Match(frame.Image, frame.Settings, cancellation.Token, annotate: false);
                    var observations = tracker.Update(new Size(frame.Image.Width, frame.Image.Height), frame.Settings, rows,
                        matcher.LoadedTemplateNames, (name, bounds, options) => matcher.VerifyAt(frame.Image, name, bounds, options), cancellation.Token);
                    var skills = new SkillObservationSnapshot(frame.Settings.Version, frame.Id, frame.CaptureStartedTicks,
                        frame.CapturedAt, System.Diagnostics.Stopwatch.GetTimestamp(), observations,
                        frame.Settings.ImagePath is null ? frame.Settings.ScreenBounds.Location : System.Drawing.Point.Empty);
                    TemplateMatchingService.DrawResults(frame.Image, rows);
                    var full = new Rectangle(0, 0, frame.Image.Width, frame.Image.Height);
                    var roi = frame.Settings.Roi is { } selected ? Rectangle.Intersect(full, selected) : full;
                    int sourcePixels = roi.Width * roi.Height;
                    int processPixels = Math.Max(1, (int)Math.Round(roi.Width * frame.Settings.ProcessScale))
                        * Math.Max(1, (int)Math.Round(roi.Height * frame.Settings.ProcessScale));
                    var value = new DetectionOutput(frame.Settings.Version, BitmapConverter.ToBitmap(frame.Image), rows,
                        matcher.TemplateStatus, sourcePixels, processPixels, watch.Elapsed.TotalMilliseconds, skills);
                    bool published = false;
                    lock (gate)
                    {
                        if (cancellation.IsCancellationRequested || frame.Settings.Version != Volatile.Read(ref settings).Version) value.Dispose();
                        else
                        {
                            output?.Dispose(); output = value;
                            Volatile.Write(ref latestResults, Array.AsReadOnly(rows.ToArray()));
                            Volatile.Write(ref latestSkills, skills);
                            published = true;
                        }
                    }
                    if (published) SkillsUpdated?.Invoke(skills);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref error, $"辨識失敗：{ex.Message}");
                    bool invalidated = false;
                    lock (gate)
                    {
                        if (frame.Settings.Version == Volatile.Read(ref settings).Version)
                        {
                            Volatile.Write(ref latestSkills, SkillObservationSnapshot.Empty);
                            Volatile.Write(ref latestResults, Array.Empty<DetectionRow>());
                            invalidated = true;
                        }
                    }
                    if (invalidated)
                    {
                        SkillsUpdated?.Invoke(SkillObservationSnapshot.Empty with { SettingsVersion = frame.Settings.Version });
                        Faulted?.Invoke($"辨識失敗：{ex.Message}");
                    }
                }
            }
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cancellation.Cancel(); available.Set();
        if (started) { captureThread.Join(); imageThread.Join(); }
        lock (gate) { pending?.Dispose(); output?.Dispose(); pending = null; output = null; }
        Volatile.Write(ref latestSkills, SkillObservationSnapshot.Empty);
        Volatile.Write(ref latestResults, Array.Empty<DetectionRow>());
        available.Dispose(); cancellation.Dispose();
    }
}
