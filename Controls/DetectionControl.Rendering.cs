using WOWCV.Services;

namespace WOWCV.Controls;

public sealed partial class DetectionControl
{
    // Reconcile membership by identity; a new frame must not replace a selected row.
    /// <summary>
    /// 依項目身分保留既有資料列，只更新變動值並增刪實際異動的項目。
    /// </summary>
    private static void UpdateRows<T>(DataGridView table, IReadOnlyList<T> items, Func<T,
        object> key, Func<T, object?[]> cells)
        where T : class
    {
        var remaining = table.Rows.Cast<DataGridViewRow>().Where(row => row.Tag is T)
            .GroupBy(row => key((T)row.Tag!)).ToDictionary(group => group.Key,
            group => new Queue<DataGridViewRow>(group));
        foreach (var item in items)
        {
            object identity = key(item);
            DataGridViewRow row;
            if (remaining.TryGetValue(identity, out var matches) && matches.Count > 0)
                row = matches.Dequeue();
            else
                row = table.Rows[table.Rows.Add()];
            row.Tag = item;
            UpdateCells(row, cells(item));
            string? template = item switch
            {
                DetectionRow candidate => candidate.Template,
                SkillObservation skill => skill.Binding.TemplateFile,
                _ => null
            };
            if (template is not null)
            {
                row.Cells["樣版"].ToolTipText = template;
                row.Cells["縮圖"].ToolTipText = template;
            }
        }

        foreach (var row in remaining.Values.SelectMany(queue => queue))
            table.Rows.Remove(row);
    }

    /// <summary>
    /// 只寫入與現值不同的儲存格，降低不必要的重繪。
    /// </summary>
    private static void UpdateCells(DataGridViewRow row, object?[] values)
    {
        for (int column = 0; column < values.Length; column++)
            if (!Equals(row.Cells[column].Value, values[column]))
                row.Cells[column].Value = values[column];
    }

    /// <summary>
    /// 依最新取樣結果更新五點明細，盡量重用既有資料列。
    /// </summary>
    private static void UpdatePointDetails(DataGridView table, PixelVerificationResult verification)
    {
        while (table.Rows.Count > verification.Samples.Count)
            table.Rows.RemoveAt(table.Rows.Count - 1);
        for (int index = 0; index < verification.Samples.Count; index++)
        {
            if (index == table.Rows.Count)
                table.Rows.Add();
            var point = verification.Samples[index];
            UpdateCells(
                table.Rows[index],
                [
                    point.Number,
                    $"{point.TemplatePoint.X},{point.TemplatePoint.Y}",
                    $"{point.FramePoint.X},{point.FramePoint.Y}",
                    point.Expected?.ToString() ?? "越界",
                    point.Actual?.ToString() ?? "越界",
                    point.Matches ? "是" : "否"
                ]
            );
        }
    }

    /// <summary>
    /// 設定固定欄寬並停用欄位排序，避免刷新資料造成版面跳動。
    /// </summary>
    private static void StabilizeColumns(DataGridView table)
    {
        table.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        foreach (DataGridViewColumn column in table.Columns)
        {
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            if (column.AutoSizeMode != DataGridViewAutoSizeColumnMode.NotSet)
                continue;
            column.Width = column.Name switch
            {
                "技能" => 100,
                "施放鍵" => 65,
                "狀態" => 75,
                "符合點數" => 90,
                "原因" => 190,
                "樣版位置" or "畫面位置" => 100,
                "樣版 RGB" or "實際 RGB" => 145,
                "候選通過" or "最終命中" => 80,
                "RGB符合點數" => 105,
                _ => 85
            };
        }
    }
}
