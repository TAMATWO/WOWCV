using System.ComponentModel;

namespace WOWCV.Controls;

/// <summary>Selections use source pixels, independent of zoom and letterboxing.</summary>
public sealed class RoiPictureBox : PictureBox
{
    public Rectangle? Selection { get; private set; }
    [DefaultValue(false)] public bool SelectionEnabled { get; set; }
    [DefaultValue(1)] public int GridRows { get; set; } = 1;
    [DefaultValue(1)] public int GridColumns { get; set; } = 1;
    public event EventHandler? SelectionChanged;
    private Point? anchor;
    private (Point Point, bool Matches, int Number)[] sampleMarkers = [];

    public void SetSampleMarkers(IEnumerable<(Point Point, bool Matches, int Number)> markers)
    {
        sampleMarkers = markers.ToArray(); Invalidate();
    }

    public RoiPictureBox()
    {
        Dock = DockStyle.Fill; SizeMode = PictureBoxSizeMode.Zoom;
        BackColor = Color.FromArgb(25, 28, 34);
    }
    public void ClearSelection()
    {
        Selection = null; anchor = null; Capture = false;
        Invalidate(); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    private Rectangle DisplayBounds()
    {
        if (Image is null || ClientSize.Width < 1 || ClientSize.Height < 1) return Rectangle.Empty;
        double scale = Math.Min((double)ClientSize.Width / Image.Width, (double)ClientSize.Height / Image.Height);
        int w = Math.Max(1, (int)Math.Round(Image.Width * scale)), h = Math.Max(1, (int)Math.Round(Image.Height * scale));
        return new((ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h);
    }
    private Point ToImage(Point value)
    {
        var bounds = DisplayBounds();
        return new(Math.Clamp((int)Math.Round((value.X - bounds.X) * (double)Image!.Width / bounds.Width), 0, Image.Width),
            Math.Clamp((int)Math.Round((value.Y - bounds.Y) * (double)Image.Height / bounds.Height), 0, Image.Height));
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (SelectionEnabled && e.Button == MouseButtons.Left && Image is not null && DisplayBounds().Contains(e.Location))
        { anchor = ToImage(e.Location); Capture = true; }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (anchor is not { } start || Image is null) return;
        var end = ToImage(e.Location);
        Selection = Rectangle.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));
        Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (anchor is null || e.Button != MouseButtons.Left) return;
        OnMouseMove(e); anchor = null; Capture = false;
        if (Selection is not { Width: > 0, Height: > 0 }) Selection = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    protected override void OnPaint(PaintEventArgs pe)
    {
        base.OnPaint(pe);
        if (Image is null) return;
        var bounds = DisplayBounds();
        float sx = (float)bounds.Width / Image.Width, sy = (float)bounds.Height / Image.Height;
        if (Selection is { Width: > 0, Height: > 0 } roi)
        {
            var rect = new RectangleF(bounds.X + roi.X * sx, bounds.Y + roi.Y * sy, roi.Width * sx, roi.Height * sy);
            using var pen = new Pen(Color.Orange, 2);
            pe.Graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            for (int col = 1; col < GridColumns; col++)
            {
                float x = rect.X + rect.Width * col / GridColumns;
                pe.Graphics.DrawLine(pen, x, rect.Top, x, rect.Bottom);
            }
            for (int row = 1; row < GridRows; row++)
            {
                float y = rect.Y + rect.Height * row / GridRows;
                pe.Graphics.DrawLine(pen, rect.Left, y, rect.Right, y);
            }
        }
        foreach (var marker in sampleMarkers)
        {
            if (marker.Point.X < 0 || marker.Point.Y < 0 || marker.Point.X >= Image.Width || marker.Point.Y >= Image.Height) continue;
            float x = bounds.X + (marker.Point.X + 0.5f) * sx, y = bounds.Y + (marker.Point.Y + 0.5f) * sy;
            using var brush = new SolidBrush(marker.Matches ? Color.Lime : Color.Red);
            pe.Graphics.DrawEllipse(Pens.Black, x - 5, y - 5, 10, 10);
            pe.Graphics.FillEllipse(brush, x - 3, y - 3, 6, 6);
            pe.Graphics.DrawString(marker.Number.ToString(), Font, brush, x + 5, y - 10);
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { var previous = Image; Image = null; previous?.Dispose(); }
        base.Dispose(disposing);
    }
}
