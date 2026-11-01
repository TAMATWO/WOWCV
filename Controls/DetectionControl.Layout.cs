namespace WOWCV.Controls;

public sealed partial class DetectionControl
{
    private bool sampleDetailsExpanded, trackedDetailsExpanded;
    private readonly Label performanceStatus = new()
    {
        AutoSize = true,
        Dock = DockStyle.Top
    };
    private readonly Label sourceStatus = new()
    {
        AutoSize = true,
        Dock = DockStyle.Top,
        MaximumSize = new Size(195, 0)
    };
    private readonly Label configurationPathStatus = new()
    {
        Dock = DockStyle.Top,
        Height = 36,
        AutoEllipsis = true
    };
    private readonly ToolTip workbenchTips = new();

    /// <summary>
    /// 建立主頁監控布局，將設定、預覽、結果與診斷資訊分區呈現。
    /// </summary>
    private void InitializeWorkbench()
    {
        DoubleBuffered = true;
        Padding = new Padding(6);
        var toolbar = new FlowLayoutPanel
        {
            Name = "DetectionToolbar",
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(2, 4, 2, 6)
        };
        var settingsToggle = ActionButton("收合設定", () =>
        {
        });
        toolbar.Controls.AddRange([
            settingsToggle,
            toggle,
            castingEnabled,
            ActionButton(
                "重新定位技能",
                () =>
                {
                    trackingResetRevision++;
                    Configure(save: false);
                }
            )
        ]);
        var settings = new Panel
        {
            Name = "DetectionSettings",
            Dock = DockStyle.Left,
            Width = 240,
            AutoScroll = true,
            Padding = new Padding(0, 0, 8, 0)
        };
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settings.Controls.Add(stack);
        void Group(string title, params Control[] controls)
        {
            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var control in controls)
            {
                control.Dock = DockStyle.Top;
                control.Margin = new Padding(3, 4, 3, 4);
                body.Controls.Add(control, 0, body.RowCount++);
            }

            var group = new LockableSettingsGroup(title, body);
            if (title == "偵測範圍")
                group.Locked += (_, _) =>
                {
                    preview.SelectionEnabled = false;
                    preview.Cursor = Cursors.Default;
                };
            stack.Controls.Add(group, 0, stack.RowCount++);
        }

        Group(
            "畫面來源",
            screens,
            ActionButton("切換螢幕來源", () => ChangeSource(null)),
            ActionButton("靜態圖片", SelectImage)
        );
        Group(
            "樣版與流程",
            ActionButton("載入樣版資料夾", SelectTemplates),
            ActionButton("匯入 TXT / JSON 暫存", ImportConfiguration),
            sourceStatus
        );
        var scaleRange = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1
        };
        scaleRange.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        scaleRange.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        scaleRange.Controls.Add(SettingField("最小", multiScaleMin), 0, 0);
        scaleRange.Controls.Add(SettingField("最大", multiScaleMax), 1, 0);
        workbenchTips.SetToolTip(processScale,
            "對畫面來源（螢幕擷取或載入圖片）及所有模板同步縮放。"
            + "1 表示維持原尺寸；0.5 表示寬高各減半，以減少運算量，"
            + "不改變模板相對於畫面的大小。");
        const string scaleTip = "適用於載入模板資料夾內的所有圖片。"
            + "先比對原尺寸，未達門檻時，再將各模板依設定範圍放大縮小後搜尋；"
            + "不改變畫面來源尺寸。";
        workbenchTips.SetToolTip(multiScale, scaleTip);
        workbenchTips.SetToolTip(multiScaleMin, scaleTip);
        workbenchTips.SetToolTip(multiScaleMax, scaleTip);
        Group(
            "辨識參數",
            SettingField("門檻", threshold),
            SettingField("處理倍率", processScale),
            multiScale,
            scaleRange,
            ActionButton("重新偵測", Configure)
        );
        Group(
            "偵測範圍",
            useRoi,
            ActionButton(
                "框選偵測範圍",
                () =>
                {
                    preview.SelectionEnabled = !preview.SelectionEnabled;
                    preview.Cursor = preview.SelectionEnabled ? Cursors.Cross : Cursors.Default;
                    UpdatePreviewViewport();
                    status.Text = preview.SelectionEnabled ? "請在預覽上拖曳框選範圍。" : "已取消框選。";
                }
            ),
            ActionButton("清除範圍", () => preview.ClearSelection())
        );
        pixelSettings.Dock = DockStyle.Top;
        pixelSettings.Height = 570;
        pixelSettings.Visible = false;
        var pixelToggle = ActionButton("展開五點設定", () =>
        {
        });
        pixelToggle.Click += (_, _) =>
        {
            pixelSettings.Visible = !pixelSettings.Visible;
            pixelToggle.Text = pixelSettings.Visible ? "收合五點設定" : "展開五點設定";
        };
        pixelToggle.Tag = "LockView";
        Group("五點驗證", pixelSettings.VerificationToggle, pixelToggle, pixelSettings);
        var castingSettings = new TableLayoutPanel
        {
            Name = "CastingToolbar",
            AutoSize = true,
            ColumnCount = 1
        };
        castingSettings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        InitializeCastingControls(castingSettings);
        Group("施放參數", castingSettings);
        settingsToggle.Click += (_, _) =>
        {
            settings.Visible = !settings.Visible;
            settingsToggle.Text = settings.Visible ? "收合設定" : "展開設定";
        };
        var split = new SplitContainer
        {
            Name = "MonitorSplit",
            Dock = DockStyle.Fill,
            Size = new Size(1160, 600),
            SplitterDistance = 690,
            Panel1MinSize = 220,
            Panel2MinSize = 310
        };
        split.Panel1.Controls.Add(preview);
        var right = new TabControl
        {
            Dock = DockStyle.Fill
        };
        var matches = new TabPage("辨識結果");
        matches.Controls.Add(ResultPanel(results, sampleDetails, sampleStatus, false));
        var config = new TabPage("執行設定暫存");
        config.Controls.Add(configurationPreview);
        config.Controls.Add(configStatus);
        right.TabPages.AddRange([matches, config]);
        split.Panel2.Controls.Add(right);
        InitializeTrackingPage(right);
        right.SelectedTab = trackedPage;
        status.Height = 25;
        status.AutoEllipsis = true;
        castingStatus.Height = 42;
        castingStatus.AutoEllipsis = true;
        var diagnostics = new Panel
        {
            Name = "DetectionDiagnostics",
            Dock = DockStyle.Top,
            Height = 100,
            Visible = false,
            AutoScroll = true
        };
        inputStatus.Dock = DockStyle.Top;
        diagnostics.Controls.Add(configurationPathStatus);
        diagnostics.Controls.Add(inputStatus);
        diagnostics.Controls.Add(performanceStatus);
        var diagnosticToggle = ActionButton("展開診斷資訊", () =>
        {
        });
        diagnosticToggle.Dock = DockStyle.Top;
        diagnosticToggle.Click += (_, _) =>
        {
            diagnostics.Visible = !diagnostics.Visible;
            diagnosticToggle.Text = diagnostics.Visible ? "收合診斷資訊" : "展開診斷資訊";
        };
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        persistenceStatus.Dock = DockStyle.Top;
        persistenceStatus.Height = 24;
        status.Dock = DockStyle.Top;
        castingStatus.Dock = DockStyle.Top;
        footer.Controls.Add(diagnostics);
        footer.Controls.Add(diagnosticToggle);
        footer.Controls.Add(persistenceStatus);
        footer.Controls.Add(castingStatus);
        footer.Controls.Add(status);
        var workspace = new Panel
        {
            Dock = DockStyle.Fill
        };
        workspace.Controls.Add(split);
        workspace.Controls.Add(settings);
        Controls.Add(workspace);
        Controls.Add(footer);
        Controls.Add(toolbar);
    }

    /// <summary>
    /// 建立結果表格與可收合明細面板，切換時顯示目前選取項目的資料。
    /// </summary>
    private Control ResultPanel(DataGridView table, DataGridView details, Label notice,
        bool tracked)
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Size = new Size(450, 600),
            SplitterDistance = 350,
            Panel2Collapsed = true
        };
        split.Panel1.Controls.Add(table);
        split.Panel2.Controls.Add(details);
        split.Panel2.Controls.Add(notice);
        var button = ActionButton("顯示五點明細", () =>
        {
        });
        button.Name = tracked ? "ToggleTrackedDetails" : "ToggleSampleDetails";
        button.Dock = DockStyle.Top;
        button.Click += (_, _) =>
        {
            split.Panel2Collapsed = !split.Panel2Collapsed;
            if (tracked)
                trackedDetailsExpanded = !split.Panel2Collapsed;
            else
                sampleDetailsExpanded = !split.Panel2Collapsed;
            button.Text = split.Panel2Collapsed ? "顯示五點明細" : "收合五點明細";
            ShowActiveDetails();
        };
        var panel = new Panel
        {
            Dock = DockStyle.Fill
        };
        panel.Controls.Add(split);
        panel.Controls.Add(button);
        return panel;
    }

    /// <summary>
    /// 建立按鈕並連接指定操作。
    /// </summary>
    private static Button ActionButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true
        };
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>
    /// 建立包含欄位標題與輸入控制項的設定區塊。
    /// </summary>
    private static Control SettingField(string title, Control input)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Text = title, AutoSize = true,
            Anchor = AnchorStyles.Left }, 0, 0);
        input.Dock = DockStyle.Fill;
        panel.Controls.Add(input, 0, 1);
        return panel;
    }
}
