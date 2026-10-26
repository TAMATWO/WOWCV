using WOWCV.Services;

namespace WOWCV.Controls;

public sealed partial class DetectionControl
{
    private readonly DataGridView trackedResults = new BufferedResultGrid()
    {
        Name = "TrackedSkills", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells, RowHeadersVisible = false,
        MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };
    private readonly DataGridView trackedDetails = new BufferedResultGrid()
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
        trackedPage.Controls.Add(ResultPanel(trackedResults, trackedDetails, trackedStatus, true));
        tabs.TabPages.Insert(0, trackedPage);
        foreach (string name in new[] { "技能", "樣版", "施放鍵", "狀態", "符合點數", "中心 X", "中心 Y", "寬", "高", "原因" })
            trackedResults.Columns.Add(name, name);
        InitializeTemplateResultDisplay(trackedResults);
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
        updatingResults = true;
        try
        {
            displayedSkills = snapshot;
            UpdateRows(trackedResults, snapshot.Items, item => item.Binding, item =>
            [
                GetTemplateThumbnail(item.Binding.TemplateFile), item.Binding.SkillName,
                ShortTemplateName(item.Binding.TemplateFile), item.Binding.CastKey,
                StateText(item.State), PointCount(item.Verification),
                item.Bounds is { } box ? snapshot.Origin.X + box.X + box.Width / 2 : "—",
                item.Bounds is { } boxY ? snapshot.Origin.Y + boxY.Y + boxY.Height / 2 : "—",
                item.Bounds?.Width.ToString() ?? "—", item.Bounds?.Height.ToString() ?? "—", item.Reason
            ]);
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
        if (trackedResults.CurrentRow?.Tag is not SkillObservation observation)
        {
            if (trackedDetailsExpanded) trackedDetails.Rows.Clear();
            trackedStatus.Text = "請匯入有樣版與施放按鍵綁定的流程；首次命中後開始持續五點驗證。";
            preview.SetSampleMarkers([]); preview.SetTrackedBounds(null, false); return;
        }
        if (trackedDetailsExpanded)
            trackedStatus.Text = $"{observation.Binding.SkillName} · {observation.Reason}\n五點畫面位置為來源圖片內座標；表格中心座標包含螢幕偏移。";
        if (trackedDetailsExpanded) UpdatePointDetails(trackedDetails, observation.Verification);
        preview.SetSampleMarkers(observation.Verification.Samples.Where(point => point.Actual is not null)
            .Select(point => (point.FramePoint, point.Matches, point.Number)));
        preview.SetTrackedBounds(observation.Bounds, observation.Found);
    }
}
