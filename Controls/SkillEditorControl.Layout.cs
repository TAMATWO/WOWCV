using WOWCV.Models;

namespace WOWCV.Controls;

public sealed partial class SkillEditorControl
{
    /// <summary>
    /// 建立編輯器操作按鈕，執行前先提交尚未完成的有效編輯。
    /// </summary>
    private Button MakeButton(string title, Action action, string? name = null)
    {
        var button = new Button
        {
            Name = name ?? "",
            Text = title,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            Padding = new Padding(6, 3, 6, 3),
            Margin = new Padding(0, 0, 6, 4)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        button.Click += (_, _) =>
        {
            if (CommitPendingEdits())
                action();
        };
        return button;
    }

    /// <summary>
    /// 建立受技能數值範圍限制的批次整數輸入欄位。
    /// </summary>
    private static NumericUpDown BatchNumber(string name, int min, int max, int value) => new()
    {
        Name = name,
        Minimum = min,
        Maximum = max,
        Value = value,
        Width = 70,
        DecimalPlaces = 0,
        Increment = 1,
        Enabled = false
    };

    /// <summary>
    /// 建立工具列中使用的欄位標題。
    /// </summary>
    private static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(4, 6, 6, 0)
    };

    /// <summary>
    /// 建立檔案操作、按鍵組搜尋、技能表格及按需顯示的批次工具列。
    /// </summary>
    private void InitializeEditorLayout()
    {
        var toolbar = new FlowLayoutPanel
        {
            Name = "SkillEditorToolbar",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(12, 10, 6, 4),
            BackColor = Color.White
        };
        undoButton = MakeButton("修改前（復原）", () => Restore(undo, redo));
        redoButton = MakeButton("修改後（重做）", () => Restore(redo, undo));
        deleteSkillButton = MakeButton("刪除技能", DeleteSkill, "DeleteSkill");
        deleteBindingButton = MakeButton("刪除按鍵組", DeleteBinding, "DeleteSkillBinding");
        toolbar.Controls.AddRange([
            MakeButton("開啟 JSON / TXT", ImportDocument),
            MakeButton("儲存 JSON / TXT", SaveDocument),
            MakeButton("新增按鍵組", AddBinding),
            MakeButton("新增技能", AddSkill),
            deleteSkillButton,
            deleteBindingButton,
            undoButton,
            redoButton
        ]);
        order.Items.AddRange(["決策順序", "原始順序"]);
        order.SelectedIndex = 0;
        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(12, 8, 6, 2)
        };
        filters.Controls.AddRange([
            Caption("按鍵組"),
            bindingList,
            Caption("觸發鍵"),
            pressInput,
            Caption("搜尋"),
            search,
            order,
            MakeButton(
                "全選搜尋結果",
                () =>
                {
                    foreach (DataGridViewRow row in grid.Rows)
                        marked.Add((SkillEntry)row.Tag!);
                    RefreshRows(SelectedSkill);
                },
                "SelectVisibleSkills"
            ),
            MakeButton(
                "取消選取",
                () =>
                {
                    marked.Clear();
                    RefreshRows(SelectedSkill);
                }
            ),
            selectionCount
        ]);
        tips.SetToolTip(search, "搜尋目前按鍵組的技能名稱、施放鍵、冷卻檢查與樣版檔名；修改搜尋會清除勾選。");
        tips.SetToolTip(order, "只改變顯示順序，不更動原始文件排列；順位始終依整組計算。");
        applyBatchButton = MakeButton("套用選取欄位", ApplyBatch, "ApplySkillBatch");
        batch.Controls.AddRange([
            Caption("批次修改"),
            batchPriority,
            priorityValue,
            batchWeight,
            weightValue,
            batchCooldown,
            cooldownValue,
            applyBatchButton
        ]);
        foreach (var (check, input) in new[]
        {
            (batchPriority, priorityValue),
            (batchWeight, weightValue),
            (batchCooldown, cooldownValue)
        }
        )
        {
            check.Margin = new Padding(6, 7, 2, 0);
            check.CheckedChanged += (_, _) =>
            {
                input.Enabled = check.Checked;
                UpdateStatus();
            };
            input.ValueChanged += (_, _) =>
            {
                decimal integer = decimal.Round(input.Value, 0, MidpointRounding.AwayFromZero);
                if (input.Value != integer)
                    input.Value = integer;
            };
        }

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 6, 12, 6)
        };
        body.Controls.Add(grid);
        Controls.Add(body);
        Controls.Add(batch);
        Controls.Add(filters);
        Controls.Add(toolbar);
        Controls.Add(templateStatus);
        Controls.Add(footer);
        bindingList.SelectedIndexChanged += (_, _) =>
        {
            if (loading)
                return;
            int next = bindingList.SelectedIndex;
            if (!CommitPendingEdits())
            {
                loading = true;
                bindingList.SelectedIndex = bindingIndex;
                loading = false;
                return;
            }

            marked.Clear();
            bindingIndex = next;
            skillIndex = 0;
            loading = true;
            search.Clear();
            loading = false;
            RefreshEditor();
        };
        pressInput.Validated += (_, _) =>
        {
            if (loading || SelectedBinding is not { } binding)
                return;
            Edit(() => binding.Press = pressInput.Text);
            RefreshEditor();
        };
        search.TextChanged += (_, _) =>
        {
            if (loading)
                return;
            marked.Clear();
            RefreshRows(SelectedSkill);
        };
        order.SelectedIndexChanged += (_, _) =>
        {
            if (!loading && CommitPendingEdits())
                RefreshRows(SelectedSkill);
        };
    }
}
