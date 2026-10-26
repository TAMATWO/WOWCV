using System.Reflection;
using System.Text.Json;
using WOWCV;
using WOWCV.Controls;
using WOWCV.Models;
using WOWCV.Services;

internal static class SkillValueLimitTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        foreach (var (priority, weight, cooldown) in new[] { (1, 1, 0m), (10, 100, 600m) })
        {
            var parsed = SkillDocumentService.Parse(Json(priority, weight, cooldown), out var adjustments).Data[0].Process[0];
            check(parsed.Priority == priority && parsed.SkillWeight == weight && parsed.CdSec == cooldown && adjustments.Count == 0,
                $"Skill numeric limits accept exact boundary {priority}/{weight}/{cooldown}");
        }
        foreach (var (priority, weight, cooldown, field) in new[]
        {
            (0, 1, 0m, "priority"), (11, 1, 0m, "priority"), (1, 0, 0m, "skillweight"),
            (1, 101, 0m, "skillweight"), (1, 1, -0.4m, "cd_sec"), (1, 1, 600.1m, "cd_sec")
        })
        {
            try { SkillDocumentService.Parse(Json(priority, weight, cooldown)); check(false, "Out-of-range values must fail"); }
            catch (JsonException ex) { check(ex.Message.Contains("數值技能") && ex.Message.Contains(field) && ex.Message.Contains("～"),
                $"Invalid {field} identifies the skill and allowed range"); }
        }
        foreach (var (input, expected) in new[] { (15.45m, 15m), (15.5m, 16m), (16.5m, 17m), (0.5m, 1m), (599.5m, 600m) })
        {
            var parsed = SkillDocumentService.Parse(Json(1, 100, input), out var adjustments);
            check(parsed.Data[0].Process[0].CdSec == expected && adjustments.Count == 1 && adjustments[0].Contains("數值技能"),
                $"Legacy cooldown {input} rounds half-up to {expected} with a named adjustment notice");
        }
        var runtime = new RuntimeConfiguration();
        runtime.Import(Json(1, 100, 15.5m), out var notices);
        long revision = runtime.Bindings.Revision;
        check(runtime.Read()!.Data[0].Process[0].CdSec == 16 && notices.Count == 1,
            "Runtime import normalizes cooldowns and exposes adjustments");
        try { runtime.Import(Json(11, 100, 10)); check(false, "Invalid runtime import must fail"); }
        catch (JsonException) { check(runtime.Bindings.Revision == revision && runtime.Read()!.Data[0].Process[0].CdSec == 16,
            "Rejected range changes preserve the previous runtime document and revision"); }
        string directory = Path.Combine(root, "skill-values"); Directory.CreateDirectory(directory);
        TestEditor(directory, check);
        TestStartup(directory, check);
    }

    private static string Json(int priority, int weight, decimal cooldown) => SkillDocumentService.Serialize(new()
    {
        Data = [new() { Press = "E", Process = [new() { SkillName = "數值技能", Priority = priority, SkillWeight = weight, CdSec = cooldown,
            Extra = new() { ["custom"] = JsonSerializer.SerializeToElement(1.25m) } }] }]
    });

    private static void TestEditor(string directory, Action<bool, string> check)
    {
        string path = Path.Combine(directory, "legacy.json"), original = Json(1, 100, 15.5m);
        File.WriteAllText(path, original);
        using var editor = new SkillEditorControl((_, _) => true) { Dock = DockStyle.Fill };
        using var host = new Form { ClientSize = new(1100, 740), ShowInTaskbar = false, Opacity = 0,
            StartPosition = FormStartPosition.Manual, Location = new(-30000, -30000) };
        host.Controls.Add(editor); host.Show();
        var parsed = SkillDocumentService.Parse(File.ReadAllText(path), out var adjustments);
        Invoke(editor, "ApplyImportedDocument", parsed, adjustments, "legacy.json");
        var priority = Find<NumericUpDown>(editor).Single(input => input.Name == "PriorityInput");
        var weight = Find<NumericUpDown>(editor).Single(input => input.Name == "WeightInput");
        var cooldown = Find<NumericUpDown>(editor).Single(input => input.Name == "CooldownInput");
        check(priority.Minimum == 1 && priority.Maximum == 10 && weight.Minimum == 1 && weight.Maximum == 100
            && cooldown.Minimum == 0 && cooldown.Maximum == 600 && new[] { priority, weight, cooldown }.All(input => input.DecimalPlaces == 0 && input.Increment == 1),
            "All editor numeric fields enforce the specified integer ranges and unit steps");
        check(Field<Label>(editor, "footer").Text.Contains("四捨五入") && Field<string>(editor, "saved") != Snapshot(editor)
            && File.ReadAllText(path) == original, "Rounded editor imports are marked unsaved and leave the source file untouched");
        priority.Value = 10; weight.Value = 1; cooldown.Value = 600;
        check(Find<TrackBar>(editor).Single(slider => slider.Name == "PrioritySlider").Value == 10
            && Find<TrackBar>(editor).Single(slider => slider.Name == "WeightSlider").Value == 1
            && Find<TrackBar>(editor).Single(slider => slider.Name == "CooldownSlider").Value == 600,
            "Sliders follow the same unscaled integer values as numeric inputs");
        cooldown.Value = 15.5m;
        check(cooldown.Value == 16 && Read(editor).Data[0].Process[0].CdSec == 16,
            "A fractional numeric value is normalized before it enters editor history");
        Find<Button>(editor).Single(button => button.Text == "修改前（復原）").PerformClick();
        check(cooldown.Value == 600, "One undo restores the previous integer cooldown");
        Find<Button>(editor).Single(button => button.Text == "修改後（重做）").PerformClick();
        check(cooldown.Value == 16, "One redo restores the rounded integer cooldown");
        var slider = Find<TrackBar>(editor).Single(control => control.Name == "PrioritySlider"); slider.Value = 1;
        check(priority.Value == 1 && Read(editor).Data[0].Process[0].Priority == 1, "Moving a slider updates the numeric field and stored skill");
        Invoke(editor, "SaveToFile", path);
        using (var saved = JsonDocument.Parse(File.ReadAllText(path)))
        {
            var entry = saved.RootElement.GetProperty("data")[0].GetProperty("process")[0];
            check(entry.GetProperty("cd_sec").GetRawText() == "16" && entry.GetProperty("custom").GetDecimal() == 1.25m
                && Field<string>(editor, "saved") == Snapshot(editor), "Explicit save writes integer cooldowns, preserves custom values and clears unsaved state");
        }
        using (var image = new Bitmap(host.Width, host.Height))
        {
            host.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(directory, "integer-fields.png"));
        }
        Find<Button>(editor).Single(button => button.Name == "DeleteSkill").PerformClick();
        Find<Button>(editor).Single(button => button.Name == "DeleteSkillBinding").PerformClick();
        check(Read(editor).Data.Count == 0 && !priority.Enabled && priority.Value == 1 && weight.Value == 100 && cooldown.Value == 0,
            "Deleting the final group leaves disabled fields at valid placeholder values");
        Find<Button>(editor).Single(button => button.Text == "修改前（復原）").PerformClick();
        Find<Button>(editor).Single(button => button.Text == "修改前（復原）").PerformClick();
        check(priority.Enabled && Read(editor).Data[0].Process[0].CdSec == 16,
            "Undo after an empty document restores integer fields without range errors");
        host.Close();
    }

    private static void TestStartup(string directory, Action<bool, string> check)
    {
        string flow = Path.Combine(directory, "startup-flow.json"), image = Path.Combine(directory, "source.png"), config = Path.Combine(directory, "config.json");
        string original = Json(1, 100, 15.5m); File.WriteAllText(flow, original);
        using (var source = new Bitmap(40, 40)) source.Save(image);
        using (var store = new WorkbenchConfigurationStore(config))
        {
            store.QueueSave(new() { Source = new() { Kind = SourceKind.Image, ImagePath = image }, SkillFlowPath = flow }); store.Flush();
        }
        using var host = new Form1(config) { ShowInTaskbar = false, Opacity = 0,
            StartPosition = FormStartPosition.Manual, Location = new(-30000, -30000) };
        host.Show(); var detection = Find<DetectionControl>(host).Single();
        check(detection.Configuration.Read()!.Data[0].Process[0].CdSec == 16 && detection.ConfigurationNotice.Contains("四捨五入"),
            "Startup normalizes legacy flow cooldowns and displays the conversion notice");
        host.Close(); check(File.ReadAllText(flow) == original, "Startup restoration and shutdown never rewrite the original skill flow");
    }

    private static SkillDocument Read(SkillEditorControl editor) => SkillDocumentService.Parse(Snapshot(editor));
    private static string Snapshot(SkillEditorControl editor) => (string)Invoke(editor, "Snapshot")!;
    private static object? Invoke(SkillEditorControl editor, string name, params object[] args) => typeof(SkillEditorControl)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, args);
    private static T Field<T>(SkillEditorControl editor, string name) => (T)typeof(SkillEditorControl)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
    private static IEnumerable<T> Find<T>(Control parent) where T : Control => parent.Controls.Cast<Control>()
        .SelectMany(child => (child is T match ? new[] { match } : []).Concat(Find<T>(child)));
}
