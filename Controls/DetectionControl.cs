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
    private readonly ComboBox screens = new()
    {
        Name = "SourceScreen",
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 220
    };
    private readonly NumericUpDown threshold = new()
    {
        Name = "Threshold",
        Minimum = 0,
        Maximum = 1,
        DecimalPlaces = 2,
        Increment = 0.01m,
        Width = 65
    };
    private readonly NumericUpDown processScale = new()
    {
        Name = "ProcessScale",
        Minimum = 0.1m,
        Maximum = 1,
        DecimalPlaces = 2,
        Increment = 0.05m,
        Width = 65
    };
    private readonly NumericUpDown multiScaleMin = new()
    {
        Name = "MultiScaleMin", Minimum = 0.1m, Maximum = 3,
        DecimalPlaces = 2, Increment = 0.05m, Value = 0.8m, Width = 65, Enabled = false
    };
    private readonly NumericUpDown multiScaleMax = new()
    {
        Name = "MultiScaleMax", Minimum = 0.1m, Maximum = 3,
        DecimalPlaces = 2, Increment = 0.05m, Value = 1.2m, Width = 65, Enabled = false
    };
    private readonly CheckBox multiScale = new()
    {
        Text = "多尺度",
        AutoSize = true
    };
    private readonly CheckBox useRoi = new()
    {
        Text = "僅偵測選取區",
        AutoSize = true
    };
    private readonly Button toggle = new()
    {
        Text = "開始偵測",
        AutoSize = true
    };
    private readonly Label status = new()
    {
        Dock = DockStyle.Bottom,
        Height = 42,
        Text = "尚未啟動"
    };
    private readonly Label inputStatus = new()
    {
        Dock = DockStyle.Bottom,
        Height = 25,
        Text = "Raw Input 尚未啟動"
    };
    private readonly Label configStatus = new()
    {
        Dock = DockStyle.Top,
        Height = 25,
        Text = "尚未匯入執行設定；編輯器內容不會自動套用。"
    };
    private readonly TextBox configurationPreview = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both
    };
    private readonly DataGridView results = new BufferedResultGrid
    {
        Name = "DetectionResults",
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
        RowHeadersVisible = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };
    private readonly DataGridView sampleDetails = new BufferedResultGrid
    {
        Name = "PixelSampleDetails",
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
        RowHeadersVisible = false
    };
    private readonly Label sampleStatus = new()
    {
        Name = "PixelSampleStatus",
        Dock = DockStyle.Top,
        Height = 52,
        Text = "選取一筆結果，查看五點位置與 RGB。"
    };
    private readonly System.Windows.Forms.Timer refresh = new()
    {
        Interval = 100
    };
    private string? imagePath, templateFolder;
    private string latestInput = "等待輸入", latestInputError = "";
    private bool detecting, started, stopped, configuring;
    private long version;
    private int templateRevision;
    private bool updatingResults;

    /// <summary>
    /// 建立辨識頁面並連接設定、影像處理與施放服務；背景工作由啟動方法開始。
    /// </summary>
    public DetectionControl() : this(WorkbenchConfigurationStore.DefaultPath)
    {
    }

    /// <summary>
    /// 建立辨識頁面並連接設定、影像處理與施放服務；背景工作由啟動方法開始。
    /// </summary>
    public DetectionControl(string configurationPath, ISendInput? sender = null)
    {
        casting = new SkillCastingService(sender ?? new WindowsSendInput());
        configurationStore = new WorkbenchConfigurationStore(configurationPath);
        threshold.Value = (decimal)appConfiguration.Search.Threshold;
        processScale.Value = (decimal)appConfiguration.Search.ProcessScale;
        InitializeWorkbench();
        foreach (var name in new[]
        {
            "樣版",
            "候選通過",
            "最終命中",
            "RGB符合點數",
            "原因",
            "相似度",
            "SqDiffNormed",
            "樣版倍率",
            "中心 X",
            "中心 Y",
            "寬",
            "高",
            "位置校正",
            "MSE"
        }
        )
            results.Columns.Add(name, name);
        InitializeTemplateResultDisplay(results);
        var reasonColumn = results.Columns["原因"]!;
        reasonColumn.Width = 190;
        reasonColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        foreach (var name in new[]
        {
            "點",
            "樣版位置",
            "畫面位置",
            "樣版 RGB",
            "實際 RGB",
            "符合"
        }
        )
            sampleDetails.Columns.Add(name, name);
        StabilizeColumns(results);
        StabilizeColumns(sampleDetails);
        StabilizeColumns(trackedResults);
        StabilizeColumns(trackedDetails);
        results.Columns["最終命中"]!.DisplayIndex = 2;
        results.Columns["RGB符合點數"]!.DisplayIndex = 3;
        trackedResults.Columns["樣版"]!.DisplayIndex = trackedResults.Columns.Count - 1;
        results.CurrentCellChanged += (_, _) =>
        {
            if (!updatingResults)
                ShowActiveDetails();
        };
        pixelSettings.OptionsChanged += (_, _) => Configure();
        foreach (var screen in Screen.AllScreens)
            screens.Items.Add(
                $"{screen.DeviceName} · {screen.Bounds.Width}×{screen.Bounds.Height}");
        if (screens.Items.Count > 0)
            screens.SelectedIndex = 0;
        screens.SelectedIndexChanged += (_, _) =>
        {
            if (applyingConfiguration)
                return;
            trackingResetRevision++;
            var selected = Screen.AllScreens.ElementAtOrDefault(screens.SelectedIndex);
            appConfiguration = appConfiguration with
            {
                Source = appConfiguration.Source with
                {
                    ScreenDeviceName = selected?.DeviceName
                }
            };
            ChangeSource(null);
        };
        threshold.ValueChanged += (_, _) => Configure();
        processScale.ValueChanged += (_, _) => Configure();
        multiScale.CheckedChanged += (_, _) =>
        {
            multiScaleMin.Enabled = multiScaleMax.Enabled = multiScale.Checked;
            Configure();
        };
        multiScaleMin.ValueChanged += (_, _) =>
        {
            if (applyingConfiguration)
                return;
            if (multiScaleMax.Value < multiScaleMin.Value)
                multiScaleMax.Value = multiScaleMin.Value;
            else
                Configure();
        };
        multiScaleMax.ValueChanged += (_, _) =>
        {
            if (applyingConfiguration)
                return;
            if (multiScaleMin.Value > multiScaleMax.Value)
                multiScaleMin.Value = multiScaleMax.Value;
            else
                Configure();
        };
        useRoi.CheckedChanged += (_, _) => Configure();
        preview.SelectionChanged += (_, _) =>
        {
            if (applyingConfiguration)
                return;
            trackingResetRevision++;
            ClearConfigurationWarnings("原 ROI");
            var rect = preview.Selection;
            var size = preview.Image?.Size ?? Size.Empty;
            appConfiguration = appConfiguration with
            {
                Search = appConfiguration.Search with
                {
                    Roi = rect is { } r ? new(r.X, r.Y, r.Width, r.Height, size.Width,
                size.Height) : null
                }
            };
            configuring = true;
            useRoi.Checked = preview.Selection is not null;
            configuring = false;
            preview.SelectionEnabled = false;
            preview.Cursor = Cursors.Default;
            Configure();
        };
        toggle.Click += (_, _) =>
        {
            detecting = !detecting;
            toggle.Text = detecting ? "停止偵測" : "開始偵測";
            Configure();
        };
        input.KeyboardInput += (_, e) => Volatile.Write(ref latestInput,
            $"鍵盤 {e.Key} {(e.IsDown ? "按下" : "放開")}");
        input.KeyboardInput += (_, e) => casting.KeyboardInput(e);
        pipeline.SkillsUpdated += casting.PublishSnapshot;
        pipeline.Faulted += reason => casting.Disable(reason);
        input.MouseInput += (_, e) => Volatile.Write(
            ref latestInput,
            ($"滑鼠 {e.Buttons}，滾輪 {e.VerticalWheel}/{e.HorizontalWheel}，" + $"{(e.IsAbsolute ? "絕對"
            : "相對")}位置 {e.X},{e.Y}")
        );
        input.Error += (_, e) =>
        {
            Volatile.Write(ref latestInputError, e.Message);
            casting.Disable($"Raw Input 失敗：{e.Message}");
        };
        refresh.Tick += (_, _) => RefreshOutput();
    }

    /// <summary>
    /// 還原配置後啟動辨識、輸入及施放服務與介面更新計時器。
    /// </summary>
    public void StartServices()
    {
        if (started || stopped)
            return;
        RestoreConfiguration();
        started = true;
        casting.Start();
        Configure(save: false);
        pipeline.Start();
        try
        {
            input.Start();
            inputReady = true;
        }
        catch (Exception ex)
        {
            Volatile.Write(ref latestInputError, ex.Message);
        }

        refresh.Start();
    }

    /// <summary>
    /// 停止介面更新並釋放背景服務；可重複呼叫而不重複釋放。
    /// </summary>
    public void StopServices()
    {
        if (stopped)
            return;
        stopped = true;
        refresh.Stop();
        casting.Dispose();
        pipeline.Dispose();
        input.Dispose();
        configurationStore.Dispose();
    }

    /// <summary>
    /// 發布新的辨識設定版本並清除過期顯示，依呼叫選項排程保存配置。
    /// </summary>
    private void Configure() => Configure(save: true);

    /// <summary>
    /// 發布新的辨識設定版本並清除過期顯示，依呼叫選項排程保存配置。
    /// </summary>
    private void Configure(bool save)
    {
        UpdatePreviewViewport();
        if (stopped || configuring || applyingConfiguration)
            return;
        if (save && configurationReady)
            QueueConfigurationSave();
        var allScreens = Screen.AllScreens;
        var bounds = allScreens.Length > 0 ? allScreens[Math.Clamp(screens.SelectedIndex, 0,
            allScreens.Length - 1)].Bounds : Rectangle.Empty;
        version++;
        PrepareCastingContext();
        pipeline.Configure(new(
            version,
            bounds,
            imagePath,
            templateFolder,
            (double)processScale.Value,
            (double)threshold.Value,
            multiScale.Checked,
            detecting,
            useRoi.Checked ? preview.Selection : null,
            templateRevision,
            pixelSettings.GetOptions(),
            Configuration.Bindings,
            trackingResetRevision,
            imagePath ?? allScreens.ElementAtOrDefault(screens.SelectedIndex)?.DeviceName,
            (double)multiScaleMin.Value,
            (double)multiScaleMax.Value
        ));
        results.Rows.Clear();
        sampleDetails.Rows.Clear();
        preview.SetSampleMarkers([]);
        ClearTrackingDisplay();
        ResetTemplateThumbnailsIfChanged();
        sampleStatus.Text = "等待新的辨識結果。";
    }

    /// <summary>
    /// 依 ROI 啟用狀態調整預覽範圍，保持原圖座標不變。
    /// </summary>
    private void UpdatePreviewViewport() => preview.SetViewport(useRoi.Checked ? preview.Selection
        : null);

    /// <summary>
    /// 切換螢幕或靜態圖片來源，清除舊選取與預覽並重新設定辨識。
    /// </summary>
    private void ChangeSource(string? path)
    {
        ClearConfigurationWarnings("靜態圖片", "未指定靜態圖片", "原 ROI");
        if (screens.SelectedIndex >= 0)
            ClearConfigurationWarnings("找不到原螢幕", "找不到可用螢幕");
        appConfiguration = appConfiguration with
        {
            Source = appConfiguration.Source with
            {
                Kind = path is null ? WOWCV.Models.SourceKind.Screen
            : WOWCV.Models.SourceKind.Image,
                ImagePath = path ?? appConfiguration.Source.ImagePath
            }
        };
        imagePath = path;
        preview.ClearSelection();
        var old = preview.Image;
        preview.Image = null;
        old?.Dispose();
        Configure();
    }

    /// <summary>
    /// 由檔案對話框選取靜態圖片並切換辨識來源。
    /// </summary>
    private void SelectImage()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "圖片|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            ChangeSource(dialog.FileName);
    }

    /// <summary>
    /// 載入樣版資料夾，通知編輯器並更新辨識快取版本。
    /// </summary>
    private void SelectTemplates()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "選擇技能樣版資料夾"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        templateFolder = dialog.SelectedPath;
        ClearConfigurationWarnings("樣版資料夾");
        appConfiguration = appConfiguration with
        {
            Source = appConfiguration.Source with
            {
                TemplateFolderPath = templateFolder
            }
        };
        templateRevision++;
        TemplateFolderChanged?.Invoke(this, EventArgs.Empty);
        Configure();
    }

    /// <summary>
    /// 匯入主頁專用的技能流程暫存；失敗時保留原有流程。
    /// </summary>
    private void ImportConfiguration()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "技能設定|*.txt;*.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            Configuration.Import(File.ReadAllText(dialog.FileName), out var adjustments);
            var document = Configuration.Read()!;
            configurationPreview.Text = SkillDocumentService.Serialize(document);
            configStatus.Text = ($"{Path.GetFileName(
                dialog.FileName)} · {document.Data.Count} 組按鍵 · "
                + $"{document.Data.Sum(binding => binding.Process.Count)} 個技能");
            if (adjustments.Count > 0)
                configStatus.Text += $" · {adjustments.Count} 個 cd_sec 已四捨五入（僅暫存，原檔未修改）";
            appConfiguration = appConfiguration with
            {
                SkillFlowPath = dialog.FileName
            };
            ClearConfigurationWarnings("技能流程檔");
            QueueConfigurationSave();
            Configure(save: false);
        }
        catch (Exception ex) when (ex is JsonException or IOException
            or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "匯入失敗（保留原暫存）");
        }
    }

    /// <summary>
    /// 在 UI 執行緒取用最新影像結果，更新預覽、資料列與服務狀態。
    /// </summary>
    private void RefreshOutput()
    {
        var value = pipeline.TakeOutput();
        if (value is not null)
        {
            if (value.Version != version)
                value.Dispose();
            else
            {
                var previous = preview.Image;
                preview.Image = value.Preview;
                previous?.Dispose();
                updatingResults = true;
                try
                {
                    UpdateRows(
                        results,
                        value.Rows,
                        row => row.Template,
                        row => [
                            GetTemplateThumbnail(row.Template),
                            ShortTemplateName(row.Template),
                            row.CandidatePassed ? "是" : "否",
                            row.Found ? "是" : "否",
                            PointCount(row.PixelVerification),
                            row.PixelVerification.Reason,
                            row.Similarity.ToString("F6"),
                            row.Difference.ToString("F6"),
                            row.Scale.ToString("F2"),
                            row.X,
                            row.Y,
                            row.Width,
                            row.Height,
                            row.PositionRefined ? "已校正" : "—",
                            row.Mse.ToString("F4")
                        ]
                    );
                }
                finally
                {
                    updatingResults = false;
                }

                UpdateTrackedResults(value.Skills);
                ShowActiveDetails();
                status.Text = ($"{(detecting ? "偵測中" : "僅預覽")} · 樣版命中 "
                    + $"{value.Rows.Count(row => row.Found)} / {value.Rows.Count} · 技能框命中 "
                    + $"{value.Skills.Items.Count(item => item.Found)} · {value.Status}");
                performanceStatus.Text = ($"{(imagePath is null ? "螢幕絕對中心座標" : "圖片中心座標")}\n像素 "
                    + $"{value.SourcePixels:N0} → {value.ProcessPixels:N0} · 處理 "
                    + $"{value.ElapsedMs:F1} ms\n影格 #{value.Skills.FrameId} · 擷取 "
                    + $"{value.Skills.CapturedAt.ToLocalTime():HH:mm:ss.fff}");
                preview.Invalidate();
            }
        }

        var failure = pipeline.TakeError();
        if (failure is not null)
            status.Text = failure;
        string error = Volatile.Read(ref latestInputError);
        inputStatus.Text = error.Length > 0 ? $"Raw Input：{error}"
            : ($"Raw Input：{Volatile.Read(ref latestInput)} · 按住 " + $"{string.Join(", ",
            input.PressedKeys)}");
        RefreshPersistenceStatus();
        RefreshCastingStatus();
    }

    /// <summary>
    /// 將完整五點驗證結果格式化為符合點數，未執行時顯示佔位文字。
    /// </summary>
    private static string PointCount(PixelVerificationResult result) => result.Samples.Count == 5
        ? $"{result.MatchCount}/5" : "—";

    /// <summary>
    /// 依選取的辨識結果更新展開中的五點明細及預覽取樣標記。
    /// </summary>
    private void ShowSampleDetails()
    {
        if (results.CurrentRow?.Tag is not DetectionRow row)
        {
            if (sampleDetailsExpanded)
                sampleDetails.Rows.Clear();
            sampleStatus.Text = "選取一筆結果，查看五點位置與 RGB。";
            preview.SetSampleMarkers([]);
            return;
        }

        if (sampleDetailsExpanded)
            sampleStatus.Text =
                $"{row.Template} · {row.PixelVerification.Reason}\n畫面位置為預覽圖片內座標；上表中心座標包含螢幕偏移。";
        if (sampleDetailsExpanded)
            UpdatePointDetails(sampleDetails, row.PixelVerification);
        preview.SetSampleMarkers(row.PixelVerification.Samples.Where(point =>
            point.Actual is not null).Select(point => (point.FramePoint, point.Matches,
            point.Number)));
    }

    /// <summary>
    /// 停止辨識頁面服務，釋放計時器、提示、縮圖與表格字體資源。
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopServices();
            refresh.Dispose();
            workbenchTips.Dispose();
            results.Rows.Clear();
            trackedResults.Rows.Clear();
            DisposeTemplateThumbnails();
        }

        base.Dispose(disposing);
        if (disposing)
            resultTableFont.Dispose();
    }
}
