using System.ComponentModel;
using WOWCV.Models;

namespace WOWCV.Controls;

/// <summary>保留原生分頁尺寸與切換操作，只覆繪深色分頁標籤和邊框。</summary>
internal sealed class ThemeTabControl : TabControl
{
    private UiTheme theme;
    [DefaultValue(UiTheme.Light)]
    public UiTheme Theme
    {
        get => theme;
        set
        {
            theme = value;
            bool dark = theme == UiTheme.Dark;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer, dark);
            Invalidate();
        }
    }

    /// <summary>深色時覆繪分頁外觀，保留原生標籤尺寸、切換與鍵盤操作。</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Theme == UiTheme.Dark && TabCount > 0)
            DrawDarkHeader(e.Graphics);
    }

    /// <summary>補繪原生分頁的螢幕與列印路徑，讓預覽產物亦有相同深色標籤。</summary>
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (Theme != UiTheme.Dark || !IsHandleCreated || TabCount == 0)
            return;
        if (m.Msg == 0x000F)
        {
            using var graphics = Graphics.FromHwnd(Handle);
            DrawDarkHeader(graphics);
        }
        else if (m.Msg == 0x0318 && m.WParam != IntPtr.Zero)
        {
            using var graphics = Graphics.FromHdc(m.WParam);
            DrawDarkHeader(graphics);
        }
    }

    /// <summary>繪製標籤、選取背景和焦點提示，保持原生點擊範圍。</summary>
    private void DrawDarkHeader(Graphics graphics)
    {
        var surface = Color.FromArgb(38, 40, 43);
        using var background = new SolidBrush(surface);
        int bottom = Enumerable.Range(0, TabCount).Max(i => GetTabRect(i).Bottom) + 2;
        graphics.FillRectangle(background, 0, 0, Width, bottom);
        // 原生分頁的立體邊框寬於一個像素，需完整覆蓋內容區外的留邊。
        var content = DisplayRectangle;
        graphics.FillRectangle(background, 0, bottom, content.Left,
            Math.Max(0, Height - bottom));
        graphics.FillRectangle(background, content.Right, bottom,
            Math.Max(0, Width - content.Right), Math.Max(0, Height - bottom));
        graphics.FillRectangle(background, content.Left, content.Bottom, content.Width,
            Math.Max(0, Height - content.Bottom));
        using var border = new Pen(Color.FromArgb(53, 57, 63));
        graphics.DrawRectangle(border, 0, bottom, Math.Max(0, Width - 1),
            Math.Max(0, Height - bottom - 1));
        for (int index = 0; index < TabCount; index++)
        {
            var rect = GetTabRect(index);
            using var fill = new SolidBrush(index == SelectedIndex
                ? Color.FromArgb(49, 54, 61) : surface);
            graphics.FillRectangle(fill, rect);
            TextRenderer.DrawText(graphics, TabPages[index].Text, Font, rect, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Focused && ShowFocusCues && index == SelectedIndex)
                ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(rect, -3, -3),
                    ForeColor, fill.Color);
        }
    }
}
