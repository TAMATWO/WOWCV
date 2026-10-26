using System.Text.Json;
using WOWCV.Controls;
using WOWCV.Models;
using WOWCV.Services;
using static SkillEditorUi;

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
        var groups = Find<ComboBox>(editor).Single(control => control.Name == "SkillBindingSelector");
        var grid = Grid(editor);
        var deleteSkill = Find<Button>(editor).Single(button => button.Name == "DeleteSkill");
        var deleteGroup = Find<Button>(editor).Single(button => button.Name == "DeleteSkillBinding");
        var undo = Find<Button>(editor).Single(button => button.Text == "修改前（復原）");
        var redo = Find<Button>(editor).Single(button => button.Text == "修改後（重做）");
        check(!deleteSkill.Enabled && !deleteGroup.Enabled && !groups.Enabled && grid.Rows.Count == 0,
            "Empty grid disables deletion and key-group controls");
        var fixture = new SkillDocument { Data = [new() { Press = "E", Process = [
            new() { SkillName = "A" }, new() { SkillName = "B", TemplateFile = "b.png", CastKey = "2", CdSec = 1,
                Extra = new() { ["custom"] = JsonSerializer.SerializeToElement(42) } }, new() { SkillName = "C", CooldownCheck = "B" }] },
            new() { Press = "R", Process = [new() { SkillName = "D", TemplateFile = "d.png", CastKey = "4", CdSec = 4 }] },
            new() { Press = "T", Process = [new() { SkillName = "F" }] }] };
        Load(editor, fixture);
        string original = Snapshot(editor);
        var runtime = new RuntimeConfiguration(); runtime.Import(original);
        grid.CurrentCell = Row(editor, "B").Cells["SkillName"]; deleteSkill.PerformClick();
        check(Read(editor).Data[0].Process.Select(skill => skill.SkillName).SequenceEqual(["A", "C"])
            && (string)grid.CurrentRow!.Cells["SkillName"].Value! == "C" && prompts.Count == 0,
            "Deleting a middle row selects its successor without confirmation");
        check(Read(editor).Data[0].Process[1].CooldownCheck == "B", "Deletion preserves other cooldown references");
        check(Field<Label>(editor, "footer").Text.Contains("尚未儲存"), "Deletion marks the document as unsaved");
        undo.PerformClick(); check(Snapshot(editor) == original, "One undo restores the deleted skill and custom fields");
        redo.PerformClick(); check(Read(editor).Data[0].Process.Count == 2, "Redo reapplies row deletion");
        var toolbar = Find<FlowLayoutPanel>(editor).Single(control => control.Name == "SkillEditorToolbar");
        check(toolbar.Controls.Cast<Control>().All(control => toolbar.ClientRectangle.Contains(control.Bounds)), "Editor commands fit at minimum width");
        deleteSkill.PerformClick(); deleteSkill.PerformClick();
        check(Read(editor).Data[0].Process.Count == 0 && !deleteSkill.Enabled && deleteGroup.Enabled && Field<TextBox>(editor, "pressInput").Enabled,
            "Deleting the final row preserves an editable empty key group");
        undo.PerformClick(); check(Read(editor).Data[0].Process.Count == 1 && deleteSkill.Enabled, "Undo restores the final row");
        redo.PerformClick();
        string empty = Snapshot(editor); int history = Field<Stack<string>>(editor, "undo").Count;
        deleteGroup.PerformClick();
        check(Snapshot(editor) == empty && Field<Stack<string>>(editor, "undo").Count == history && prompts[^1] == ("E", 0),
            "Canceling group deletion leaves document and history unchanged");
        confirm = true; deleteGroup.PerformClick();
        check(Read(editor).Data.Select(group => group.Press).SequenceEqual(["R", "T"]) && groups.SelectedIndex == 0,
            "Deleting a group selects its successor");
        undo.PerformClick(); check(Snapshot(editor) == empty, "Undo restores an empty group");
        redo.PerformClick(); string before = Snapshot(editor); deleteGroup.PerformClick();
        check(prompts[^1] == ("R", 1), "Group confirmation includes the contained skill count");
        undo.PerformClick(); check(Snapshot(editor) == before, "One undo restores an entire key group");
        redo.PerformClick(); deleteGroup.PerformClick();
        check(Read(editor).Data.Count == 0 && grid.Rows.Count == 0 && !deleteGroup.Enabled && groups.SelectedIndex == -1
            && !Field<TextBox>(editor, "pressInput").Enabled, "Removing the final group leaves a usable empty editor");
        undo.PerformClick(); check(Read(editor).Data.Single().Press == "T", "Undo restores the final group");
        redo.PerformClick(); Find<Button>(editor).Single(button => button.Text == "新增技能").PerformClick(); grid.EndEdit();
        check(Read(editor).Data.Count == 1 && grid.Rows.Count == 1, "Adding after an empty document creates a group and editable row");
        check(SkillDocumentService.Serialize(runtime.Read()!) == SkillDocumentService.Serialize(fixture), "Editing never changes the previously imported runtime flow");
        Load(editor, fixture); Mark(editor, "A", "C"); deleteSkill.PerformClick();
        check(Read(editor).Data[0].Process.Single().SkillName == "B", "Checked rows are deleted together");
        undo.PerformClick(); check(Snapshot(editor) == original, "One undo restores all deleted checked rows");
        groups.SelectedIndex = 1; deleteGroup.PerformClick();
        check(groups.SelectedIndex == 1 && (string)grid.CurrentRow!.Cells["SkillName"].Value! == "F", "Middle-group deletion selects the following group");
        deleteGroup.PerformClick(); check(groups.SelectedIndex == 0, "Last-group deletion selects the previous group");
        string directory = Path.Combine(root, "editor-deletion"); Directory.CreateDirectory(directory);
        using var image = new Bitmap(host.Width, host.Height);
        host.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(directory, "delete-controls.png"));
        host.Close();
    }
}