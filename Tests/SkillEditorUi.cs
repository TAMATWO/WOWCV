using System.Reflection;
using WOWCV.Controls;
using WOWCV.Models;
using WOWCV.Services;

internal static class SkillEditorUi
{
    internal static IEnumerable<T> Find<T>(Control parent) where T : Control => parent.Controls.Cast<Control>()
        .SelectMany(child => (child is T match ? new[] { match } : Array.Empty<T>()).Concat(Find<T>(child)));
    internal static DataGridView Grid(SkillEditorControl editor) => Find<DataGridView>(editor).Single(control => control.Name == "SkillOverview");
    internal static DataGridViewRow Row(SkillEditorControl editor, string name) => Grid(editor).Rows.Cast<DataGridViewRow>()
        .Single(row => (string)row.Cells["SkillName"].Value! == name);
    internal static T Field<T>(SkillEditorControl editor, string name) => (T)typeof(SkillEditorControl)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
    internal static object? Invoke(SkillEditorControl editor, string name, params object[] args) => typeof(SkillEditorControl)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, args);
    internal static string Snapshot(SkillEditorControl editor) => (string)Invoke(editor, "Snapshot")!;
    internal static SkillDocument Read(SkillEditorControl editor) => SkillDocumentService.Parse(Snapshot(editor));
    internal static void Load(SkillEditorControl editor, SkillDocument document) => Invoke(editor, "ApplyImportedDocument",
        SkillDocumentService.Parse(SkillDocumentService.Serialize(document)), Array.Empty<string>(), "fixture.json");
    internal static bool Edit(SkillEditorControl editor, string skill, string column, string text)
    {
        var grid = Grid(editor);
        grid.EndEdit();
        grid.CurrentCell = Row(editor, skill).Cells[column];
        grid.BeginEdit(true);
        ((TextBox)grid.EditingControl!).Text = text;
        bool committed = grid.EndEdit();
        Application.DoEvents();
        return committed;
    }
    internal static void Mark(SkillEditorControl editor, params string[] names)
    {
        Grid(editor).EndEdit();
        foreach (string name in names) Row(editor, name).Cells["Marked"].Value = true;
    }
    internal static void ClickCell(SkillEditorControl editor, string skill, string column)
    {
        var grid = Grid(editor); grid.EndEdit();
        var cell = Row(editor, skill).Cells[column]; grid.CurrentCell = cell;
        typeof(DataGridView).GetMethod("OnCellClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(grid, [new DataGridViewCellEventArgs(cell.ColumnIndex, cell.RowIndex)]);
    }
    internal static void Click(SkillEditorControl editor, string name) => Find<Button>(editor).Single(button => button.Name == name).PerformClick();
}
