using System.Diagnostics;
using System.Text.Json;
using OpenCvSharp;
using WOWCV;
using WOWCV.Controls;
using WOWCV.Models;
using WOWCV.Services;

internal static class Program
{
    private static int checks;
    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        Cv2.SetNumThreads(1);
        // Keep artifacts isolated and inspectable; validation never deletes existing files.
        string root = Path.Combine(AppContext.BaseDirectory, "artifacts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            TestConfiguration();
            TestSlicing(root);
            TestMatching(root);
            PixelVerificationTests.Run(root, Check);
            TestPipeline(root);
            TestRawInput();
            TestRoiSelection();
            TestTabs(root);
            TestPixelResultsUi(root);
            Console.WriteLine($"PASS: {checks} checks. Artifacts: {root}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        checks++; Console.WriteLine($"PASS {message}");
    }
    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); } catch (T) { Check(true, message); return; }
        throw new InvalidOperationException(message);
    }
    private static void TestConfiguration()
    {
        const string json = """
        {"data":[{"press":"E","process":[{"skillname":"技能","status":"Y","priority":2,"cd_sec":1.25,"skillweight":80,"cooldown_check":"冷卻","custom":42}]}],"version":7}
        """;
        var store = new RuntimeConfiguration();
        Check(store.Read() is null, "Configuration starts empty");
        store.Import(json);
        var first = store.Read()!;
        first.Data[0].Process[0].SkillName = "changed";
        Check(store.Read()!.Data[0].Process[0].SkillName == "技能", "Configuration readers cannot mutate cached snapshot");
        Check(store.Read()!.Extra!["version"].GetInt32() == 7, "Unknown document fields preserved");
        Check(store.Read()!.Data[0].Process[0].Extra!["custom"].GetInt32() == 42, "Unknown skill fields preserved");
        Throws<JsonException>(() => store.Import("{\"data\":null}"), "Invalid import rejected");
        Check(store.Read()!.Data[0].Press == "E", "Failed import preserves previous snapshot");
        Throws<JsonException>(() => store.Import(json.Replace("\"Y\"", "\"Z\"")), "Invalid status rejected");
        Throws<JsonException>(() => store.Import(json.Replace("1.25", "-1")), "Negative cooldown rejected");
        Check(SkillDocumentService.Parse(SkillDocumentService.Serialize(store.Read()!)).Data[0].Process[0].CdSec == 1.25m,
            "JSON/TXT content round-trips without precision loss");
    }
    private static void TestSlicing(string root)
    {
        var roi = new Rectangle(2, 3, 101, 41);
        var cells = SkillSlicingService.GetCells(roi, 2, 3);
        Check(cells.Count == 6 && cells.Sum(cell => cell.Width * cell.Height) == roi.Width * roi.Height, "Uneven grid covers every pixel");
        Check(cells[0].Location == roi.Location && cells[5].Right == roi.Right && cells[5].Bottom == roi.Bottom, "Grid respects ROI edges");
        Throws<ArgumentException>(() => SkillSlicingService.GetCells(roi, 100, 3), "Subpixel cells rejected");
        using var source = new Mat(50, 110, MatType.CV_8UC3, new Scalar(20, 80, 160));
        var folder = Path.Combine(root, "exports"); Directory.CreateDirectory(folder);
        string sentinel = Path.Combine(folder, "keep.txt"); File.WriteAllText(sentinel, "keep");
        string output = SkillSlicingService.Export(source, roi, 2, 3, folder, CancellationToken.None);
        string second = SkillSlicingService.Export(source, roi, 2, 3, folder, CancellationToken.None);
        Check(output != second && File.ReadAllText(sentinel) == "keep", "Exports use distinct directories and preserve existing files");
        Check(Directory.GetFiles(output, "*.png").Length == 6, "Exports all row/column PNG files");
        using var saved = Cv2.ImRead(Path.Combine(output, "Skill_R2_C3.png"));
        Check(saved.Width == cells[5].Width && saved.Height == cells[5].Height, "Exported cell dimensions match preview");
        Throws<OperationCanceledException>(() => SkillSlicingService.Export(source, roi, 2, 3, folder, new CancellationToken(true)), "Export observes cancellation");
    }
    private static string TemplateFolder(string root) => Path.Combine(root, "templates");
    private static Mat MakeTemplate()
    {
        var image = new Mat(20, 20, MatType.CV_8UC3);
        Cv2.Randu(image, new Scalar(40, 40, 40), new Scalar(240, 240, 240));
        return image;
    }
    private static Mat MakeFrame(Mat template, double scale = 1)
    {
        var frame = new Mat(100, 100, MatType.CV_8UC3, new Scalar(15, 15, 15));
        using var resized = new Mat();
        Cv2.Resize(template, resized, new OpenCvSharp.Size((int)(20 * scale), (int)(20 * scale)), 0, 0, InterpolationFlags.Area);
        using var region = new Mat(frame, new Rect(40, 30, resized.Width, resized.Height)); resized.CopyTo(region);
        return frame;
    }
    private static void TestMatching(string root)
    {
        string folder = TemplateFolder(root); Directory.CreateDirectory(folder);
        using var template = MakeTemplate(); Cv2.ImWrite(Path.Combine(folder, "skill.png"), template);
        File.WriteAllText(Path.Combine(folder, "broken.png"), "not an image");
        using var matcher = new TemplateMatchingService();
        var settings = new DetectionSettings(1, new Rectangle(-1920, -100, 100, 100), "static", folder,
            ProcessScale: 1, Threshold: 0.9999, Detecting: true, PixelVerification: new() { Enabled = false });
        using (var frame = MakeFrame(template))
        {
            var row = matcher.Match(frame, settings, CancellationToken.None).Single();
            Check(row.Found && row.Scale == 1 && row.Mse < 0.001, "Original-size match and MSE are exact");
            Check(row.X == 50 && row.Y == 40 && row.Width == 20 && row.Height == 20, "Static image center and dimensions correct");
        }
        using (var frame = MakeFrame(template))
        {
            var row = matcher.Match(frame, settings with { ImagePath = null, ProcessScale = 0.5, Roi = new Rectangle(20, 10, 60, 60) }, CancellationToken.None).Single();
            Check(row.Found && row.X == -1870 && row.Y == -60, "ROI and processing scale restore negative desktop coordinates");
        }
        using (var frame = MakeFrame(template, 1.2))
        {
            var row = matcher.Match(frame, settings with { MultiScale = true }, CancellationToken.None).Single();
            Check(row.Found && Math.Abs(row.Scale - 1.2) < 0.001 && row.Width == 24, "Multiscale fallback finds enlarged target");
        }
        using (var frame = MakeFrame(template))
            Check(matcher.Match(frame, settings with { MultiScale = true }, CancellationToken.None).Single().Scale == 1,
                "Successful original match keeps original scale");
        using (var frame = new Mat(100, 100, MatType.CV_8UC3, new Scalar(15, 15, 15)))
            Check(!matcher.Match(frame, settings, CancellationToken.None).Single().Found, "Below-threshold candidate is not a hit");
        using (var frame = MakeFrame(template))
            Check(matcher.Match(frame, settings with { Roi = new Rectangle(0, 0, 10, 10) }, CancellationToken.None).Count == 0,
                "Oversized templates are skipped safely");
        using var a = new Mat(3, 3, MatType.CV_8UC3, new Scalar(10, 10, 10));
        using var b = new Mat(3, 3, MatType.CV_8UC3, new Scalar(20, 20, 20));
        Check(Math.Abs(TemplateMatchingService.CalculateMse(a, b) - 100) < 0.001, "MSE accounts for all color channels");
    }
    private static DetectionOutput WaitOutput(DetectionPipeline pipeline, long version)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 5000)
        {
            var output = pipeline.TakeOutput();
            if (output?.Version == version) return output;
            output?.Dispose(); Thread.Sleep(10);
        }
        throw new InvalidOperationException($"Pipeline did not produce version {version}: {pipeline.TakeError()}");
    }
    private static void TestPipeline(string root)
    {
        string path = Path.Combine(root, "frame.png");
        using (var template = Cv2.ImRead(Path.Combine(TemplateFolder(root), "skill.png")))
        using (var image = MakeFrame(template)) Cv2.ImWrite(path, image);
        using var pipeline = new DetectionPipeline();
        var settings = new DetectionSettings(1, Rectangle.Empty, path, TemplateFolder(root), ProcessScale: 1, Detecting: true,
            PixelVerification: new() { Up = new(0, -5), Down = new(0, 5), Left = new(-5, 0), Right = new(5, 0) });
        pipeline.Configure(settings); pipeline.Start();
        using (var output = WaitOutput(pipeline, 1)) Check(output.Rows.Single().Found, "Background pipeline produces static detection");
        Check(pipeline.LatestResults.Count == 1, "Results remain available independently of UI preview consumption");
        for (long v = 2; v <= 100; v++) pipeline.Configure(settings with { Version = v });
        using (var output = WaitOutput(pipeline, 100)) Check(output.Version == 100, "Rapid reconfiguration discards stale frames");
        pipeline.Configure(settings with { Version = 101, Detecting = false });
        Check(pipeline.LatestResults.Count == 0, "Reconfiguration clears previous results");
        using (var output = WaitOutput(pipeline, 101)) Check(output.Rows.Count == 0, "Stopping detection preserves preview without results");
        pipeline.Configure(settings with { Version = 102, PixelVerification = settings.PixelVerification! with { Right = new(500, 0) } });
        using (var output = WaitOutput(pipeline, 102))
            Check(output.Rows.Single() is { CandidatePassed: true, Found: false }, "Pixel settings changes reach worker snapshots without stale hits");
        var watch = Stopwatch.StartNew(); pipeline.Dispose();
        Check(watch.ElapsedMilliseconds < 3000, "Capture and image workers stop cleanly");
        pipeline.Dispose(); Check(true, "Pipeline disposal is idempotent");
    }
    private static void TestRawInput()
    {
        for (int i = 0; i < 3; i++)
        {
            var watch = Stopwatch.StartNew();
            using var input = new RawInputService(); input.Start(); input.Dispose(); input.Dispose();
            Check(watch.ElapsedMilliseconds < 3000, $"Raw Input registration/message-loop shutdown cycle {i + 1}");
        }
    }
    private static void TestRoiSelection()
    {
        using var preview = new RoiPictureBox { Size = new System.Drawing.Size(200, 200), SelectionEnabled = true,
            Image = new Bitmap(100, 50), Dock = DockStyle.None };
        void Mouse(string method, int x, int y) => typeof(RoiPictureBox)
            .GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(preview, [new MouseEventArgs(MouseButtons.Left, 1, x, y, 0)]);
        Mouse("OnMouseDown", 20, 70); Mouse("OnMouseMove", 160, 130); Mouse("OnMouseUp", 160, 130);
        Check(preview.Selection == new Rectangle(10, 10, 70, 30), "Mouse selection accounts for image zoom and letterboxing");
        preview.ClearSelection();
        Mouse("OnMouseDown", 20, 20); Mouse("OnMouseUp", 100, 100);
        Check(preview.Selection is null, "Letterbox clicks do not start a selection");
    }
    private static void TestTabs(string root)
    {
        using var form = new Form1();
        // A transparent, offscreen host exercises real WinForms layout without a visible window.
        form.ShowInTaskbar = false; form.Opacity = 0;
        form.StartPosition = FormStartPosition.Manual; form.Location = new System.Drawing.Point(-30000, -30000);
        form.Show();
        FindControls<DetectionControl>(form).Single().StopServices();
        form.PerformLayout();
        var tabs = form.Controls.OfType<TabControl>().Single();
        Check(tabs.TabPages.Count == 3, "Main form contains three integrated pages");
        Check(tabs.TabPages[0].Controls[0] is DetectionControl && tabs.TabPages[1].Controls[0] is SlicingControl
            && tabs.TabPages[2].Controls[0] is SkillEditorControl, "Each page owns its separate feature control");
        var editor = (SkillEditorControl)tabs.TabPages[2].Controls[0];
        Check(editor.ConfirmDiscard(), "Unmodified editor closes without prompting");
        for (int index = 0; index < 3; index++)
        {
            tabs.SelectedIndex = index;
            form.PerformLayout();
            using var rendered = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(rendered, new Rectangle(System.Drawing.Point.Empty, rendered.Size));
            rendered.Save(Path.Combine(root, $"page-{index + 1}.png"));
            Check(tabs.SelectedTab!.Controls[0].Width > 900, $"Page {index + 1} fills the tab area");
        }
        tabs.SelectedIndex = 0;
        form.Size = form.MinimumSize;
        form.PerformLayout();
        var detection = (DetectionControl)tabs.TabPages[0].Controls[0];
        var toolbar = FindControls<FlowLayoutPanel>(detection).Single(control => control.Name == "DetectionToolbar");
        Check(toolbar.Controls.Cast<Control>().All(control => control.Bottom <= toolbar.ClientSize.Height),
            "Main toolbar wraps without clipping at minimum window size");
        Check(FindControls<NumericUpDown>(detection).Single(control => control.Name == "ProcessScale").Value == 1,
            "Processing scale defaults to 1.00 in the main UI");
        var pixelControl = FindControls<PixelVerificationControl>(detection).Single();
        Check(pixelControl.GetOptions() is { Enabled: true, RgbTolerance: 0, MinimumMatches: 5 }, "UI defaults to exact five-point verification");
        var rightTabs = FindControls<TabControl>(detection).Single();
        rightTabs.SelectedTab = rightTabs.TabPages.Cast<TabPage>().Single(page => page.Text == "五點設定");
        pixelControl.ApplyOptions(new() { RgbTolerance = 3, MinimumMatches = 4, Up = new(-1, -17) });
        Check(pixelControl.GetOptions() is { RgbTolerance: 3, MinimumMatches: 4, Up: { X: -1, Y: -17 } }, "UI supports independent editable offsets and tolerance");
        using var samplingImage = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(samplingImage, new Rectangle(System.Drawing.Point.Empty, samplingImage.Size));
        samplingImage.Save(Path.Combine(root, "five-point-settings.png"));
    }
    private static IEnumerable<T> FindControls<T>(Control parent) where T : Control => parent.Controls.Cast<Control>()
        .SelectMany(child => (child is T match ? new[] { match } : Array.Empty<T>()).Concat(FindControls<T>(child)));

    private static void TestPixelResultsUi(string root)
    {
        using var form = new Form1 { ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.Manual,
            Location = new System.Drawing.Point(-30000, -30000) };
        var detection = FindControls<DetectionControl>(form).Single();
        var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        // Feed only synthetic images to the UI so no desktop screenshots are stored in artifacts.
        typeof(DetectionControl).GetField("imagePath", fields)!.SetValue(detection, Path.Combine(root, "pixel-ui.png"));
        typeof(DetectionControl).GetField("templateFolder", fields)!.SetValue(detection, Path.Combine(root, "pixel-templates"));
        typeof(DetectionControl).GetField("detecting", fields)!.SetValue(detection, true);
        form.Show();
        var table = FindControls<DataGridView>(detection).Single(grid => grid.Name == "DetectionResults");
        var details = FindControls<DataGridView>(detection).Single(grid => grid.Name == "PixelSampleDetails");
        WaitFor(() => table.Rows.Count == 2);
        Check(table.Rows.Cast<DataGridViewRow>().All(row => row.Tag is DetectionRow { Found: true }),
            "Live UI distinguishes successful candidates and final hits");
        Check(details.Rows.Count == 5 && (string)details.Rows[0].Cells[5].Value! == "是",
            "Selecting a live result populates five expected/actual RGB rows");
        table.CurrentCell = table.Rows[1].Cells[0];
        Check(details.Rows.Count == 5 && table.CurrentRow!.Tag is DetectionRow, "Selecting another result refreshes point diagnostics");
        string selectedName = ((DetectionRow)table.CurrentRow!.Tag!).Template;
        Check(FindControls<Label>(detection).Single(label => label.Name == "PixelSampleStatus").Text.StartsWith(selectedName + " ·"),
            "Point diagnostics follow the current row rather than the previous selection");
        using (var image = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(image, new Rectangle(System.Drawing.Point.Empty, image.Size));
            image.Save(Path.Combine(root, "five-point-results.png"));
        }
        var editor = FindControls<PixelVerificationControl>(detection).Single();
        editor.ApplyOptions(editor.GetOptions() with { Right = new(500, 0) });
        WaitFor(() => table.Rows.Count == 2 && table.Rows[0].Tag is DetectionRow { PixelVerification.Status: PixelCheckStatus.OutOfBounds });
        Check((string)table.Rows[0].Cells["候選通過"].Value! == "是" && (string)table.Rows[0].Cells["最終命中"].Value! == "否",
            "Live parameter edits turn candidate-only results into rejected final hits");
        editor.VerificationToggle.Checked = false;
        WaitFor(() => table.Rows.Count == 2 && table.Rows[0].Tag is DetectionRow { PixelVerification.Status: PixelCheckStatus.Disabled });
        Check(table.Rows[0].Tag is DetectionRow { Found: true }, "Main checkbox restores OpenCV-only hits without restarting the application");
        detection.StopServices();

        static void WaitFor(Func<bool> condition)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 5000)
            {
                Application.DoEvents();
                if (condition()) return;
                Thread.Sleep(10);
            }
            throw new InvalidOperationException("UI did not receive its expected detection result.");
        }
    }
}
