using System.Collections;
using System.ComponentModel;
using System.Globalization;
using WOWCV.Models;

namespace WOWCV.Controls;

public sealed partial class SkillEditorControl
{
    private void InitializeGrid()
    {
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = ForeColor;
        grid.ColumnHeadersHeight = 38;
        grid.RowTemplate.Height = 36;
        grid.GridColor = Color.FromArgb(226, 232, 240);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(224, 231, 255);
        grid.DefaultCellStyle.SelectionForeColor = ForeColor;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Marked", HeaderText = "選取", Width = 48 });
        void Text(string name, string title, int width, bool readOnly = false) => grid.Columns.Add(new DataGridViewTextBoxColumn
            { Name = name, HeaderText = title, Width = width, ReadOnly = readOnly, SortMode = DataGridViewColumnSortMode.NotSortable });
        Text("Rank", "順位", 50, true); Text("SkillName", "技能名稱", 170); Text("CastKey", "施放鍵", 90);
        Text("Priority", "優先序 ↑", 80); Text("Weight", "權重 ↓", 80); Text("Cooldown", "冷卻秒", 80);
        foreach (string name in new[] { "Priority", "Weight", "Cooldown" })
        {
            NumericRange(name, out int min, out int max);
            grid.Columns[name]!.CellTemplate = new SkillIntegerCell(min, max);
        }
        Text("CooldownCheck", "冷卻檢查", 140);
        grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "Status", HeaderText = "狀態", Width = 65, Items = { "Y", "A" },
            FlatStyle = FlatStyle.Flat, ToolTipText = "保留的 Y／A 欄位；目前不影響施放決策。" });
        Text("Template", "樣版檔名", 170, true);
        grid.Columns.Add(new DataGridViewButtonColumn { Name = "PickTemplate", HeaderText = "樣版", Text = "選圖", UseColumnTextForButtonValue = true, Width = 65 });
        grid.Columns.Add(new DataGridViewButtonColumn { Name = "ClearTemplate", HeaderText = "", Text = "清除", UseColumnTextForButtonValue = true, Width = 60 });
        grid.Columns["Marked"]!.Frozen = true; grid.Columns["Rank"]!.Frozen = true; grid.Columns["SkillName"]!.Frozen = true;
        grid.CellValidating += ValidateCell;
        grid.CellValueChanged += CommitCell;
        grid.CellEndEdit += (_, e) => { grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = ""; QueueRowsRefresh(); };
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.CurrentCell is DataGridViewCheckBoxCell && grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.CurrentCellChanged += (_, _) =>
        {
            if (loading) return;
            if (grid.CurrentRow?.Tag is SkillEntry skill && SelectedBinding is { } binding) skillIndex = binding.Process.IndexOf(skill);
            UpdateStatus();
        };
        grid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || grid.Rows[e.RowIndex].Tag is not SkillEntry skill) return;
            skillIndex = SelectedBinding!.Process.IndexOf(skill);
            string column = grid.Columns[e.ColumnIndex].Name;
            if (column == "PickTemplate") SelectTemplateImage();
            else if (column == "ClearTemplate") { Edit(() => skill.TemplateFile = ""); RefreshRows(skill); }
            else if (!grid.Columns[e.ColumnIndex].ReadOnly && column != "Marked") grid.BeginEdit(true);
        };
        grid.DataError += (_, e) =>
        {
            e.ThrowException = false; e.Cancel = true;
            string message = e.Exception?.Message ?? "欄位格式不正確，請修正或按 Esc 取消輸入。";
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0) grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = message;
            footer.ForeColor = Color.DarkRed; footer.Text = message;
        };
    }

    private sealed class SkillIntegerCell : DataGridViewTextBoxCell
    {
        private int minimum, maximum;
        public SkillIntegerCell() { }
        public SkillIntegerCell(int minimum, int maximum) { this.minimum = minimum; this.maximum = maximum; }
        public override Type ValueType => typeof(int);
        public override object Clone()
        {
            var clone = (SkillIntegerCell)base.Clone(); clone.minimum = minimum; clone.maximum = maximum; return clone;
        }
        public override object ParseFormattedValue(object? formattedValue, DataGridViewCellStyle cellStyle,
            TypeConverter? formattedValueTypeConverter, TypeConverter? valueTypeConverter)
        {
            if (!int.TryParse(Convert.ToString(formattedValue), NumberStyles.Integer, CultureInfo.CurrentCulture, out int value)
                || value < minimum || value > maximum)
                throw new FormatException($"請輸入 {minimum}～{maximum} 的整數；Esc 取消。");
            return value;
        }
    }

    private static bool NumericRange(string column, out int min, out int max)
    {
        (min, max) = column switch
        {
            "Priority" => (SkillValueLimits.MinimumPriority, SkillValueLimits.MaximumPriority),
            "Weight" => (SkillValueLimits.MinimumWeight, SkillValueLimits.MaximumWeight),
            "Cooldown" => (SkillValueLimits.MinimumCooldown, SkillValueLimits.MaximumCooldown),
            _ => (0, 0)
        };
        return column is "Priority" or "Weight" or "Cooldown";
    }

    private void ValidateCell(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (loading || e.RowIndex < 0) return;
        var row = grid.Rows[e.RowIndex];
        string column = grid.Columns[e.ColumnIndex].Name;
        if (NumericRange(column, out int min, out int max)
            && (!int.TryParse(Convert.ToString(e.FormattedValue), NumberStyles.Integer, CultureInfo.CurrentCulture, out int value) || value < min || value > max))
        {
            e.Cancel = true; row.Cells[e.ColumnIndex].ErrorText = $"請輸入 {min}～{max} 的整數；Esc 取消。";
            footer.ForeColor = Color.DarkRed; footer.Text = row.Cells[e.ColumnIndex].ErrorText;
        }
        else row.Cells[e.ColumnIndex].ErrorText = "";
    }

    private void CommitCell(object? sender, DataGridViewCellEventArgs e)
    {
        if (loading || e.RowIndex < 0 || e.ColumnIndex < 0 || grid.Rows[e.RowIndex].Tag is not SkillEntry skill) return;
        var row = grid.Rows[e.RowIndex];
        string column = grid.Columns[e.ColumnIndex].Name;
        string text = Convert.ToString(row.Cells[e.ColumnIndex].Value) ?? "";
        if (column == "Marked")
        {
            if (row.Cells[e.ColumnIndex].Value is true) marked.Add(skill); else marked.Remove(skill);
            UpdateStatus(); return;
        }
        if (NumericRange(column, out int min, out int max)
            && (!int.TryParse(text, out int number) || number < min || number > max)) return;
        Edit(() =>
        {
            switch (column)
            {
                case "SkillName": skill.SkillName = text; break;
                case "CastKey": skill.CastKey = text; break;
                case "Priority": skill.Priority = int.Parse(text); break;
                case "Weight": skill.SkillWeight = int.Parse(text); break;
                case "Cooldown": skill.CdSec = int.Parse(text); break;
                case "CooldownCheck": skill.CooldownCheck = text; break;
                case "Status": skill.Status = text; break;
            }
        });
        // Reordering is deferred until editing ends, so typing never moves the active cell.
        if (!grid.IsCurrentCellInEditMode) QueueRowsRefresh();
    }

    private void QueueRowsRefresh()
    {
        if (loading || refreshQueued || !IsHandleCreated || IsDisposed) return;
        refreshQueued = true;
        BeginInvoke((Action)(() =>
        {
            refreshQueued = false;
            if (!IsDisposed && !grid.IsCurrentCellInEditMode) RefreshRows(SelectedSkill);
        }));
    }

    private sealed class SkillRowComparer(Dictionary<SkillEntry, int> positions) : IComparer
    {
        public int Compare(object? x, object? y) => positions[(SkillEntry)((DataGridViewRow)x!).Tag!]
            .CompareTo(positions[(SkillEntry)((DataGridViewRow)y!).Tag!]);
    }

    private void RefreshRows(SkillEntry? focus)
    {
        if (grid.IsCurrentCellInEditMode) return;
        string column = grid.CurrentCell?.OwningColumn?.Name ?? "SkillName";
        int scroll = grid.HorizontalScrollingOffset;
        int first = grid.FirstDisplayedScrollingRowIndex;
        var source = SelectedBinding?.Process ?? [];
        var ranked = source.OrderBy(skill => skill.Priority).ThenByDescending(skill => skill.SkillWeight).ToList();
        var ranks = ranked.Select((skill, index) => (skill, rank: index + 1)).ToDictionary(pair => pair.skill, pair => pair.rank);
        string query = search.Text.Trim();
        var visible = (order.SelectedIndex == 0 ? ranked : source).Where(skill => query.Length == 0 ||
            new[] { skill.SkillName, skill.CastKey, skill.TemplateFile, skill.CooldownCheck }
                .Any(text => text.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
        var positions = visible.Select((skill, index) => (skill, index)).ToDictionary(pair => pair.skill, pair => pair.index);
        marked.IntersectWith(visible);
        loading = true;
        try
        {
            foreach (var row in grid.Rows.Cast<DataGridViewRow>().Where(row => !positions.ContainsKey((SkillEntry)row.Tag!)).ToArray()) grid.Rows.Remove(row);
            var existing = grid.Rows.Cast<DataGridViewRow>().ToDictionary(row => (SkillEntry)row.Tag!);
            foreach (var skill in visible)
            {
                if (!existing.TryGetValue(skill, out var row)) { row = grid.Rows[grid.Rows.Add()]; row.Tag = skill; }
                object[] values = [marked.Contains(skill), ranks[skill], skill.SkillName, skill.CastKey, skill.Priority, skill.SkillWeight,
                    (int)skill.CdSec, skill.CooldownCheck, skill.Status, skill.TemplateFile];
                for (int index = 0; index < values.Length; index++)
                    if (!Equals(row.Cells[index].Value, values[index])) row.Cells[index].Value = values[index];
                row.Cells["Template"].ToolTipText = skill.TemplateFile;
                row.Cells["SkillName"].ToolTipText = skill.SkillName;
                row.Cells["CooldownCheck"].ToolTipText = skill.CooldownCheck;
            }
            if (grid.Rows.Count > 1) grid.Sort(new SkillRowComparer(positions));
            var selected = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(row => ReferenceEquals(row.Tag, focus));
            selected ??= grid.Rows.Cast<DataGridViewRow>().FirstOrDefault();
            if (selected is not null)
            {
                grid.CurrentCell = selected.Cells[column]; skillIndex = source.IndexOf((SkillEntry)selected.Tag!);
                if (first >= 0) grid.FirstDisplayedScrollingRowIndex = Math.Min(first, grid.Rows.Count - 1);
                if (!selected.Displayed) grid.FirstDisplayedScrollingRowIndex = selected.Index;
            }
            grid.HorizontalScrollingOffset = scroll;
        }
        finally { loading = false; }
        UpdateStatus();
    }
}
