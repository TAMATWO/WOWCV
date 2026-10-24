using OpenCvSharp;
using WOWCV.Controls;

namespace WOWCV;

public partial class Form1 : Form
{
    private readonly DetectionControl detection;
    private readonly SkillEditorControl editor;
    private bool servicesDisposed;

    public Form1()
    {
        InitializeComponent();
        Cv2.SetNumThreads(1);
        Text = "WOWCV · 技能與影像工作台";
        ClientSize = new System.Drawing.Size(1440, 900);
        MinimumSize = new System.Drawing.Size(1100, 740);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        detection = new DetectionControl();
        editor = new SkillEditorControl();
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

    private void Form1_Load(object? sender, EventArgs e) => detection.StartServices();

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!editor.ConfirmDiscard()) e.Cancel = true;
        base.OnFormClosing(e);
        if (!e.Cancel) DisposeServices();
    }

    private void DisposeServices()
    {
        if (servicesDisposed) return;
        servicesDisposed = true;
        detection?.StopServices();
    }
}
