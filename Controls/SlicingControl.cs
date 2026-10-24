using OpenCvSharp.Extensions;
using WOWCV.Services;

namespace WOWCV.Controls;

public sealed class SlicingControl : UserControl
{
    private readonly RoiPictureBox preview = new() { SelectionEnabled = true };
    private readonly NumericUpDown rows = new() { Minimum = 1, Maximum = 100, Value = 1, Width = 65 };
    private readonly NumericUpDown columns = new() { Minimum = 1, Maximum = 100, Value = 12, Width = 65 };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 45 };
    private readonly Button export = new() { Text = "匯出 PNG", AutoSize = true };
    private readonly CancellationTokenSource lifetime = new();
    private Task<string>? exportTask;
    private bool stopping;

    public SlicingControl()
    {
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 55, Padding = new Padding(8) };
        var load = new Button { Text = "載入技能欄圖片", AutoSize = true };
        load.Click += (_, _) => LoadImage();
        toolbar.Controls.AddRange([load, new Label { Text = "列數", AutoSize = true }, rows,
            new Label { Text = "欄數", AutoSize = true }, columns, export]);
        Controls.Add(preview); Controls.Add(toolbar); Controls.Add(status);
        rows.ValueChanged += (_, _) => RefreshGrid(); columns.ValueChanged += (_, _) => RefreshGrid();
        preview.SelectionChanged += (_, _) => RefreshGrid();
        export.Click += async (_, _) => await ExportAsync();
        RefreshGrid();
    }
    private void LoadImage()
    {
        using var dialog = new OpenFileDialog { Filter = "圖片|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tif;*.tiff" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using var mat = OpenCvSharp.Cv2.ImRead(dialog.FileName);
            if (mat.Empty()) throw new IOException("無法讀取圖片。");
            var image = BitmapConverter.ToBitmap(mat);
            var old = preview.Image; preview.Image = image; old?.Dispose(); preview.ClearSelection();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "載入失敗"); }
    }
    private void RefreshGrid()
    {
        preview.GridRows = (int)rows.Value; preview.GridColumns = (int)columns.Value; preview.Invalidate();
        status.Text = preview.Selection is { } rect
            ? $"範圍 X={rect.X}, Y={rect.Y}, {rect.Width}×{rect.Height}；預計 {rows.Value * columns.Value} 張 PNG。"
            : "載入圖片後，在預覽上拖曳框選技能欄；格線即為切圖預覽。";
    }
    private async Task ExportAsync()
    {
        if (preview.Image is not Bitmap image || preview.Selection is not { } rect)
        { MessageBox.Show(this, "請先載入圖片並框選範圍。"); return; }
        using var dialog = new FolderBrowserDialog { Description = "選擇輸出位置（將建立新子目錄）" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        int r = (int)rows.Value, c = (int)columns.Value;
        try
        {
            SkillSlicingService.GetCells(rect, r, c);
            string parent = dialog.SelectedPath;
            var bitmap = new Bitmap(image);
            var token = lifetime.Token;
            export.Enabled = false;
            exportTask = Task.Run(() =>
            {
                using (bitmap)
                using (var mat = BitmapConverter.ToMat(bitmap))
                    return SkillSlicingService.Export(mat, rect, r, c, parent, token);
            });
            string path = await exportTask;
            if (!stopping) status.Text = $"已輸出 {r * c} 張：{path}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!stopping) MessageBox.Show(this, ex.Message, "匯出失敗"); }
        finally { if (!stopping) export.Enabled = true; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !stopping)
        {
            stopping = true; lifetime.Cancel();
            try { exportTask?.GetAwaiter().GetResult(); } catch (Exception) { /* Export handler reports errors while open. */ }
            lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
