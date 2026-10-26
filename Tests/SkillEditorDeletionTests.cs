using System.Reflection;
using System.Text.Json;
using WOWCV.Controls;
using WOWCV.Models;
using WOWCV.Services;

internal static class SkillEditorDeletionTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        bool confirm = false;
        var prompts = new List<(string Press, int Count)>();
        using var editor = new SkillEditorControl((press, count) => { prompts.Add((press, count)); return confirm; }) { Dock = DockStyle.Fill };
        using var host = new Form { ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.Manual,
            Location = new(-30000, -30000), ClientSize = new(1100, 740) };
        host.Controls.Add(editor); host.Show();
        var deleteSkill = Find<Button>(editor).Single(button => button.Name == "DeleteSkill");
        var deleteGroup = Find<Button>(editor).Single(button => button.Name == "DeleteSkillBinding");
        var groups = Find<ComboBox>(editor).Single(control => control.Name == "SkillBindingSelector");
        var skills = Find<ComboBox>(editor).Single(control => control.Name == "SkillEntrySelector");
        var undo = Find<Button>(editor).Single(button => button.Text == "修改前（復原）");
        var redo = Find<Button>(editor).Single(button => button.Text == "修改後（重做）");
        check(!deleteSkill.Enabled && !deleteGroup.Enabled && !groups.Enabled && !skills.Enabled,
            "An empty editor disables deletion buttons and empty selectors");

        var fixture = new SkillDocument { Data = [new() { Press = "E", Process = [
            new() { SkillName = "A" },
            new() { SkillName = "B", TemplateFile = "b.png", CastKey = "2", CdSec = 1m,
                Extra = new() { ["custom"] = JsonSerializer.SerializeToElement(42) } },
            new() { SkillName = "C", CooldownCheck = "B" }] },
            new() { Press = "R", Process = [new() { SkillName = "D", TemplateFile = "d.png", CastKey = "4", CdSec = 4m }] },
            new() { Press = "T", Process = [new() { SkillName = "F" }] }] };
        Load(editor, fixture);
        string original = Snapshot(editor);
        var runtime = new RuntimeConfiguration(); runtime.Import(original);
        skills.SelectedIndex = 1; deleteSkill.PerformClick();
        check(Read(editor).Data[0].Process.Select(skill => skill.SkillName).SequenceEqual(["A", "C"])
            && skills.Text == "C" && prompts.Count == 0,
            "Deleting a middle skill selects its successor without a confirmation dialog");
        check(Read(editor).Data[0].Process[1].CooldownCheck == "B", "Deleting a skill leaves other cooldown references unchanged");
        check(Field<Label>(editor, "footer").Text.Contains("尚未儲存"), "Deletion marks the editor document as unsaved");
        undo.PerformClick(); check(Snapshot(editor) == original, "One undo restores the complete deleted skill including custom fields");
        redo.PerformClick(); check(Read(editor).Data[0].Process.Count == 2 && skills.Text == "C", "One redo reapplies skill deletion");
        var toolbar = Find<FlowLayoutPanel>(editor).Single(control => control.Name == "SkillEditorToolbar");
        host.PerformLayout();
        check(toolbar.Controls.Cast<Control>().All(control => control.Bottom <= toolbar.ClientSize.Height),
            "Editor toolbar accommodates both deletion buttons without clipping");
        string directory = Path.Combine(root, "editor-deletion"); Directory.CreateDirectory(directory);
        using (var image = new Bitmap(host.Width, host.Height))
        {
            host.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(directory, "delete-controls.png"));
        }
        deleteSkill.PerformClick();
        check(Read(editor).Data[0].Process.Single().SkillName == "A" && skills.Text == "A",
            "Deleting the last listed skill selects the previous skill");
        deleteSkill.PerformClick();
        check(Read(editor).Data.Count == 3 && Read(editor).Data[0].Process.Count == 0
            && !deleteSkill.Enabled && deleteGroup.Enabled && !skills.Enabled && Field<TextBox>(editor, "pressInput").Enabled,
            "Deleting the final skill preserves an editable empty key group");
        undo.PerformClick(); check(Read(editor).Data[0].Process.Single().SkillName == "A" && deleteSkill.Enabled,
            "Undo restores the final skill and re-enables its editing controls");
        redo.PerformClick();
        string emptyGroupDocument = Snapshot(editor);
        int history = Field<Stack<string>>(editor, "undo").Count;
        deleteGroup.PerformClick();
        check(Snapshot(editor) == emptyGroupDocument && Field<Stack<string>>(editor, "undo").Count == history && prompts[^1] == ("E", 0),
            "Canceling group deletion preserves content and history while reporting the trigger and skill count");
        confirm = true; deleteGroup.PerformClick();
        check(Read(editor).Data.Select(group => group.Press).SequenceEqual(["R", "T"]) && groups.SelectedIndex == 0 && skills.Text == "D",
            "Confirmed group deletion selects the next group and its first skill");
        undo.PerformClick(); check(Snapshot(editor) == emptyGroupDocument, "Undo restores an empty deleted key group");
        redo.PerformClick();
        string beforeGroup = Snapshot(editor);
        deleteGroup.PerformClick();
        check(prompts[^1] == ("R", 1) && Read(editor).Data.Single().Press == "T",
            "Group deletion confirms the number of contained skills and removes them together");
        undo.PerformClick(); check(Snapshot(editor) == beforeGroup, "One undo restores a group and every contained skill binding");
        redo.PerformClick(); deleteGroup.PerformClick();
        check(Read(editor).Data.Count == 0 && !deleteGroup.Enabled && !deleteSkill.Enabled && groups.SelectedIndex == -1
            && skills.SelectedIndex == -1 && !Field<TextBox>(editor, "pressInput").Enabled
            && Field<List<Control>>(editor, "skillInputs").All(control => !control.Enabled),
            "Deleting the final group leaves a valid empty document and disables its editing fields");
        undo.PerformClick(); check(Read(editor).Data.Single().Press == "T" && Read(editor).Data[0].Process.Single().SkillName == "F",
            "Undo restores the final removed group and its skill");
        redo.PerformClick();
        Find<Button>(editor).Single(button => button.Text == "新增技能").PerformClick();
        check(Read(editor).Data.Count == 1 && Read(editor).Data[0].Process.Count == 1 && deleteSkill.Enabled && deleteGroup.Enabled,
            "Adding a skill after deleting everything creates a usable new group");
        check(SkillDocumentService.Serialize(runtime.Read()!) == SkillDocumentService.Serialize(fixture),
            "Editor deletion never changes the previously imported runtime flow");

        Load(editor, fixture); groups.SelectedIndex = 1; deleteGroup.PerformClick();
        check(Read(editor).Data.Select(group => group.Press).SequenceEqual(["E", "T"]) && groups.SelectedIndex == 1 && skills.Text == "F",
            "Deleting a middle key group selects the group that followed it");
        deleteGroup.PerformClick();
        check(groups.SelectedIndex == 0 && skills.Text == "A", "Deleting the last key group selects the previous group");
        host.Close();
    }

    private static void Load(SkillEditorControl editor, SkillDocument document)
    {
        string json = SkillDocumentService.Serialize(document);
        SetField(editor, "document", SkillDocumentService.Parse(json));
        SetField(editor, "bindingIndex", 0); SetField(editor, "skillIndex", 0);
        SetField(editor, "saved", Snapshot(editor));
        Field<Stack<string>>(editor, "undo").Clear(); Field<Stack<string>>(editor, "redo").Clear();
        typeof(SkillEditorControl).GetMethod("RefreshEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null);
    }
    private static string Snapshot(SkillEditorControl editor) => (string)typeof(SkillEditorControl)
        .GetMethod("Snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null)!;
    private static SkillDocument Read(SkillEditorControl editor) => SkillDocumentService.Parse(Snapshot(editor));
    private static T Field<T>(SkillEditorControl editor, string name) => (T)typeof(SkillEditorControl)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
    private static void SetField(SkillEditorControl editor, string name, object value) => typeof(SkillEditorControl)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(editor, value);
    private static IEnumerable<T> Find<T>(Control parent) where T : Control => parent.Controls.Cast<Control>()
        .SelectMany(child => (child is T match ? new[] { match } : []).Concat(Find<T>(child)));
}
