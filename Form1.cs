using OpenCvSharp;
using WOWCV.Controls;
using WOWCV.Services;

namespace WOWCV;

public partial class Form1 : Form
{
    private readonly DetectionControl detection;
    private readonly SkillEditorControl editor;
    private bool servicesDisposed;

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
        var tabs = new TabControl
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
    private void Form1_Load(object? sender, EventArgs e) => detection.StartServices();

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
        detection?.StopServices();
    }
}
