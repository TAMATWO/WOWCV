using System.Text.Json;
using WOWCV.Models;
using WOWCV.Services;

namespace WOWCV.Controls;

/// <summary>Edits a global immutable options snapshot; its enable checkbox is hosted by the main toolbar.</summary>
public sealed class PixelVerificationControl : UserControl
{
    public CheckBox VerificationToggle { get; } = new() { Text = "五點驗證", Checked = true, AutoSize = true };
    private readonly NumericUpDown tolerance = new() { Minimum = 0, Maximum = 255, Width = 80 };
    private readonly NumericUpDown minimum = new() { Minimum = 1, Maximum = 5, Value = 5, Width = 80 };
    private readonly NumericUpDown[] x = new NumericUpDown[5], y = new NumericUpDown[5];
    private readonly Label fileStatus = new() { AutoSize = true, MaximumSize = new Size(180, 0), Text = "所有技能共用；辨識設定與技能流程檔分開保存。" };
    private bool updating;
    public event EventHandler? OptionsChanged;

    public PixelVerificationControl()
    {
        Dock = DockStyle.Fill; AutoScroll = true;
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        void AddWide(Control control, int row) { layout.Controls.Add(control, 0, row); layout.SetColumnSpan(control, 3); }
        AddWide(new Label { Text = "五點位置：相對於每張樣版中心的原始像素偏移。", AutoSize = true, MaximumSize = new Size(180, 0) }, 0);
        var toleranceLabel = new Label { Text = "RGB 容差 ±", AutoSize = true };
        layout.Controls.Add(toleranceLabel, 0, 1); layout.SetColumnSpan(toleranceLabel, 2); layout.Controls.Add(tolerance, 2, 1);
        var minimumLabel = new Label { Text = "至少通過點數", AutoSize = true };
        layout.Controls.Add(minimumLabel, 0, 2); layout.SetColumnSpan(minimumLabel, 2); layout.Controls.Add(minimum, 2, 2);
        tolerance.Width = minimum.Width = 60;
        layout.Controls.Add(new Label { Text = "取樣點", AutoSize = true }, 0, 3);
        layout.Controls.Add(new Label { Text = "X 偏移", AutoSize = true }, 1, 3);
        layout.Controls.Add(new Label { Text = "Y 偏移", AutoSize = true }, 2, 3);
        string[] names = ["1 中心", "2 上", "3 下", "4 左", "5 右"];
        for (int index = 0; index < 5; index++)
        {
            x[index] = CreateOffset(); y[index] = CreateOffset();
            layout.Controls.Add(new Label { Text = names[index], AutoSize = true, Margin = new Padding(3, 7, 3, 3) }, 0, index + 4);
            layout.Controls.Add(x[index], 1, index + 4); layout.Controls.Add(y[index], 2, index + 4);
            x[index].ValueChanged += Changed; y[index].ValueChanged += Changed;
        }
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var load = new Button { Text = "匯入辨識設定", AutoSize = true };
        var save = new Button { Text = "匯出辨識設定", AutoSize = true };
        load.Click += (_, _) => Import(); save.Click += (_, _) => Export();
        buttons.Controls.AddRange([load, save]); AddWide(buttons, 9); AddWide(fileStatus, 10);
        AddWide(new Label { Text = "RGB 容差是每個通道的允許差值。\n取樣越界會拒絕命中；不同尺寸不會縮放後比較。", AutoSize = true, MaximumSize = new Size(180, 0) }, 11);
        Controls.Add(layout);
        VerificationToggle.CheckedChanged += Changed; tolerance.ValueChanged += Changed; minimum.ValueChanged += Changed;
        ApplyOptions(new PixelVerificationOptions());
    }

    private static NumericUpDown CreateOffset() => new() { Minimum = int.MinValue, Maximum = int.MaxValue, Width = 60 };
    private void Changed(object? sender, EventArgs e) { if (!updating) OptionsChanged?.Invoke(this, EventArgs.Empty); }
    public PixelVerificationOptions GetOptions()
    {
        SampleOffset Point(int index) => new((int)x[index].Value, (int)y[index].Value);
        return new()
        {
            Enabled = VerificationToggle.Checked, RgbTolerance = (int)tolerance.Value, MinimumMatches = (int)minimum.Value,
            Center = Point(0), Up = Point(1), Down = Point(2), Left = Point(3), Right = Point(4)
        };
    }
    public void ApplyOptions(PixelVerificationOptions options)
    {
        options.Validate();
        updating = true;
        try
        {
            VerificationToggle.Checked = options.Enabled; tolerance.Value = options.RgbTolerance; minimum.Value = options.MinimumMatches;
            var offsets = options.Offsets;
            for (int index = 0; index < 5; index++) { x[index].Value = offsets[index].X; y[index].Value = offsets[index].Y; }
        }
        finally { updating = false; }
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Import()
    {
        using var dialog = new OpenFileDialog { Filter = "辨識設定 JSON|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var options = PixelVerificationSettingsService.Parse(File.ReadAllText(dialog.FileName));
            ApplyOptions(options); fileStatus.Text = $"已匯入：{dialog.FileName}";
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, ex.Message, "匯入失敗（保留原設定）"); }
    }
    private void Export()
    {
        using var dialog = new SaveFileDialog { Filter = "辨識設定 JSON|*.json", FileName = "pixel-verification.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, PixelVerificationSettingsService.Serialize(GetOptions()), new System.Text.UTF8Encoding(false));
            fileStatus.Text = $"已匯出：{dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, ex.Message, "匯出失敗"); }
    }
}
