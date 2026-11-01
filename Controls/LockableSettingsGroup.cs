using System.Drawing.Drawing2D;

namespace WOWCV.Controls;

/// <summary>集中管理設定區塊的編輯鎖與查看操作。</summary>
internal sealed class LockableSettingsGroup : GroupBox
{
    private readonly Button lockButton = new()
    {
        FlatStyle = FlatStyle.Flat,
        AccessibleName = "解鎖此區設定",
        TabStop = true
    };
    private readonly ToolTip tips = new();
    private readonly List<Control> editingContainers = [];
    private readonly string title;
    private bool locked = true;
    public event EventHandler? Locked;
    public bool IsLocked => locked;

    /// <summary>建立預設鎖定的設定區塊。</summary>
    public LockableSettingsGroup(string title, Control content)
    {
        this.title = title;
        Dock = DockStyle.Top;
        AutoSize = true;
        Padding = new Padding(6, 30, 6, 8);
        Controls.Add(content);
        WrapEditors(content);
        lockButton.FlatAppearance.BorderSize = 0;
        lockButton.Click += (_, _) =>
        {
            locked = !locked;
            UpdateLock();
            if (locked)
                Locked?.Invoke(this, EventArgs.Empty);
        };
        Controls.Add(lockButton);
        UpdateLock();
    }

    /// <summary>以父容器停用編輯元件，保留元件原本的啟用條件。</summary>
    private void WrapEditors(Control parent)
    {
        foreach (Control child in parent.Controls.Cast<Control>().ToArray())
        {
            if (child is Button && Equals(child.Tag, "LockView"))
                continue;
            if (child is Button or CheckBox or NumericUpDown or ComboBox or TextBox)
            {
                int index = parent.Controls.GetChildIndex(child);
                var wrapper = new Panel
                {
                    AutoSize = true, Dock = child.Dock, Margin = child.Margin,
                    MinimumSize = child.Size
                };
                if (parent is TableLayoutPanel table)
                {
                    var position = table.GetPositionFromControl(child);
                    int columns = table.GetColumnSpan(child);
                    int rows = table.GetRowSpan(child);
                    table.Controls.Remove(child);
                    table.Controls.Add(wrapper, position.Column, position.Row);
                    table.SetColumnSpan(wrapper, columns);
                    table.SetRowSpan(wrapper, rows);
                }
                else
                {
                    parent.Controls.Remove(child);
                    parent.Controls.Add(wrapper);
                    parent.Controls.SetChildIndex(wrapper, index);
                }
                child.Location = Point.Empty;
                wrapper.Controls.Add(child);
                editingContainers.Add(wrapper);
            }
            else
                WrapEditors(child);
        }
    }

    /// <summary>更新鎖頭、可操作區域與無障礙說明。</summary>
    private void UpdateLock()
    {
        foreach (var container in editingContainers)
            container.Enabled = !locked;
        Text = locked ? title + " · 已鎖定" : title;
        string hint = locked ? "解鎖此區設定" : "鎖定此區設定";
        tips.SetToolTip(lockButton, hint);
        lockButton.AccessibleName = hint;
        UpdateIcon();
    }

    /// <summary>依目前 DPI 直接繪製鎖頭，不載入外部圖形套件。</summary>
    private void UpdateIcon()
    {
        int size = Math.Max(16, (int)Math.Round(16 * DeviceDpi / 96.0));
        var icon = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(icon))
        using (var pen = new Pen(Color.FromArgb(64, 64, 64), 1.8f))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.ScaleTransform(size / 24f, size / 24f);
            pen.StartCap = pen.EndCap = LineCap.Round;
            graphics.DrawRectangle(pen, 5, 10, 14, 11);
            if (locked)
            {
                graphics.DrawArc(pen, 8, 3, 8, 8, 180, 180);
                graphics.DrawLine(pen, 8, 7, 8, 10);
                graphics.DrawLine(pen, 16, 7, 16, 10);
            }
            else
            {
                graphics.DrawArc(pen, 12, 2, 8, 8, 180, 180);
                graphics.DrawLine(pen, 12, 6, 12, 10);
            }
            graphics.DrawLine(pen, 12, 14, 12, 17);
        }
        var old = lockButton.Image;
        lockButton.Image = icon;
        old?.Dispose();
    }

    /// <summary>配置右上角操作區。</summary>
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int size = (int)Math.Round(28 * DeviceDpi / 96.0);
        lockButton.SetBounds(ClientSize.Width - size - 6, 0, size, size);
    }

    /// <summary>隨 DPI 重新建立圖示。</summary>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateIcon();
    }

    /// <summary>釋放圖示與提示資源。</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lockButton.Image?.Dispose();
            tips.Dispose();
        }
        base.Dispose(disposing);
    }
}