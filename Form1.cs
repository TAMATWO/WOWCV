using OpenCvSharp;
using WOWCV.Controls;
using WOWCV.Services;
using WOWCV.Models;

namespace WOWCV;

public partial class Form1 : Form
{
    private readonly DetectionControl detection;
    private readonly SkillEditorControl editor;
    private bool servicesDisposed;
    private WorkbenchTheme? theme;
    private readonly ThemeToggleButton themeButton = new();

    /// <summary>
    /// 建立主視窗，整合辨識、樣版切圖及技能流程三個分頁。
    /// </summary>
    public Form1() : this(WorkbenchConfigurationStore.DefaultPath)
    {
    }

    /// <summary>
    /// 建立主視窗，整合辨識、樣版切圖及技能流程三個分頁。
    /// </summary>
    public Form1(string configurationPath)
    {
        InitializeComponent();
        Cv2.SetNumThreads(1);
        Text = "WOWCV · 技能與影像工作台";
        ClientSize = new System.Drawing.Size(1440, 900);
        MinimumSize = new System.Drawing.Size(1100, 740);
        var tabs = new ThemeTabControl
        {
            Dock = DockStyle.Fill
        };
        detection = new DetectionControl(configurationPath);
        editor = new SkillEditorControl();
        detection.TemplateFolderChanged += (_,
            _) => editor.SetTemplateFolder(detection.TemplateFolderPath);
        AddPage("主頁 · 影像辨識", detection);
        AddPage("建立技能樣版", new SlicingControl());
        AddPage("技能流程設定", editor);
        Controls.Add(tabs);
        Controls.Add(themeButton);
        themeButton.BringToFront();
        theme = new WorkbenchTheme(this);
        theme.Apply(detection.SelectedTheme);
        themeButton.UpdateTheme(detection.SelectedTheme);
        themeButton.Click += (_, _) =>
        {
            var selected = detection.SelectedTheme == UiTheme.Dark ? UiTheme.Light : UiTheme.Dark;
            theme.Apply(selected);
            themeButton.UpdateTheme(selected);
            detection.SaveTheme(selected);
        };
        Layout += (_, _) => PositionThemeButton();
        tabs.Layout += (_, _) => PositionThemeButton();
        DpiChanged += (_, _) => PositionThemeButton();
        PositionThemeButton();

        // 使用既有分頁標籤列的右側空白，不保留額外列高或改動內容區大小。
        void PositionThemeButton()
        {
            if (tabs.TabCount == 0)
                return;
            var header = tabs.GetTabRect(0);
            int inset = LogicalToDeviceUnits(2);
            int side = Math.Max(1, header.Height - inset * 2);
            themeButton.SetBounds(tabs.Right - side - LogicalToDeviceUnits(8),
                tabs.Top + header.Top + inset, side, side);
        }
        void AddPage(string title, Control content)
        {
            var page = new TabPage(title);
            content.Dock = DockStyle.Fill;
            page.Controls.Add(content);
            tabs.TabPages.Add(page);
        }
    }

    /// <summary>
    /// 主視窗完成載入後啟動辨識頁面的背景服務。
    /// </summary>
    private void Form1_Load(object? sender, EventArgs e)
    {
        detection.StartServices();
        theme?.Apply(detection.SelectedTheme);
        themeButton.UpdateTheme(detection.SelectedTheme);
    }

    /// <summary>
    /// 關閉前確認編輯內容、保存配置並停止背景服務。
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!editor.ConfirmDiscard())
            e.Cancel = true;
        base.OnFormClosing(e);
        if (!e.Cancel)
        {
            string? error = detection.FlushConfiguration();
            if (error is not null)
                MessageBox.Show(this, error, "配置未能保存", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            DisposeServices();
        }
    }

    /// <summary>
    /// 以可重複呼叫的方式停止辨識頁面服務。
    /// </summary>
    private void DisposeServices()
    {
        if (servicesDisposed)
            return;
        servicesDisposed = true;
        theme?.Dispose();
        detection?.StopServices();
    }
}
