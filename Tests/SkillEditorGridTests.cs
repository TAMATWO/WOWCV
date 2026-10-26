using System.Text.Json;
using WOWCV.Controls;
using WOWCV.Models;
using static SkillEditorUi;

internal static class SkillEditorGridTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        using var editor = new SkillEditorControl((_, _) => true) { Dock = DockStyle.Fill };
        using var host = new Form { ClientSize = new(1440, 900), ShowInTaskbar = false, Opacity = 0,
            StartPosition = FormStartPosition.Manual, Location = new(-30000, -30000) };
        host.Controls.Add(editor); host.Show();
        var fixture = new SkillDocument { Data = [new() { Press = "E", Process = Enumerable.Range(0, 20).Select(index => new SkillEntry
        {
            SkillName = $"S{index:00}", Priority = index == 19 ? 1 : 2, SkillWeight = index == 19 ? 100 : 80, CdSec = index,
            Extra = new() { ["custom"] = JsonSerializer.SerializeToElement(index) }
        }).ToList() }, new() { Press = "R", Process = [new() { SkillName = "Other", Priority = 4, SkillWeight = 99, CdSec = 50 }] }] };
        Load(editor, fixture);
        var grid = Grid(editor);
        var search = Find<TextBox>(editor).Single(control => control.Name == "SkillSearch");
        var order = Find<ComboBox>(editor).Single(control => control.Name == "SkillOrder");
        var groups = Find<ComboBox>(editor).Single(control => control.Name == "SkillBindingSelector");
        var undo = Find<Button>(editor).Single(button => button.Text == "修改前（復原）");
        var redo = Find<Button>(editor).Single(button => button.Text == "修改後（重做）");
        string original = Snapshot(editor);
        check(grid.Rows.Count == 20 && grid.RowTemplate.Height == 36 && grid.DisplayedRowCount(false) >= 15,
            "A 20-skill group fits at least 15 compact rows in the default window");
        check((string)grid.Rows[0].Cells["SkillName"].Value! == "S19" && (int)Row(editor, "S00").Cells["Rank"].Value! == 2,
            "Default decision order exposes computed rank including original-order ties");
        var retained = Row(editor, "S00");
        Edit(editor, "S00", "Priority", "1"); Edit(editor, "S00", "Weight", "100");
        check(ReferenceEquals(retained, Row(editor, "S00")) && retained.Index == 0 && ReferenceEquals(grid.CurrentRow, retained),
            "Committed ranking changes move the existing row and retain the edited skill");
        check(Read(editor).Data[0].Process.Select(skill => skill.SkillName).SequenceEqual(fixture.Data[0].Process.Select(skill => skill.SkillName)),
            "Visual sorting never rewrites the original process order");
        search.Text = "S19";
        check(grid.Rows.Count == 1 && (int)grid.Rows[0].Cells["Rank"].Value! == 2, "Filtered rows retain their rank within the entire group");
        search.Clear(); order.SelectedIndex = 1;
        check((string)grid.Rows[0].Cells["SkillName"].Value! == "S00" && Snapshot(editor) != original,
            "Original-order view leaves edited values intact");
        Load(editor, fixture); order.SelectedIndex = 0;
        Mark(editor, "S00", "S01");
        var apply = Find<Button>(editor).Single(button => button.Name == "ApplySkillBatch");
        check(!apply.Enabled, "Batch apply requires an explicitly selected field");
        Find<CheckBox>(editor).Single(control => control.Name == "ApplyPriority").Checked = true;
        Find<CheckBox>(editor).Single(control => control.Name == "ApplyCooldown").Checked = true;
        Find<NumericUpDown>(editor).Single(control => control.Name == "BatchPriority").Value = 3;
        Find<NumericUpDown>(editor).Single(control => control.Name == "BatchCooldown").Value = 30;
        apply.PerformClick();
        var changed = Read(editor);
        check(changed.Data[0].Process.Take(2).All(skill => skill.Priority == 3 && skill.CdSec == 30 && skill.SkillWeight == 80)
            && changed.Data[0].Process[2].CdSec == 2 && changed.Data[1].Process[0].CdSec == 50,
            "Batch applies only selected fields and rows in the current group");
        check(Field<Stack<string>>(editor, "undo").Count == 1, "One batch creates exactly one undo entry");
        apply.PerformClick(); check(Field<Stack<string>>(editor, "undo").Count == 1, "An unchanged batch does not add history");
        undo.PerformClick(); check(Snapshot(editor) == original, "One undo restores the entire batch including extra JSON fields");
        redo.PerformClick(); check(Read(editor).Data[0].Process.Take(2).All(skill => skill.CdSec == 30), "One redo reapplies the entire batch");
        search.Text = "S0"; Click(editor, "SelectVisibleSkills");
        check(grid.Rows.Count == 10 && grid.Rows.Cast<DataGridViewRow>().All(row => row.Cells["Marked"].Value is true),
            "Select all marks only the current search results");
        search.Text = "S1";
        check(grid.Rows.Cast<DataGridViewRow>().All(row => row.Cells["Marked"].Value is false)
            && !Find<FlowLayoutPanel>(editor).Single(control => control.Name == "SkillBatchToolbar").Visible,
            "Changing search clears marks and hides batch actions");
        Mark(editor, "S10"); groups.SelectedIndex = 1;
        check(search.Text == "" && grid.Rows.Count == 1 && grid.Rows[0].Cells["Marked"].Value is false,
            "Changing key groups clears filtering and checked rows");
        groups.SelectedIndex = 0; search.Text = "no matches";
        check(grid.Rows.Count == 0 && !Find<Button>(editor).Single(button => button.Name == "DeleteSkill").Enabled,
            "An empty search never leaves an invisible skill available for deletion");
        Find<Button>(editor).Single(button => button.Text == "新增技能").PerformClick();
        check(search.Text == "" && grid.IsCurrentCellInEditMode && grid.CurrentCell!.OwningColumn!.Name == "SkillName",
            "Add skill clears search, reveals the new row and starts name editing");
        grid.CancelEdit();
        fixture.Data[0].Process = Enumerable.Range(0, 100).Select(index => new SkillEntry { SkillName = $"Large{index:000}", CdSec = index }).ToList();
        Load(editor, fixture);
        check(grid.Rows.Count == 100, $"Reloading a larger document replaces the previous view (rows={grid.Rows.Count}, editing={grid.IsCurrentCellInEditMode}, query={search.Text})");
        var stable = Row(editor, "Large099"); grid.CurrentCell = stable.Cells["Cooldown"]; grid.FirstDisplayedScrollingRowIndex = 85;
        Edit(editor, "Large099", "Cooldown", "123");
        check(grid.Rows.Count == 100 && ReferenceEquals(stable, grid.CurrentRow) && stable.Displayed
            && Read(editor).Data[0].Process[99].CdSec == 123, "A 100-row group retains the edited row and scroll context");
        string directory = Path.Combine(root, "editor-grid"); Directory.CreateDirectory(directory);
        using (var image = new Bitmap(host.Width, host.Height))
        {
            host.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(directory, "grid-100.png"));
        }
        Mark(editor, "Large098", "Large099");
        host.ClientSize = new(1100, 740);
        foreach (float factor in new[] { 1f, 1.25f, 1.2f })
        {
            if (factor != 1) host.Scale(new SizeF(factor, factor));
            host.PerformLayout();
            foreach (var bar in Find<FlowLayoutPanel>(editor).Where(bar => bar.Dock == DockStyle.Top && bar.Visible))
                check(bar.Controls.Cast<Control>().Where(control => control.Visible).All(control => bar.ClientRectangle.Contains(control.Bounds)),
                    "Editor commands and batch controls remain accessible at minimum/scaled widths");
            check(grid.Height > 200 && grid.DisplayedRowCount(false) >= 7, "Compact grid retains useful height with the batch bar visible");
        }
        host.Close();
    }
}
