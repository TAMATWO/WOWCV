using WOWCV.Services;

namespace WOWCV.Controls;

public sealed partial class DetectionControl
{
    private readonly DataGridView trackedResults = new()
    {
        Name = "TrackedSkills", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells, RowHeadersVisible = false,
        MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };
    private readonly DataGridView trackedDetails = new()
    {
        Name = "TrackedSkillDetails", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells, RowHeadersVisible = false
    };
    private readonly Label trackedStatus = new() { Name = "TrackedSkillStatus", Dock = DockStyle.Top, Height = 54 };
    private readonly TabPage trackedPage = new("已定位技能");
    private TabControl? resultTabs;
    private SkillObservationSnapshot displayedSkills = SkillObservationSnapshot.Empty;
    private long trackingResetRevision;
    public string? TemplateFolderPath => templateFolder;
    public event EventHandler? TemplateFolderChanged;

    private void InitializeTrackingPage(TabControl tabs)
    {
        resultTabs = tabs;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        var detailsPanel = new Panel { Dock = DockStyle.Fill };
        detailsPanel.Controls.Add(trackedDetails); detailsPanel.Controls.Add(trackedStatus);
        layout.Controls.Add(trackedResults, 0, 0); layout.Controls.Add(detailsPanel, 0, 1); trackedPage.Controls.Add(layout);
        tabs.TabPages.Insert(1, trackedPage);
        foreach (string name in new[] { "技能", "樣版", "施放鍵", "狀態", "符合點數", "中心 X", "中心 Y", "寬", "高", "原因" })
            trackedResults.Columns.Add(name, name);
        foreach (string name in new[] { "點", "樣版位置", "畫面位置", "樣版 RGB", "實際 RGB", "符合" }) trackedDetails.Columns.Add(name, name);
        trackedResults.CurrentCellChanged += (_, _) => { if (!updatingResults) ShowActiveDetails(); };
        tabs.SelectedIndexChanged += (_, _) => ShowActiveDetails();
        ClearTrackingDisplay();
    }

    private void ClearTrackingDisplay()
    {
        displayedSkills = SkillObservationSnapshot.Empty;
        trackedResults.Rows.Clear(); trackedDetails.Rows.Clear(); preview.SetTrackedBounds(null, false);
        trackedStatus.Text = "請匯入有樣版與施放按鍵綁定的流程；首次命中後開始持續五點驗證。";
    }

    private void UpdateTrackedResults(SkillObservationSnapshot snapshot)
    {
        var selected = (trackedResults.CurrentRow?.Tag as SkillObservation)?.Binding;
        int column = trackedResults.CurrentCell?.ColumnIndex ?? 0;
        int scroll = trackedResults.HorizontalScrollingOffset;
        int firstVisibleRow = trackedResults.FirstDisplayedScrollingRowIndex;
        updatingResults = true;
        try
        {
            displayedSkills = snapshot; trackedResults.Rows.Clear();
            foreach (var item in snapshot.Items)
            {
                var bounds = item.Bounds;
                int index = trackedResults.Rows.Add(item.Binding.SkillName, item.Binding.TemplateFile, item.Binding.CastKey,
                    StateText(item.State), PointCount(item.Verification),
                    bounds is { } box ? snapshot.Origin.X + box.X + box.Width / 2 : "—",
                    bounds is { } boxY ? snapshot.Origin.Y + boxY.Y + boxY.Height / 2 : "—",
                    bounds?.Width.ToString() ?? "—", bounds?.Height.ToString() ?? "—", item.Reason);
                trackedResults.Rows[index].Tag = item;
                if (item.Binding == selected) trackedResults.CurrentCell = trackedResults.Rows[index].Cells[column];
            }
            if (trackedResults.Rows.Count > 0 && firstVisibleRow >= 0)
                trackedResults.FirstDisplayedScrollingRowIndex = Math.Min(firstVisibleRow, trackedResults.Rows.Count - 1);
            trackedResults.HorizontalScrollingOffset = scroll;
        }
        finally { updatingResults = false; }
    }

    private static string StateText(SkillTrackingState state) => state switch
    {
        SkillTrackingState.WaitingForLocation => "等待定位", SkillTrackingState.Matched => "命中",
        SkillTrackingState.NotMatched => "未命中", SkillTrackingState.Paused => "暫停", _ => "綁定無效"
    };

    private void ShowActiveDetails()
    {
        if (resultTabs?.SelectedTab != trackedPage)
        {
            preview.SetTrackedBounds(null, false); ShowSampleDetails(); return;
        }
        trackedDetails.Rows.Clear();
        if (trackedResults.CurrentRow?.Tag is not SkillObservation observation)
        {
            trackedStatus.Text = "請匯入有樣版與施放按鍵綁定的流程；首次命中後開始持續五點驗證。";
            preview.SetSampleMarkers([]); preview.SetTrackedBounds(null, false); return;
        }
        trackedStatus.Text = $"{observation.Binding.SkillName} · {observation.Reason}\n影格 #{displayedSkills.FrameId} · 擷取 {displayedSkills.CapturedAt.ToLocalTime():HH:mm:ss.fff} · 僅驗證，不送鍵";
        foreach (var point in observation.Verification.Samples)
            trackedDetails.Rows.Add(point.Number, $"{point.TemplatePoint.X},{point.TemplatePoint.Y}",
                $"{point.FramePoint.X},{point.FramePoint.Y}", point.Expected?.ToString() ?? "越界",
                point.Actual?.ToString() ?? "越界", point.Matches ? "是" : "否");
        preview.SetSampleMarkers(observation.Verification.Samples.Where(point => point.Actual is not null)
            .Select(point => (point.FramePoint, point.Matches, point.Number)));
        preview.SetTrackedBounds(observation.Bounds, observation.Found);
    }
}
