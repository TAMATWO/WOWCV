using System.Reflection;
using WOWCV.Controls;
using WOWCV.Models;
using WOWCV.Services;

internal static class SkillEditorUi
{
    /// <summary>
    /// 遞迴找出指定型別的子控制項，供介面驗證定位。
    /// </summary>
    internal static IEnumerable<T> Find<T>(Control parent)
        where T : Control => parent.Controls.Cast<Control>().SelectMany(child => (child is T match
        ? new[] { match } : Array.Empty<T>()).Concat(Find<T>(child)));

    /// <summary>
    /// 找出技能編輯器的主表格。
    /// </summary>
    internal static DataGridView Grid(SkillEditorControl editor) => Find<DataGridView>(editor)
        .Single(control => control.Name == "SkillOverview");

    /// <summary>
    /// 依技能名稱找出測試要操作的資料列。
    /// </summary>
    internal static DataGridViewRow Row(SkillEditorControl editor,
        string name) => Grid(editor).Rows.Cast<DataGridViewRow>()
        .Single(row => (string)row.Cells["SkillName"].Value! == name);

    /// <summary>
    /// 透過反射取得驗證所需的物件欄位。
    /// </summary>
    internal static T Field<T>(SkillEditorControl editor,
        string name) => (T)typeof(SkillEditorControl).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;

    /// <summary>
    /// 透過反射呼叫待驗證的非公開方法。
    /// </summary>
    internal static object? Invoke(SkillEditorControl editor, string name,
        params object[] args) => typeof(SkillEditorControl).GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, args);

    /// <summary>
    /// 取得編輯器文件的序列化快照，供驗證變更與復原結果。
    /// </summary>
    internal static string Snapshot(SkillEditorControl editor) => (string)Invoke(editor,
        "Snapshot")!;

    /// <summary>
    /// 讀取並解析隔離測試使用的資料或編輯器快照。
    /// </summary>
    internal static SkillDocument Read(SkillEditorControl editor) => SkillDocumentService.Parse(
        Snapshot(editor));

    /// <summary>
    /// 套用獨立測試文件，重設編輯器選取與歷史。
    /// </summary>
    internal static void Load(SkillEditorControl editor, SkillDocument document) => Invoke(
        editor,
        "ApplyImportedDocument",
        SkillDocumentService.Parse(SkillDocumentService.Serialize(document)),
        Array.Empty<string>(),
        "fixture.json"
    );

    /// <summary>
    /// 透過真實儲存格編輯控制項輸入文字，回傳是否成功提交。
    /// </summary>
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

    /// <summary>
    /// 勾選指定技能列，供批次修改與刪除驗證使用。
    /// </summary>
    internal static void Mark(SkillEditorControl editor, params string[] names)
    {
        Grid(editor).EndEdit();
        foreach (string name in names)
            Row(editor, name).Cells["Marked"].Value = true;
    }

    /// <summary>
    /// 模擬點選指定技能的儲存格，觸發列內選圖或清除操作。
    /// </summary>
    internal static void ClickCell(SkillEditorControl editor, string skill, string column)
    {
        var grid = Grid(editor);
        grid.EndEdit();
        var cell = Row(editor, skill).Cells[column];
        grid.CurrentCell = cell;
        typeof(DataGridView).GetMethod("OnCellClick",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(grid,
            [new DataGridViewCellEventArgs(cell.ColumnIndex, cell.RowIndex)]);
    }

    /// <summary>
    /// 依控制項名稱執行編輯器按鈕操作。
    /// </summary>
    internal static void Click(SkillEditorControl editor,
        string name) => Find<Button>(editor).Single(button => button.Name == name).PerformClick();
}
