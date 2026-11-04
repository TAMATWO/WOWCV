using System.Drawing.Drawing2D;
using WOWCV.Models;

namespace WOWCV.Controls;

/// <summary>自行繪製太陽與月亮，切換外觀時不觸發編輯驗證。</summary>
internal sealed class ThemeToggleButton : Button
{
    private readonly ToolTip tips = new();
    private UiTheme theme;
    private bool mousePressed;

    /// <summary>建立可鍵盤操作、無外部素材的外觀切換按鈕。</summary>
    public ThemeToggleButton()
    {
        Name = "ThemeToggle";
        CausesValidation = false;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateTheme(UiTheme.Light);
    }

    /// <summary>同步目標主題的圖示、提示與無障礙名稱。</summary>
    public void UpdateTheme(UiTheme value)
    {
        theme = value;
        string hint = theme == UiTheme.Dark ? "切換淺色主題" : "切換深色主題";
        AccessibleName = hint;
        tips.SetToolTip(this, hint);
        Invalidate();
    }

    /// <summary>滑鼠點擊不搶走表格的編輯焦點，鍵盤仍可透過 Tab 操作。</summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            mousePressed = true;
            Capture = true;
        }
    }

    /// <summary>在按鈕內放開滑鼠時切換主題，不觸發輸入驗證。</summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        bool activate = mousePressed && e.Button == MouseButtons.Left
            && ClientRectangle.Contains(e.Location);
        mousePressed = false;
        Capture = false;
        if (activate)
            OnClick(EventArgs.Empty);
    }

    /// <summary>滑鼠擷取中斷時取消待觸發的點擊，避免意外切換。</summary>
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture)
            mousePressed = false;
    }

    /// <summary>依 DPI 繪製目標主題圖案與鍵盤焦點，不配置圖片資源。</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var state = e.Graphics.Save();
        float scale = Math.Min(Width, Height) / 24f;
        e.Graphics.TranslateTransform((Width - 24 * scale) / 2,
            (Height - 24 * scale) / 2);
        e.Graphics.ScaleTransform(scale, scale);
        using var brush = new SolidBrush(ForeColor);
        using var pen = new Pen(ForeColor, 1.5f);
        if (theme == UiTheme.Dark)
        {
            e.Graphics.DrawEllipse(pen, 8, 8, 8, 8);
            for (int index = 0; index < 8; index++)
            {
                double angle = index * Math.PI / 4;
                float x = (float)Math.Cos(angle), y = (float)Math.Sin(angle);
                e.Graphics.DrawLine(pen, 12 + x * 6, 12 + y * 6,
                    12 + x * 9, 12 + y * 9);
            }
        }
        else
        {
            e.Graphics.FillEllipse(brush, 5, 4, 15, 15);
            using var cutout = new SolidBrush(BackColor);
            e.Graphics.FillEllipse(cutout, 10, 2, 13, 13);
        }
        e.Graphics.Restore(state);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics,
                Rectangle.Inflate(ClientRectangle, -2, -2), ForeColor, BackColor);
    }

    /// <summary>釋放按鈕擁有的提示資源。</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            tips.Dispose();
        base.Dispose(disposing);
    }
}
