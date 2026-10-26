using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace WOWCV.Controls;

public sealed partial class RoiPictureBox
{
    private bool magnifierEnabled;
    private int magnifierZoom = 4;
    private Point? magnifierCursor;
    [DefaultValue(false)]
    public bool MagnifierEnabled
    {
        get => magnifierEnabled;
        set
        {
            if (magnifierEnabled == value)
                return;
            magnifierEnabled = value;
            if (!value)
                magnifierCursor = null;
            Invalidate();
        }
    }

    [DefaultValue(4)]
    public int MagnifierZoom
    {
        get => magnifierZoom;
        set
        {
            if (value is < 2 or > 12)
                throw new ArgumentOutOfRangeException(nameof(value), "放大倍率須為 2～12。");
            if (magnifierZoom == value)
                return;
            magnifierZoom = value;
            Invalidate();
        }
    }

    /// <summary>
    /// 清除放大鏡的游標位置與顯示狀態。
    /// </summary>
    private void ResetMagnifier()
    {
        magnifierCursor = null;
        Invalidate();
    }

    /// <summary>
    /// 依目前游標位置更新放大鏡取樣中心與可見狀態。
    /// </summary>
    private void UpdateMagnifier(Point location)
    {
        Point? next = MagnifierEnabled && Image is not null && ClientRectangle.Contains(location)
            && DisplayBounds().Contains(location) ? location : null;
        if (magnifierCursor == next)
            return;
        magnifierCursor = next;
        Invalidate();
    }

    /// <summary>
    /// 滑鼠離開控件時隱藏放大鏡。
    /// </summary>
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        ResetMagnifier();
    }

    /// <summary>
    /// 控件尺寸改變時更新放大鏡及圖片重繪。
    /// </summary>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ResetMagnifier();
    }

    private sealed record MagnifierLayout(Rectangle Bounds, Rectangle Content,
        Rectangle SourceRegion, Point SourcePoint);

    /// <summary>
    /// 計算放大鏡的來源取樣範圍與顯示位置，避免超出控件邊界。
    /// </summary>
    private MagnifierLayout? GetMagnifierLayout()
    {
        if (!MagnifierEnabled || Image is null || magnifierCursor is not { } cursor
            || !ClientRectangle.Contains(cursor) || !DisplayBounds().Contains(cursor))
            return null;
        int labelHeight = Math.Max(22, Font.Height + 6);
        int maxContent = Math.Min(180, Math.Min(ClientSize.Width - 18,
            ClientSize.Height - labelHeight - 18));
        int span = maxContent / MagnifierZoom / 2 * 2;
        if (span < 2)
            return null;
        int size = span * MagnifierZoom, width = size + 2, height = size + labelHeight + 2;
        int x = cursor.X + 20, y = cursor.Y + 20;
        if (x + width > ClientSize.Width)
            x = cursor.X - width - 20;
        if (y + height > ClientSize.Height)
            y = cursor.Y - height - 20;
        x = Math.Clamp(x, 0, ClientSize.Width - width);
        y = Math.Clamp(y, 0, ClientSize.Height - height);
        // Use the same source-coordinate rounding as the selection endpoints, including image-edge
        // boundaries.
        var point = ToImage(cursor);
        return new(
            new(x, y, width, height),
            new(x + 1, y + 1, size, size),
            new(point.X - span / 2, point.Y - span / 2, span, span),
            point
        );
    }

    /// <summary>
    /// 繪製原圖局部放大、十字線與原圖座標提示。
    /// </summary>
    private void DrawMagnifier(Graphics graphics)
    {
        if (GetMagnifierLayout() is not { } layout || Image is null)
            return;
        var state = graphics.Save();
        try
        {
            graphics.SetClip(layout.Bounds, CombineMode.Intersect);
            using var background = new SolidBrush(BackColor);
            graphics.FillRectangle(background, layout.Bounds);
            var clipped = Rectangle.Intersect(layout.SourceRegion, new Rectangle(Point.Empty,
                Image.Size));
            if (clipped.Width > 0 && clipped.Height > 0)
            {
                var destination = new Rectangle(
                    layout.Content.X + (clipped.X - layout.SourceRegion.X) * MagnifierZoom,
                    layout.Content.Y + (clipped.Y - layout.SourceRegion.Y) * MagnifierZoom,
                    clipped.Width * MagnifierZoom,
                    clipped.Height * MagnifierZoom
                );
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.DrawImage(Image, destination, clipped, GraphicsUnit.Pixel);
            }

            graphics.PixelOffsetMode = PixelOffsetMode.Default;
            graphics.SmoothingMode = SmoothingMode.None;
            int cx = layout.Content.Left + layout.Content.Width / 2,
                cy = layout.Content.Top + layout.Content.Height / 2;
            using var outline = new Pen(Color.Black, 3);
            using var crosshair = new Pen(Color.Cyan, 1);
            foreach (var pen in new[]
            {
                outline,
                crosshair
            }
            )
            {
                graphics.DrawLine(pen, cx - 10, cy, cx + 10, cy);
                graphics.DrawLine(pen, cx, cy - 10, cx, cy + 10);
            }

            graphics.DrawRectangle(
                Pens.Orange,
                layout.Bounds.X,
                layout.Bounds.Y,
                layout.Bounds.Width - 1,
                layout.Bounds.Height - 1
            );
            var label = Rectangle.FromLTRB(
                layout.Content.Left + 3,
                layout.Content.Bottom,
                layout.Content.Right - 2,
                layout.Bounds.Bottom - 1
            );
            TextRenderer.DrawText(
                graphics,
                $"{MagnifierZoom}×  X={layout.SourcePoint.X}  Y={layout.SourcePoint.Y}",
                Font,
                label,
                Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis
            );
        }
        finally
        {
            graphics.Restore(state);
        }
    }
}
