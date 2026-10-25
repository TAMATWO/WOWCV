using System.Text.Json;
using WOWCV.Services;

namespace WOWCV.Controls;

public sealed partial class DetectionControl : UserControl
{
    private readonly DetectionPipeline pipeline = new();
    private readonly RawInputService input = new();
    public RuntimeConfiguration Configuration { get; } = new();
    private readonly RoiPictureBox preview = new();
    private readonly PixelVerificationControl pixelSettings = new();
    private readonly ComboBox screens = new() { Name = "SourceScreen", DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly NumericUpDown threshold = new() { Name = "Threshold", Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.01m, Width = 65 };
    private readonly NumericUpDown processScale = new() { Name = "ProcessScale", Minimum = 0.1m, Maximum = 1, DecimalPlaces = 2, Increment = 0.05m, Width = 65 };
    private readonly CheckBox multiScale = new() { Text = "多尺度 0.8～1.2", AutoSize = true };
    private readonly CheckBox useRoi = new() { Text = "僅偵測選取區", AutoSize = true };
    private readonly Button toggle = new() { Text = "開始偵測", AutoSize = true };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 42, Text = "尚未啟動" };
    private readonly Label inputStatus = new() { Dock = DockStyle.Bottom, Height = 25, Text = "Raw Input 尚未啟動" };
    private readonly Label configStatus = new() { Dock = DockStyle.Top, Height = 25, Text = "尚未匯入執行設定；編輯器內容不會自動套用。" };
    private readonly TextBox configurationPreview = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both };
    private readonly DataGridView results = new() { Name = "DetectionResults", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells, RowHeadersVisible = false,
        MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly DataGridView sampleDetails = new() { Name = "PixelSampleDetails", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells, RowHeadersVisible = false };
    private readonly Label sampleStatus = new() { Name = "PixelSampleStatus", Dock = DockStyle.Top, Height = 52, Text = "選取一筆結果，查看五點位置與 RGB。" };
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 100 };
    private string? imagePath, templateFolder;
    private string latestInput = "等待輸入", latestInputError = "";
    private bool detecting, started, stopped, configuring;
    private long version;
    private int templateRevision;
    private bool updatingResults;

    public DetectionControl() : this(WorkbenchConfigurationStore.DefaultPath) { }

    public DetectionControl(string configurationPath, ISendInput? sender = null)
    {
        casting = new SkillCastingService(sender ?? new WindowsSendInput());
        configurationStore = new WorkbenchConfigurationStore(configurationPath);
        threshold.Value = (decimal)appConfiguration.Search.Threshold;
        processScale.Value = (decimal)appConfiguration.Search.ProcessScale;
        var toolbar = new FlowLayoutPanel { Name = "DetectionToolbar", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(5), WrapContents = true };
        Button Add(string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += (_, _) => action(); toolbar.Controls.Add(button); return button;
        }
        toolbar.Controls.Add(screens);
        Add("切換螢幕來源", () => ChangeSource(null));
        Add("靜態圖片", SelectImage);
        Add("載入樣版資料夾", SelectTemplates);
        toolbar.Controls.Add(new Label { Text = "門檻", AutoSize = true }); toolbar.Controls.Add(threshold);
        toolbar.Controls.Add(new Label { Text = "處理倍率", AutoSize = true }); toolbar.Controls.Add(processScale);
        toolbar.Controls.Add(multiScale); toolbar.Controls.Add(useRoi); toolbar.Controls.Add(pixelSettings.VerificationToggle);
        Add("框選偵測範圍", () =>
        {
            preview.SelectionEnabled = !preview.SelectionEnabled;
            preview.Cursor = preview.SelectionEnabled ? Cursors.Cross : Cursors.Default;
            UpdatePreviewViewport();
            status.Text = preview.SelectionEnabled ? "請在預覽上拖曳框選範圍。" : "已取消框選。";
        });
        Add("清除範圍", () => preview.ClearSelection());
        toolbar.Controls.Add(toggle);
        Add("重新偵測", Configure);
        Add("重新定位技能", () => { trackingResetRevision++; Configure(save: false); });
        Add("匯入 TXT / JSON 暫存", ImportConfiguration);
        var toolsHost = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 1 };
        toolsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); toolsHost.Controls.Add(toolbar);
        InitializeCastingControls(toolsHost);
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1300, 650), SplitterDistance = 800 };
        split.Panel1.Controls.Add(preview);
        var right = new TabControl { Dock = DockStyle.Fill };
        var matches = new TabPage("辨識結果");
        var resultLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        resultLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        resultLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 65)); resultLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        var detailPanel = new Panel { Dock = DockStyle.Fill };
        detailPanel.Controls.Add(sampleDetails); detailPanel.Controls.Add(sampleStatus);
        resultLayout.Controls.Add(results, 0, 0); resultLayout.Controls.Add(detailPanel, 0, 1); matches.Controls.Add(resultLayout);
        var config = new TabPage("執行設定暫存"); config.Controls.Add(configurationPreview); config.Controls.Add(configStatus);
        var sampling = new TabPage("五點設定"); sampling.Controls.Add(pixelSettings);
        right.TabPages.AddRange([matches, sampling, config]); split.Panel2.Controls.Add(right);
        InitializeTrackingPage(right);
        Add("五點位置／容差設定", () => right.SelectedTab = sampling);
        Controls.Add(split); Controls.Add(toolsHost); Controls.Add(castingStatus); Controls.Add(status); Controls.Add(inputStatus); Controls.Add(persistenceStatus);
        foreach (var name in new[] { "樣版", "候選通過", "最終命中", "RGB符合點數", "原因", "相似度", "SqDiffNormed", "樣版倍率", "中心 X", "中心 Y", "寬", "高", "位置校正", "MSE" })
            results.Columns.Add(name, name);
        InitializeTemplateResultDisplay(results);
        var reasonColumn = results.Columns["原因"]!;
        reasonColumn.Width = 190; reasonColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        foreach (var name in new[] { "點", "樣版位置", "畫面位置", "樣版 RGB", "實際 RGB", "符合" }) sampleDetails.Columns.Add(name, name);
        results.CurrentCellChanged += (_, _) => { if (!updatingResults) ShowActiveDetails(); };
        pixelSettings.OptionsChanged += (_, _) => Configure();
        foreach (var screen in Screen.AllScreens) screens.Items.Add($"{screen.DeviceName} · {screen.Bounds.Width}×{screen.Bounds.Height}");
        if (screens.Items.Count > 0) screens.SelectedIndex = 0;
        screens.SelectedIndexChanged += (_, _) =>
        {
            if (applyingConfiguration) return;
            trackingResetRevision++;
            var selected = Screen.AllScreens.ElementAtOrDefault(screens.SelectedIndex);
            appConfiguration = appConfiguration with { Source = appConfiguration.Source with { ScreenDeviceName = selected?.DeviceName } };
            ChangeSource(null);
        };
        threshold.ValueChanged += (_, _) => Configure(); processScale.ValueChanged += (_, _) => Configure();
        multiScale.CheckedChanged += (_, _) => Configure(); useRoi.CheckedChanged += (_, _) => Configure();
        preview.SelectionChanged += (_, _) =>
        {
            if (applyingConfiguration) return;
            trackingResetRevision++;
            ClearConfigurationWarnings("原 ROI");
            var rect = preview.Selection;
            var size = preview.Image?.Size ?? Size.Empty;
            appConfiguration = appConfiguration with { Search = appConfiguration.Search with
            {
                Roi = rect is { } r ? new(r.X, r.Y, r.Width, r.Height, size.Width, size.Height) : null
            } };
            configuring = true; useRoi.Checked = preview.Selection is not null; configuring = false;
            preview.SelectionEnabled = false; preview.Cursor = Cursors.Default; Configure();
        };
        toggle.Click += (_, _) => { detecting = !detecting; toggle.Text = detecting ? "停止偵測" : "開始偵測"; Configure(); };
        input.KeyboardInput += (_, e) => Volatile.Write(ref latestInput, $"鍵盤 {e.Key} {(e.IsDown ? "按下" : "放開")}");
        input.KeyboardInput += (_, e) => casting.KeyboardInput(e);
        pipeline.SkillsUpdated += casting.PublishSnapshot;
        pipeline.Faulted += reason => casting.Disable(reason);
        input.MouseInput += (_, e) => Volatile.Write(ref latestInput,
            $"滑鼠 {e.Buttons}，滾輪 {e.VerticalWheel}/{e.HorizontalWheel}，{(e.IsAbsolute ? "絕對" : "相對")}位置 {e.X},{e.Y}");
        input.Error += (_, e) => { Volatile.Write(ref latestInputError, e.Message); casting.Disable($"Raw Input 失敗：{e.Message}"); };
        refresh.Tick += (_, _) => RefreshOutput();
    }

    public void StartServices()
    {
        if (started || stopped) return;
        RestoreConfiguration();
        started = true; casting.Start(); Configure(save: false); pipeline.Start();
        try { input.Start(); inputReady = true; }
        catch (Exception ex) { Volatile.Write(ref latestInputError, ex.Message); }
        refresh.Start();
    }
    public void StopServices()
    {
        if (stopped) return;
        stopped = true; refresh.Stop(); casting.Dispose(); pipeline.Dispose(); input.Dispose(); configurationStore.Dispose();
    }
    private void Configure() => Configure(save: true);
    private void Configure(bool save)
    {
        UpdatePreviewViewport();
        if (stopped || configuring || applyingConfiguration) return;
        if (save && configurationReady) QueueConfigurationSave();
        var allScreens = Screen.AllScreens;
        var bounds = allScreens.Length > 0 ? allScreens[Math.Clamp(screens.SelectedIndex, 0, allScreens.Length - 1)].Bounds : Rectangle.Empty;
        version++;
        PrepareCastingContext();
        pipeline.Configure(new(version, bounds, imagePath, templateFolder, (double)processScale.Value,
            (double)threshold.Value, multiScale.Checked, detecting, useRoi.Checked ? preview.Selection : null, templateRevision,
            pixelSettings.GetOptions(), Configuration.Bindings, trackingResetRevision,
            imagePath ?? allScreens.ElementAtOrDefault(screens.SelectedIndex)?.DeviceName));
        results.Rows.Clear(); sampleDetails.Rows.Clear(); preview.SetSampleMarkers([]);
        ClearTrackingDisplay();
        ResetTemplateThumbnailsIfChanged();
        sampleStatus.Text = "等待新的辨識結果。";
    }
    private void UpdatePreviewViewport() => preview.SetViewport(useRoi.Checked ? preview.Selection : null);
    private void ChangeSource(string? path)
    {
        ClearConfigurationWarnings("靜態圖片", "未指定靜態圖片", "原 ROI");
        if (screens.SelectedIndex >= 0) ClearConfigurationWarnings("找不到原螢幕", "找不到可用螢幕");
        appConfiguration = appConfiguration with { Source = appConfiguration.Source with
        {
            Kind = path is null ? WOWCV.Models.SourceKind.Screen : WOWCV.Models.SourceKind.Image,
            ImagePath = path ?? appConfiguration.Source.ImagePath
        } };
        imagePath = path; preview.ClearSelection();
        var old = preview.Image; preview.Image = null; old?.Dispose(); Configure();
    }
    private void SelectImage()
    {
        using var dialog = new OpenFileDialog { Filter = "圖片|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff" };
        if (dialog.ShowDialog(this) == DialogResult.OK) ChangeSource(dialog.FileName);
    }
    private void SelectTemplates()
    {
        using var dialog = new FolderBrowserDialog { Description = "選擇技能樣版資料夾" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        templateFolder = dialog.SelectedPath;
        ClearConfigurationWarnings("樣版資料夾");
        appConfiguration = appConfiguration with { Source = appConfiguration.Source with { TemplateFolderPath = templateFolder } };
        templateRevision++; TemplateFolderChanged?.Invoke(this, EventArgs.Empty); Configure();
    }
    private void ImportConfiguration()
    {
        using var dialog = new OpenFileDialog { Filter = "技能設定|*.txt;*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Configuration.Import(File.ReadAllText(dialog.FileName));
            var document = Configuration.Read()!;
            configurationPreview.Text = SkillDocumentService.Serialize(document);
            configStatus.Text = $"{Path.GetFileName(dialog.FileName)} · {document.Data.Count} 組按鍵 · {document.Data.Sum(binding => binding.Process.Count)} 個技能";
            appConfiguration = appConfiguration with { SkillFlowPath = dialog.FileName };
            ClearConfigurationWarnings("技能流程檔");
            QueueConfigurationSave();
            Configure(save: false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, ex.Message, "匯入失敗（保留原暫存）"); }
    }
    private void RefreshOutput()
    {
        var value = pipeline.TakeOutput();
        if (value is not null)
        {
            if (value.Version != version) value.Dispose();
            else
            {
                var previous = preview.Image; preview.Image = value.Preview; previous?.Dispose();
                string? selectedTemplate = (results.CurrentRow?.Tag as DetectionRow)?.Template;
                int selectedColumn = results.CurrentCell?.ColumnIndex ?? 0;
                int horizontalOffset = results.HorizontalScrollingOffset;
                int firstVisibleRow = results.FirstDisplayedScrollingRowIndex;
                updatingResults = true;
                try
                {
                    results.Rows.Clear();
                    foreach (var row in value.Rows)
                    {
                        int index = results.Rows.Add(GetTemplateThumbnail(row.Template)!, ShortTemplateName(row.Template), row.CandidatePassed ? "是" : "否", row.Found ? "是" : "否",
                            PointCount(row.PixelVerification), row.PixelVerification.Reason,
                            row.Similarity.ToString("F6"), row.Difference.ToString("F6"),
                            row.Scale.ToString("F2"), row.X, row.Y, row.Width, row.Height,
                            row.PositionRefined ? "已校正" : "—", row.Mse.ToString("F4"));
                        results.Rows[index].Tag = row;
                        results.Rows[index].Cells["樣版"].ToolTipText = row.Template;
                        results.Rows[index].Cells["縮圖"].ToolTipText = row.Template;
                        if (row.Template == selectedTemplate) results.CurrentCell = results.Rows[index].Cells[selectedColumn];
                    }
                    if (results.Rows.Count > 0 && firstVisibleRow >= 0)
                        results.FirstDisplayedScrollingRowIndex = Math.Min(firstVisibleRow, results.Rows.Count - 1);
                    results.HorizontalScrollingOffset = horizontalOffset;
                }
                finally { updatingResults = false; }
                UpdateTrackedResults(value.Skills); ShowActiveDetails();
                status.Text = $"{(imagePath is null ? "螢幕絕對中心座標" : "圖片中心座標")} · {value.Status} · {(detecting ? "偵測中" : "僅預覽")}\n" +
                    $"像素 {value.SourcePixels:N0} → {value.ProcessPixels:N0} · 處理 {value.ElapsedMs:F1} ms · 候選 {value.Rows.Count(row => row.CandidatePassed)} / 樣版命中 {value.Rows.Count(row => row.Found)} / 技能框命中 {value.Skills.Items.Count(item => item.Found)}";
                preview.Invalidate();
            }
        }
        var failure = pipeline.TakeError(); if (failure is not null) status.Text = failure;
        string error = Volatile.Read(ref latestInputError);
        inputStatus.Text = error.Length > 0 ? $"Raw Input：{error}"
            : $"Raw Input：{Volatile.Read(ref latestInput)} · 按住 {string.Join(", ", input.PressedKeys)}";
        RefreshPersistenceStatus();
        RefreshCastingStatus();
    }
    private static string PointCount(PixelVerificationResult result) => result.Samples.Count == 5 ? $"{result.MatchCount}/5" : "—";

    private void ShowSampleDetails()
    {
        sampleDetails.Rows.Clear();
        if (results.CurrentRow?.Tag is not DetectionRow row)
        {
            sampleStatus.Text = "選取一筆結果，查看五點位置與 RGB。"; preview.SetSampleMarkers([]); return;
        }
        sampleStatus.Text = $"{row.Template} · {row.PixelVerification.Reason}\n畫面位置為預覽圖片內座標；上表中心座標包含螢幕偏移。";
        foreach (var point in row.PixelVerification.Samples)
            sampleDetails.Rows.Add(point.Number, $"{point.TemplatePoint.X},{point.TemplatePoint.Y}",
                $"{point.FramePoint.X},{point.FramePoint.Y}", point.Expected?.ToString() ?? "越界",
                point.Actual?.ToString() ?? "越界", point.Matches ? "是" : "否");
        preview.SetSampleMarkers(row.PixelVerification.Samples.Where(point => point.Actual is not null)
            .Select(point => (point.FramePoint, point.Matches, point.Number)));
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { StopServices(); refresh.Dispose(); results.Rows.Clear(); trackedResults.Rows.Clear(); DisposeTemplateThumbnails(); }
        base.Dispose(disposing);
        if (disposing) resultTableFont.Dispose();
    }
}
