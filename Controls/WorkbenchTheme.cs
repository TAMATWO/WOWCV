using WOWCV.Models;

namespace WOWCV.Controls;

/// <summary>於 UI 執行緒套用外觀，保留淺色原值並追蹤動態狀態與新控件。</summary>
internal sealed class WorkbenchTheme : IDisposable
{
    private readonly Dictionary<Control, Appearance> appearances = [];
    private bool applying;
    private UiTheme theme;

    private sealed class Appearance
    {
        public Color Background;
        public Color Foreground;
        public FlatStyle? ButtonStyle;
        public bool VisualButton;
        public Color Border, Hover, Pressed, Checked;
        public Color GridBackground, GridLines;
        public bool HeaderVisualStyles;
        public FlatStyle? ComboStyle;
        public DrawMode ComboDrawMode;
        public BorderStyle? InputBorder;
        public BorderStyle GridBorder;
        public DataGridViewHeaderBorderStyle ColumnBorder, RowBorder;
        public readonly Dictionary<DataGridViewColumn, FlatStyle> ColumnStyles = [];
        public readonly List<(DataGridViewCellStyle Style, DataGridViewCellStyle Light)> Cells = [];
    }

    /// <summary>記住所有現有控件的淺色外觀，之後只修改色彩相關屬性。</summary>
    public WorkbenchTheme(Control root) => Attach(root);

    /// <summary>註冊控件外觀與生命週期；新增的表格編輯控件亦使用目前主題。</summary>
    private void Attach(Control control)
    {
        if (appearances.ContainsKey(control))
            return;
        var value = new Appearance
        {
            Background = control.BackColor,
            Foreground = control.ForeColor
        };
        if (theme == UiTheme.Dark && control.Parent is { } parent
            && appearances.TryGetValue(parent, out var parentAppearance))
        {
            if (control.ForeColor == parent.ForeColor)
                value.Foreground = parentAppearance.Foreground;
            if (control.BackColor == parent.BackColor)
                value.Background = parentAppearance.Background;
        }
        if (control is ButtonBase button)
        {
            value.ButtonStyle = button.FlatStyle;
            value.VisualButton = button.UseVisualStyleBackColor;
            value.Border = button.FlatAppearance.BorderColor;
            value.Hover = button.FlatAppearance.MouseOverBackColor;
            value.Pressed = button.FlatAppearance.MouseDownBackColor;
            value.Checked = button.FlatAppearance.CheckedBackColor;
        }
        if (control is DataGridView grid)
        {
            value.GridBackground = grid.BackgroundColor;
            value.GridLines = grid.GridColor;
            value.HeaderVisualStyles = grid.EnableHeadersVisualStyles;
            value.GridBorder = grid.BorderStyle;
            value.ColumnBorder = grid.ColumnHeadersBorderStyle;
            value.RowBorder = grid.RowHeadersBorderStyle;
            foreach (var style in new[] { grid.DefaultCellStyle,
                grid.ColumnHeadersDefaultCellStyle, grid.RowHeadersDefaultCellStyle,
                grid.RowsDefaultCellStyle, grid.AlternatingRowsDefaultCellStyle })
                value.Cells.Add((style, style.Clone()));
            grid.EditingControlShowing += EditingControlShowing;
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column is DataGridViewButtonColumn buttonColumn)
                    value.ColumnStyles.Add(column, buttonColumn.FlatStyle);
                else if (column is DataGridViewComboBoxColumn comboColumn)
                    value.ColumnStyles.Add(column, comboColumn.FlatStyle);
            }
        }
        if (control is ComboBox combo)
        {
            value.ComboStyle = combo.FlatStyle;
            value.ComboDrawMode = combo.DrawMode;
            combo.DrawItem += DrawComboItem;
        }
        if (control is TextBoxBase text)
            value.InputBorder = text.BorderStyle;
        else if (control is UpDownBase number)
            value.InputBorder = number.BorderStyle;
        appearances.Add(control, value);
        control.BackColorChanged += BackgroundChanged;
        control.ForeColorChanged += ForegroundChanged;
        control.ControlAdded += ControlAdded;
        control.Disposed += ControlDisposed;
        control.HandleCreated += HandleCreated;
        if (control is Button)
            control.Paint += PaintDisabledButton;
        if (control is CheckBox)
            control.Paint += PaintDisabledCheckBox;
        if (control is GroupBox)
            control.Paint += PaintGroup;
        foreach (Control child in control.Controls)
            Attach(child);
    }

    /// <summary>切換整棵控件樹的外觀，不更新任何業務值、焦點或布局尺寸。</summary>
    public void Apply(UiTheme value)
    {
        theme = value;
        applying = true;
        try
        {
            foreach (var (control, appearance) in appearances.ToArray())
                if (!control.IsDisposed)
                    ApplyControl(control, appearance);
        }
        finally
        {
            applying = false;
        }
    }

    /// <summary>將一般外觀與表格樣式套用到指定控件，保留影像預覽背景。</summary>
    private void ApplyControl(Control control, Appearance value)
    {
        bool dark = theme == UiTheme.Dark;
        if (control is not PictureBox)
            control.BackColor = dark ? Background(value.Background, control) : value.Background;
        control.ForeColor = dark ? Foreground(value.Foreground) : value.Foreground;
        if (control is ThemeTabControl tabs)
            tabs.Theme = theme;
        if (control is TextBoxBase text && value.InputBorder is { } textBorder)
            text.BorderStyle = dark ? BorderStyle.FixedSingle : textBorder;
        else if (control is UpDownBase number && value.InputBorder is { } numberBorder)
            number.BorderStyle = dark ? BorderStyle.FixedSingle : numberBorder;
        if (control is ComboBox combo && value.ComboStyle is { } comboStyle)
        {
            combo.FlatStyle = dark ? FlatStyle.Flat : comboStyle;
            combo.DrawMode = dark ? DrawMode.OwnerDrawFixed : value.ComboDrawMode;
        }
        if (control is ButtonBase button && value.ButtonStyle is { } buttonStyle)
        {
            button.UseVisualStyleBackColor = dark ? false : value.VisualButton;
            button.FlatStyle = dark ? FlatStyle.Flat : buttonStyle;
            button.FlatAppearance.BorderColor = dark
                ? Color.FromArgb(65, 70, 76) : value.Border;
            button.FlatAppearance.MouseOverBackColor = dark
                ? Color.FromArgb(58, 63, 71) : value.Hover;
            button.FlatAppearance.MouseDownBackColor = dark
                ? Color.FromArgb(50, 55, 63) : value.Pressed;
            button.FlatAppearance.CheckedBackColor = dark
                ? Color.FromArgb(35, 76, 50) : value.Checked;
        }
        if (control is DataGridView grid)
        {
            grid.BackgroundColor = dark ? Color.FromArgb(38, 40, 43) : value.GridBackground;
            grid.GridColor = dark ? Color.FromArgb(53, 57, 63) : value.GridLines;
            grid.EnableHeadersVisualStyles = dark ? false : value.HeaderVisualStyles;
            grid.BorderStyle = dark ? BorderStyle.None : value.GridBorder;
            grid.ColumnHeadersBorderStyle = dark
                ? DataGridViewHeaderBorderStyle.Single : value.ColumnBorder;
            grid.RowHeadersBorderStyle = dark
                ? DataGridViewHeaderBorderStyle.Single : value.RowBorder;
            foreach (var (column, lightStyle) in value.ColumnStyles)
            {
                if (column is DataGridViewButtonColumn buttonColumn)
                    buttonColumn.FlatStyle = dark ? FlatStyle.Flat : lightStyle;
                else if (column is DataGridViewComboBoxColumn comboColumn)
                    comboColumn.FlatStyle = dark ? FlatStyle.Flat : lightStyle;
            }
            for (int index = 0; index < value.Cells.Count; index++)
            {
                var (style, light) = value.Cells[index];
                style.BackColor = dark ? Color.FromArgb(39, 42, 46) : light.BackColor;
                if (dark && index is 1 or 2)
                    style.BackColor = Color.FromArgb(45, 48, 52);
                if (dark && index == 4)
                    style.BackColor = Color.FromArgb(42, 45, 49);
                style.ForeColor = dark ? Foreground(light.ForeColor) : light.ForeColor;
                style.SelectionBackColor = dark
                    ? Color.FromArgb(58, 69, 83) : light.SelectionBackColor;
                style.SelectionForeColor = dark
                    ? Color.FromArgb(204, 209, 216) : light.SelectionForeColor;
            }
            if (grid.EditingControl is { } editor)
                ApplyEditor(editor);
        }
        control.Invalidate();
        NativeScrollTheme.Apply(control, dark);
    }

    /// <summary>延後建立或重建 Handle 的控件也套用目前捲動條主題。</summary>
    private void HandleCreated(object? sender, EventArgs e)
    {
        if (sender is Control control)
            NativeScrollTheme.Apply(control, theme == UiTheme.Dark);
    }

    /// <summary>保留透明背景與啟用狀態的綠色語意，其他背景使用深灰配色。</summary>
    private static Color Background(Color light, Control control)
    {
        if (light == Color.Transparent)
            return light;
        if (light.G > light.R + 10 && light.G > light.B + 8)
            return Color.FromArgb(35, 76, 50);
        return control is TextBoxBase or ListBox or ComboBox or NumericUpDown or Button
            ? Color.FromArgb(47, 50, 54) : Color.FromArgb(38, 40, 43);
    }

    /// <summary>保留警告與成功文字的色彩語意，提升深色背景的可讀性。</summary>
    private static Color Foreground(Color light)
    {
        if (light.R > light.G * 1.3 && light.R > light.B * 1.3)
            return Color.FromArgb(218, 145, 145);
        if (light.G > light.R * 1.3 && light.G > light.B * 1.3)
            return Color.FromArgb(146, 190, 154);
        return Color.FromArgb(190, 195, 201);
    }

    /// <summary>記住功能端指定的新背景，再轉為目前主題，避免刷新時跳回淺色。</summary>
    private void BackgroundChanged(object? sender, EventArgs e)
    {
        if (applying || sender is not Control control
            || !appearances.TryGetValue(control, out var value))
            return;
        value.Background = control.BackColor;
        RefreshControl(control, value);
    }

    /// <summary>記住警告或狀態文字的新顏色，再套用目前主題。</summary>
    private void ForegroundChanged(object? sender, EventArgs e)
    {
        if (applying || sender is not Control control
            || !appearances.TryGetValue(control, out var value))
            return;
        value.Foreground = control.ForeColor;
        if (theme == UiTheme.Dark && control.Parent is { } parent
            && control.ForeColor == parent.ForeColor
            && appearances.TryGetValue(parent, out var parentAppearance))
            value.Foreground = parentAppearance.Foreground;
        RefreshControl(control, value);
    }

    /// <summary>以重入保護更新單一控件，避免色彩事件反覆觸發。</summary>
    private void RefreshControl(Control control, Appearance value)
    {
        applying = true;
        try
        {
            ApplyControl(control, value);
        }
        finally
        {
            applying = false;
        }
    }

    /// <summary>監控執行中新增的控件，不更換或重建既有控件。</summary>
    private void ControlAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control is not { } child)
            return;
        Attach(child);
        if (theme == UiTheme.Dark)
            Apply(theme);
    }

    /// <summary>在表格準備好編輯樣式後補上主題，不提交尚未完成的編輯。</summary>
    private void EditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
        => ApplyEditor(e.Control);

    /// <summary>深色下繪製下拉清單內容，保留項目文字、選取與原生下拉操作。</summary>
    private void DrawComboItem(object? sender, DrawItemEventArgs e)
    {
        if (theme != UiTheme.Dark || sender is not ComboBox combo)
            return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        var background = selected ? Color.FromArgb(58, 69, 83) : combo.BackColor;
        using var brush = new SolidBrush(background);
        e.Graphics.FillRectangle(brush, e.Bounds);
        string text = (e.Index >= 0 ? combo.GetItemText(combo.Items[e.Index]) : combo.Text) ?? "";
        var foreground = combo.Enabled ? combo.ForeColor : Color.FromArgb(150, 160, 175);
        TextRenderer.DrawText(e.Graphics, text, combo.Font, e.Bounds, foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        e.DrawFocusRectangle();
    }

    /// <summary>補繪深色停用按鈕文字，避免原生停用文字與背景對比不足。</summary>
    private void PaintDisabledButton(object? sender, PaintEventArgs e)
    {
        if (theme != UiTheme.Dark || sender is not Button { Enabled: false } button)
            return;
        var bounds = Rectangle.Inflate(button.ClientRectangle, -2, -2);
        using var brush = new SolidBrush(button.BackColor);
        e.Graphics.FillRectangle(brush, bounds);
        TextRenderer.DrawText(e.Graphics, button.Text, button.Font, bounds,
            Color.FromArgb(150, 160, 175), TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    /// <summary>以低對比灰線重繪設定區塊，避免原生立體邊框在深色背景泛白。</summary>
    private void PaintGroup(object? sender, PaintEventArgs e)
    {
        if (theme != UiTheme.Dark || sender is not GroupBox group)
            return;
        e.Graphics.Clear(group.BackColor);
        int inset = Math.Max(1, group.DeviceDpi / 96);
        int titleLeft = inset * 6;
        int titleHeight = group.Font.Height;
        var border = new Rectangle(inset, titleHeight / 2,
            Math.Max(0, group.Width - inset * 2 - 1),
            Math.Max(0, group.Height - titleHeight / 2 - inset - 1));
        using var pen = new Pen(Color.FromArgb(65, 70, 76));
        e.Graphics.DrawRectangle(pen, border);
        var title = new Rectangle(titleLeft, 0,
            TextRenderer.MeasureText(group.Text, group.Font).Width, titleHeight);
        using var brush = new SolidBrush(group.BackColor);
        e.Graphics.FillRectangle(brush, title);
        TextRenderer.DrawText(e.Graphics, group.Text, group.Font, title, group.ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    /// <summary>為鎖定區塊的勾選框補上柔和灰字與勾選標記，維持停用狀態可讀。</summary>
    private void PaintDisabledCheckBox(object? sender, PaintEventArgs e)
    {
        if (theme != UiTheme.Dark || sender is not CheckBox
            { Enabled: false, Appearance: System.Windows.Forms.Appearance.Normal,
                CheckAlign: ContentAlignment.MiddleLeft } check)
            return;
        e.Graphics.Clear(check.BackColor);
        int side = (int)Math.Round(13 * check.DeviceDpi / 96.0);
        var box = new Rectangle(1, Math.Max(0, (check.Height - side) / 2), side, side);
        using var border = new Pen(Color.FromArgb(94, 99, 106));
        e.Graphics.DrawRectangle(border, box);
        if (check.Checked)
        {
            using var tick = new Pen(Color.FromArgb(150, 160, 170), 2);
            e.Graphics.DrawLines(tick, [new(box.Left + side / 5, box.Top + side / 2),
                new(box.Left + side * 2 / 5, box.Bottom - side / 4),
                new(box.Right - side / 6, box.Top + side / 4)]);
        }
        var text = new Rectangle(box.Right + 4, 0,
            Math.Max(0, check.Width - box.Right - 4), check.Height);
        TextRenderer.DrawText(e.Graphics, check.Text, check.Font, text,
            Color.FromArgb(150, 160, 170),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    /// <summary>只設定正在編輯中的輸入控件色彩，保留內容與選取範圍。</summary>
    private void ApplyEditor(Control editor)
    {
        bool previous = applying;
        applying = true;
        try
        {
            editor.BackColor = theme == UiTheme.Dark
                ? Color.FromArgb(47, 50, 54) : SystemColors.Window;
            editor.ForeColor = theme == UiTheme.Dark
                ? Color.FromArgb(190, 195, 201) : SystemColors.WindowText;
        }
        finally
        {
            applying = previous;
        }
    }

    /// <summary>控件釋放時解除訂閱，避免保留已不使用的表格編輯控件。</summary>
    private void ControlDisposed(object? sender, EventArgs e)
    {
        if (sender is Control control)
            Detach(control);
    }

    /// <summary>解除指定控件的外觀與生命週期事件。</summary>
    private void Detach(Control control)
    {
        control.BackColorChanged -= BackgroundChanged;
        control.ForeColorChanged -= ForegroundChanged;
        control.ControlAdded -= ControlAdded;
        control.Disposed -= ControlDisposed;
        control.HandleCreated -= HandleCreated;
        control.Paint -= PaintDisabledButton;
        control.Paint -= PaintDisabledCheckBox;
        control.Paint -= PaintGroup;
        if (control is DataGridView grid)
            grid.EditingControlShowing -= EditingControlShowing;
        if (control is ComboBox combo)
            combo.DrawItem -= DrawComboItem;
        appearances.Remove(control);
    }

    /// <summary>主視窗關閉時解除全部外觀訂閱，不操作功能服務。</summary>
    public void Dispose()
    {
        foreach (var control in appearances.Keys.ToArray())
            Detach(control);
    }
}
