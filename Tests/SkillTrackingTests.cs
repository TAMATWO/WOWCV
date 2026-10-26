using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using OpenCvSharp;
using WOWCV;
using WOWCV.Controls;
using WOWCV.Models;
using WOWCV.Services;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

internal static class SkillTrackingTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        TestBindings(check);
        string directory = Path.Combine(root, "tracking"); Directory.CreateDirectory(directory);
        string folder = Path.Combine(directory, "templates"); Directory.CreateDirectory(folder);
        using var template = new Mat(40, 40, MatType.CV_8UC3);
        Cv2.Randu(template, new Scalar(40, 40, 40), new Scalar(220, 220, 220));
        Cv2.ImWrite(Path.Combine(folder, "skill.png"), template);
        var doc = Document();
        var bindings = SkillImageBindings.Resolve(doc, 1);
        var settings = new DetectionSettings(1, Rectangle.Empty, "source", folder, Threshold: 0.99,
            Detecting: true, SkillBindings: bindings, SourceIdentity: "image-1");
        using var matcher = new TemplateMatchingService();
        var tracker = new SkillTrackingService();
        using var original = Frame(template);
        var firstRows = matcher.Match(original, settings, default, annotate: false);
        var first = Observe(tracker, original, settings, firstRows).Single();
        check(first.Found && first.Bounds == new Rectangle(40, 30, 40, 40), "First verified candidate remembers the bound skill's exact rectangle");
        using var flash = Frame(template, flash: true);
        var flashRows = matcher.Match(flash, settings, default, annotate: false);
        check(flashRows.All(row => !row.CandidatePassed), "Flashing fixture fails OpenCV's candidate gate");
        var flashed = Observe(tracker, flash, settings, flashRows).Single();
        check(flashed.Found && flashed.Verification.MatchCount == 5 && flashed.Bounds == first.Bounds,
            "Remembered skill still reads five raw points when OpenCV has no accepted candidate");

        using var changed = Frame(template);
        var center = changed.At<Vec3b>(50, 60); center.Item2 = (byte)(255 - center.Item2); changed.Set(50, 60, center);
        var four = Observe(tracker, changed, settings with { PixelVerification = new() { MinimumMatches = 4 } }, []).Single();
        check(four.Found && four.Verification.MatchCount == 4 && four.Verification.Samples.Count == 5,
            "Persistent verification reads five points but accepts the configured four-point threshold");
        var five = Observe(tracker, changed, settings with { PixelVerification = new() { MinimumMatches = 5 } }, []).Single();
        check(!five.Found && five.Bounds == first.Bounds, "Changing minimum matches to five rejects the same frame without moving the anchor");
        var offset = Observe(tracker, changed, settings with { PixelVerification = new() { Center = new(1, 1), Up = new(-1, -12) } }, []).Single();
        check(offset.Found && offset.Verification.Samples[0].TemplatePoint == new Point(21, 21),
            "Editing offsets reads new reference colors while preserving the remembered position");
        var relaxed = Observe(tracker, original, settings with { Threshold = 0.3, ProcessScale = 0.5 }, []).Single();
        check(relaxed.Found && relaxed.Bounds == first.Bounds, "Threshold and processing-scale edits do not erase position memory");
        var paused = Observe(tracker, original, settings with { Detecting = false }, []).Single();
        check(!paused.Found && paused.State == SkillTrackingState.Paused && paused.Bounds == first.Bounds && paused.Verification.Samples.Count == 0,
            "Pause clears live matches and samples but retains the anchor");
        var disabled = Observe(tracker, original, settings with { PixelVerification = new() { Enabled = false } }, []).Single();
        check(!disabled.Found && disabled.State == SkillTrackingState.Paused && disabled.Bounds == first.Bounds,
            "Disabling five-point verification pauses remembered skill validation");
        check(Observe(tracker, original, settings, []).Single().Found, "Re-enabling verification resumes from the saved rectangle without OpenCV");

        using var moved = Frame(template, location: new Point(110, 75));
        var movedRows = matcher.Match(moved, settings, default, annotate: false);
        var held = Observe(tracker, moved, settings, movedRows).Single();
        check(!held.Found && held.Bounds == first.Bounds, "A later candidate elsewhere cannot make a remembered skill drift");
        var relocated = Observe(tracker, moved, settings with { TrackingResetRevision = 1 }, movedRows).Single();
        check(relocated.Found && relocated.Bounds == new Rectangle(110, 75, 40, 40), "Explicit reset allows a new verified position to be learned");

        foreach (var change in new[]
        {
            settings with { SourceIdentity = "image-2" }, settings with { ImagePath = "another-image" },
            settings with { Roi = new Rectangle(0, 0, 190, 150) }, settings with { TemplateRevision = 1 },
            settings with { TemplateFolder = folder + "-other" },
            settings with { SkillBindings = SkillImageBindings.Resolve(doc, 2) }, settings with { TrackingResetRevision = 9 }
        })
        {
            var isolated = new SkillTrackingService(); Observe(isolated, original, settings, firstRows);
            check(Observe(isolated, original, change, []).Single().Bounds is null, "Source/ROI/template/flow/reset context changes invalidate position memory");
        }
        var sizeTracker = new SkillTrackingService(); Observe(sizeTracker, original, settings, firstRows);
        check(sizeTracker.Update(new Size(201, 160), settings, [], matcher.LoadedTemplateNames,
            (name, box, options) => matcher.VerifyAt(original, name, box, options)).Single().Bounds is null,
            "A source-size change clears the remembered rectangle");
        var missing = settings with { SkillBindings = SkillImageBindings.Resolve(Document("missing.png"), 3) };
        check(Observe(new(), original, missing, firstRows).Single().State == SkillTrackingState.InvalidBinding,
            "Missing template files are reported instead of creating an anchor");
        var notVerified = settings with { PixelVerification = new() { Enabled = false } };
        var candidateOnly = matcher.Match(original, notVerified, default, annotate: false);
        check(Observe(new(), original, notVerified, candidateOnly).Single().Bounds is null,
            "OpenCV-only hits never initialize five-point position memory");
        var edgeOptions = new PixelVerificationOptions { Center = new(-20, -20) };
        using (var frame = Frame(template))
        {
            var rows = matcher.Match(frame, settings with { PixelVerification = edgeOptions }, default, annotate: false);
            var result = Observe(new(), frame, settings with { PixelVerification = edgeOptions }, rows).Single();
            TemplateMatchingService.DrawResults(frame, rows);
            check(result.Found && result.Verification.MatchCount == 5,
                "Remembered sampling completes before candidate rectangles paint over edge pixels");
        }
        TestPipeline(directory, template, folder, settings, check);
        TestEditor(directory, folder, check);
        TestUi(directory, template, folder, check);

        IReadOnlyList<SkillObservation> Observe(SkillTrackingService service, Mat frame, DetectionSettings options, IReadOnlyList<DetectionRow> rows)
            => service.Update(new Size(frame.Width, frame.Height), options, rows, matcher.LoadedTemplateNames,
                (name, box, pixels) => matcher.VerifyAt(frame, name, box, pixels));
    }

    private static SkillDocument Document(string template = "skill.png", string key = "1", string name = "技能A") => new()
    {
        Data = [new() { Press = "E", Process = [new() { SkillName = name, TemplateFile = template, CastKey = key }] }]
    };
    private static Mat Frame(Mat template, bool flash = false, Point? location = null)
    {
        var result = new Mat(160, 200, MatType.CV_8UC3, new Scalar(15, 15, 15));
        var at = location ?? new Point(40, 30);
        using var region = new Mat(result, new Rect(at.X, at.Y, 40, 40));
        if (!flash) template.CopyTo(region);
        else
        {
            region.SetTo(new Scalar(250, 250, 250));
            foreach (var offset in new PixelVerificationOptions().Offsets)
            {
                int x = 20 + offset.X, y = 20 + offset.Y;
                result.Set(at.Y + y, at.X + x, template.At<Vec3b>(y, x));
            }
        }
        return result;
    }

    private static void TestBindings(Action<bool, string> check)
    {
        var old = SkillDocumentService.Parse("{\"data\":[{\"press\":\"E\",\"process\":[{\"skillname\":\"技能\",\"status\":\"A\"}]}]}");
        check(old.Data[0].Process[0] is { TemplateFile: "", CastKey: "", Status: "A" }, "Old flow files preserve STATUS and load without new binding fields");
        check(SkillImageBindings.Resolve(old, 1).Items.Single().Issue is not null, "Incomplete old entries are displayed as unbound, not silently guessed");
        var doc = Document();
        var copy = SkillDocumentService.Parse(SkillDocumentService.Serialize(doc));
        check(copy.Data[0].Process[0] is { TemplateFile: "skill.png", CastKey: "1" }, "Template filename and cast key round-trip in flow JSON");
        doc.Data.Add(new() { Press = "R", Process = [new() { SkillName = "技能A", TemplateFile = "SKILL.PNG", CastKey = "1", Status = "A" }] });
        var runtime = new RuntimeConfiguration(); runtime.Import(SkillDocumentService.Serialize(doc));
        var snapshot = runtime.Bindings;
        check(snapshot.Items.Count == 1 && snapshot.Items[0].Issue is null, "Identical bindings across flows and STATUS values share one locator");
        doc.Data[0].Process[0].CastKey = "2";
        check(snapshot.Items[0].CastKey == "1", "Runtime binding snapshots do not share mutable editor documents");
        runtime.Import(SkillDocumentService.Serialize(doc));
        check(runtime.Bindings.Revision > snapshot.Revision && runtime.Bindings.Items.All(item => item.Issue is not null),
            "Reloading contradictory skill keys increments revision and reports every conflict");
        var collision = Document(); collision.Data[0].Process.Add(new() { SkillName = "技能B", TemplateFile = "skill.png", CastKey = "2" });
        check(SkillImageBindings.Resolve(collision, 1).Items.All(item => item.Issue is not null), "One template cannot silently identify two different skill names");
        try
        {
            SkillDocumentService.Parse(SkillDocumentService.Serialize(Document("../skill.png")));
            check(false, "Template bindings reject directory traversal");
        }
        catch (JsonException) { check(true, "Template bindings reject directory traversal"); }
    }

    private static void TestPipeline(string directory, Mat template, string folder, DetectionSettings options, Action<bool, string> check)
    {
        string source = Path.Combine(directory, "pipeline.png");
        using (var image = Frame(template)) Cv2.ImWrite(source, image);
        using var pipeline = new DetectionPipeline();
        var settings = options with { ImagePath = source };
        pipeline.Configure(settings); pipeline.Start();
        using var initial = WaitOutput(pipeline, settings.Version);
        check(initial.Skills.Items.Single().Found && initial.Skills.FrameId > 0
            && initial.Skills.AnalysisCompletedTicks >= initial.Skills.CaptureStartedTicks,
            "Background skill snapshots carry frame identity and capture/analysis timing");
        check(ReferenceEquals(pipeline.LatestSkills, initial.Skills), "Latest skill evidence is independent of UI preview consumption");
        using (var image = Frame(template, flash: true)) Cv2.ImWrite(source, image);
        pipeline.Configure(settings with { Version = 2 });
        check(pipeline.LatestSkills == SkillObservationSnapshot.Empty, "Configuration changes immediately invalidate exposed stale observations");
        using var flash = WaitOutput(pipeline, 2);
        check(flash.Rows.All(row => !row.CandidatePassed) && flash.Skills.Items.Single().Found
            && flash.Skills.FrameId > initial.Skills.FrameId && flash.Skills.CaptureStartedTicks > initial.Skills.CaptureStartedTicks,
            "A fresh frame with no OpenCV candidate still publishes remembered five-point matches");
        check(initial.Skills.Items.Single().Found && initial.Skills.FrameId != flash.Skills.FrameId, "Previously published snapshots remain immutable");
        pipeline.Configure(settings with { Version = 3, Detecting = false });
        using var paused = WaitOutput(pipeline, 3);
        check(paused.Skills.Items.Single() is { Found: false, State: SkillTrackingState.Paused, Bounds: not null },
            "Pausing a running pipeline cannot expose its last successful skill as a current match");
        pipeline.Configure(settings with { Version = 4, TrackingResetRevision = 1 });
        using var reset = WaitOutput(pipeline, 4);
        check(reset.Skills.Items.Single().State == SkillTrackingState.WaitingForLocation, "Reset during flashing waits for a new verified candidate");
        pipeline.Dispose(); check(pipeline.LatestSkills == SkillObservationSnapshot.Empty, "Stopping the pipeline clears published skill evidence");
    }

    private static void TestEditor(string directory, string folder, Action<bool, string> check)
    {
        using var host = new Form { ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.Manual,
            Location = new Point(-30000, -30000), ClientSize = new Size(1300, 900) };
        var editor = new SkillEditorControl { Dock = DockStyle.Fill }; host.Controls.Add(editor); host.Show();
        editor.SetTemplateFolder(folder);
        Find<Button>(editor).Single(button => button.Text == "新增技能").PerformClick();
        var grid = SkillEditorUi.Grid(editor); grid.EndEdit();
        string skillName = (string)grid.Rows[0].Cells["SkillName"].Value!;
        check(grid.Columns["Template"]!.ReadOnly && !grid.Columns["PickTemplate"]!.ReadOnly && editor.TemplateFolderPath == folder,
            "Editor offers a row image picker with a read-only bound filename");
        typeof(SkillEditorControl).GetMethod("BindTemplateImage", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(editor, [Path.Combine(folder, "skill.png")]);
        SkillEditorUi.Edit(editor, skillName, "CastKey", "Ctrl+1");
        string serialized = EditorText(editor);
        var entry = SkillDocumentService.Parse(serialized).Data[0].Process[0];
        check(entry.TemplateFile == "skill.png" && entry.CastKey == "Ctrl+1", "Editor binds the image and directly records a separate casting key");
        var runtime = new RuntimeConfiguration(); runtime.Import(serialized);
        SkillEditorUi.Edit(editor, skillName, "CastKey", "2");
        check(runtime.Bindings.Items.Single().CastKey == "Ctrl+1", "Editing bindings never changes an imported runtime flow");
        Find<Button>(editor).Single(button => button.Text == "修改前（復原）").PerformClick();
        check((string)grid.Rows[0].Cells["CastKey"].Value! == "Ctrl+1", "Undo restores the casting-key binding");
        Find<Button>(editor).Single(button => button.Text == "修改後（重做）").PerformClick();
        check((string)grid.Rows[0].Cells["CastKey"].Value! == "2", "Redo restores the edited casting key");
        string before = EditorText(editor);
        editor.SetTemplateFolder(Path.Combine(directory, "missing-folder"));
        check(EditorText(editor) == before && (string)grid.Rows[0].Cells["Template"].Value! == "skill.png"
            && grid.Columns["PickTemplate"]!.ReadOnly, "Missing image folders preserve binding data and disable image selection");
        editor.SetTemplateFolder(folder);
        try
        {
            typeof(SkillEditorControl).GetMethod("BindTemplateImage", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(editor, [Path.Combine(directory, "outside.png")]);
            check(false, "Image binding rejects outside-folder files");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException)
        { check(EditorText(editor) == before, "Outside-folder bindings leave the document unchanged"); }
        SkillEditorUi.ClickCell(editor, skillName, "ClearTemplate");
        check((string)grid.Rows[0].Cells["Template"].Value! == "", "The row clear button removes its image binding");
        Find<Button>(editor).Single(button => button.Text == "修改前（復原）").PerformClick();
        check((string)grid.Rows[0].Cells["Template"].Value! == "skill.png", "Undo restores a cleared row image binding");
        using var image = new Bitmap(host.Width, host.Height); host.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        image.Save(Path.Combine(directory, "binding-editor.png"));
        host.Close();
    }

    private static void TestUi(string directory, Mat template, string folder, Action<bool, string> check)
    {
        string source = Path.Combine(directory, "ui.png"), flow = Path.Combine(directory, "flow.json"), config = Path.Combine(directory, "config.json");
        using (var image = Frame(template)) Cv2.ImWrite(source, image);
        File.WriteAllText(flow, SkillDocumentService.Serialize(Document()));
        using (var store = new WorkbenchConfigurationStore(config))
        {
            store.QueueSave(new() { Source = new() { Kind = SourceKind.Image, ImagePath = source, TemplateFolderPath = folder },
                Search = new() { Threshold = 0.99 }, SkillFlowPath = flow }); store.Flush();
        }
        using var host = new Form1(config) { ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000) };
        host.Show();
        var detection = Find<DetectionControl>(host).Single();
        check(Find<SkillEditorControl>(host).Single().TemplateFolderPath == folder,
            "Restored main template folder supplies the editor's image picker without importing its document");
        var table = Find<DataGridView>(detection).Single(control => control.Name == "TrackedSkills");
        Find<Button>(detection).Single(button => button.Text == "開始偵測").PerformClick();
        WaitFor(() => table.Rows.Count == 1 && table.Rows[0].Tag is SkillObservation { Found: true });
        Find<TabControl>(detection).Single().SelectedTab = Find<TabControl>(detection).Single().TabPages.Cast<TabPage>().Single(page => page.Text == "已定位技能");
        Find<Button>(detection).Single(button => button.Name == "ToggleTrackedDetails").PerformClick();
        check((string)table.Rows[0].Cells["技能"].Value! == "技能A" && (string)table.Rows[0].Cells["施放鍵"].Value! == "1",
            "Located-skill UI displays skill identity and the stored casting key");
        check(table.Font.SizeInPoints == 12 && table.Columns["縮圖"]!.Index < table.Columns["技能"]!.Index
            && table.Rows[0].Cells["縮圖"].Value is Bitmap
            && table.Rows[0].Cells["樣版"].ToolTipText == "skill.png",
            "Located skills use 12pt text with a thumbnail before skill identity and a full template-name tooltip");
        check(Find<DataGridView>(detection).Single(control => control.Name == "TrackedSkillDetails").Rows.Count == 5,
            "Selecting a located skill displays five expected and actual RGB values");
        var detailTable = Find<DataGridView>(detection).Single(control => control.Name == "TrackedSkillDetails");
        var stableRow = table.Rows[0];
        var stableDetail = detailTable.Rows[0];
        table.CurrentCell = stableRow.Cells["施放鍵"];
        table.Columns["技能"]!.Width = 173;
        table.HorizontalScrollingOffset = 55;
        int previousScroll = table.HorizontalScrollingOffset;
        int detailChanges = 0;
        detailTable.CellValueChanged += (_, _) => detailChanges++;
        var snapshotField = typeof(DetectionControl).GetField("displayedSkills", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var update = typeof(DetectionControl).GetMethod("UpdateTrackedResults", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var showDetails = typeof(DetectionControl).GetMethod("ShowActiveDetails", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var snapshot = (SkillObservationSnapshot)snapshotField.GetValue(detection)!;
        for (int index = 0; index < 20; index++)
        {
            update.Invoke(detection, [snapshot with { FrameId = snapshot.FrameId + index + 1 }]);
            showDetails.Invoke(detection, null);
        }
        check(ReferenceEquals(stableRow, table.CurrentRow) && table.CurrentCell!.OwningColumn!.Name == "施放鍵"
            && table.Columns["技能"]!.Width == 173 && table.HorizontalScrollingOffset == previousScroll,
            "Repeated frames retain row identity, selected cell, custom width and scroll position");
        check(ReferenceEquals(stableDetail, detailTable.Rows[0]) && detailChanges == 0,
            "Unchanged five-point samples retain rows without rewriting cells");
        var changed = snapshot with { Items = [snapshot.Items[0] with { State = SkillTrackingState.NotMatched, Reason = "刷新驗證" }] };
        update.Invoke(detection, [changed]);
        check(ReferenceEquals(stableRow, table.Rows[0]) && (string)table.Rows[0].Cells["狀態"].Value! == "未命中",
            "A changed match updates the existing row in place");
        update.Invoke(detection, [snapshot]);
        using (var image = Frame(template, flash: true)) Cv2.ImWrite(source, image);
        Find<Button>(detection).Single(button => button.Text == "重新偵測").PerformClick();
        WaitFor(() => table.Rows.Count == 1 && table.Rows[0].Tag is SkillObservation { Found: true });
        var candidates = Find<DataGridView>(detection).Single(control => control.Name == "DetectionResults");
        check(candidates.Rows.Cast<DataGridViewRow>().All(row => row.Tag is DetectionRow { CandidatePassed: false }),
            "Main UI keeps persistent skill matches even when the OpenCV result is not accepted");
        using (var image = new Bitmap(host.Width, host.Height))
        {
            host.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(directory, "tracked-flashing.png"));
        }
        Find<Button>(detection).Single(button => button.Text == "停止偵測").PerformClick();
        WaitFor(() => table.Rows.Count == 1 && table.Rows[0].Tag is SkillObservation { State: SkillTrackingState.Paused });
        check(table.Rows[0].Tag is SkillObservation { Found: false, Bounds: not null }, "Main pause clears the active match without discarding the skill rectangle");
        Find<Button>(detection).Single(button => button.Text == "開始偵測").PerformClick();
        WaitFor(() => table.Rows.Count == 1 && table.Rows[0].Tag is SkillObservation { Found: true });
        Find<Button>(detection).Single(button => button.Text == "重新定位技能").PerformClick();
        WaitFor(() => table.Rows.Count == 1 && table.Rows[0].Tag is SkillObservation { State: SkillTrackingState.WaitingForLocation });
        check(table.Rows[0].Tag is SkillObservation { Bounds: null }, "Main relocate button clears the old rectangle and waits during flashing");
        host.Close();
    }

    private static string EditorText(SkillEditorControl editor) => (string)typeof(SkillEditorControl)
        .GetMethod("Snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null)!;
    private static IEnumerable<T> Find<T>(Control parent) where T : Control => parent.Controls.Cast<Control>()
        .SelectMany(child => (child is T match ? new[] { match } : []).Concat(Find<T>(child)));
    private static DetectionOutput WaitOutput(DetectionPipeline pipeline, long version)
    {
        DetectionOutput? result = null;
        WaitFor(() => { result = pipeline.TakeOutput(); if (result?.Version == version) return true; result?.Dispose(); return false; });
        return result!;
    }
    private static void WaitFor(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 5000)
        {
            Application.DoEvents(); if (condition()) return; Thread.Sleep(10);
        }
        throw new InvalidOperationException("Timed out waiting for skill tracking.");
    }
}
