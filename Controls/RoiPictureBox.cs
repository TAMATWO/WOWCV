using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace WOWCV.Controls;

/// <summary>Selections use source pixels, independent of zoom and letterboxing.</summary>
public sealed partial class RoiPictureBox : PictureBox
{
    public Rectangle? Selection
    {
        get; private set;
    }

    private bool selectionEnabled;
    [DefaultValue(false)]
    public bool SelectionEnabled
    {
        get => selectionEnabled;
        set
        {
            if (selectionEnabled == value)
                return;
            selectionEnabled = value;
            if (!value)
            {
                anchor = null;
                dragSelection = null;
                Capture = false;
            }

            Invalidate();
        }
    }

    [DefaultValue(1)]
    public int GridRows { get; set; } = 1;

    [DefaultValue(1)]
    public int GridColumns { get; set; } = 1;

    public event EventHandler? SelectionChanged;
    private Point? anchor;
    private Rectangle? dragSelection, viewport;
    private (Point Point, bool Matches, int Number)[] sampleMarkers = [];
    private Rectangle? trackedBounds;
    private bool trackedMatches;

    /// <summary>
    /// 更新選取技能的記憶框與命中顏色，內容未變時略過重繪。
    /// </summary>
    public void SetTrackedBounds(Rectangle? region, bool matches)
    {
        if (trackedBounds == region && trackedMatches == matches)
            return;
        trackedBounds = region;
        trackedMatches = matches;
        Invalidate();
    }

    /// <summary>The original-image rectangle currently displayed. Selection editing temporarily
    /// shows the full image.</summary>
    public Rectangle DisplayedSourceBounds
    {
        get
        {
            if (Image is null)
                return Rectangle.Empty;
            var full = new Rectangle(Point.Empty, Image.Size);
            if (SelectionEnabled || viewport is null)
                return full;
            var clipped = Rectangle.Intersect(full, viewport.Value);
            return clipped.Width > 0 && clipped.Height > 0 ? clipped : full;
        }
    }

    /// <summary>
    /// 設定顯示用的來源範圍，不改變原始圖片與取樣座標。
    /// </summary>
    public void SetViewport(Rectangle? sourceRegion)
    {
        viewport = sourceRegion is { Width: > 0, Height: > 0 } ? sourceRegion : null;
        Invalidate();
    }

    /// <summary>
    /// 更新原圖五點標記，標記相同時不重複要求重繪。
    /// </summary>
    public void SetSampleMarkers(IEnumerable<(Point Point, bool Matches, int Number)> markers)
    {
        var next = markers.ToArray();
        if (sampleMarkers.SequenceEqual(next))
            return;
        sampleMarkers = next;
        Invalidate();
    }

    /// <summary>
    /// 初始化支援 ROI 選取、等比例顯示與雙緩衝的圖片控件。
    /// </summary>
    public RoiPictureBox()
    {
        Dock = DockStyle.Fill;
        SizeMode = PictureBoxSizeMode.Zoom;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(25, 28, 34);
    }

    /// <summary>
    /// 清除已選 ROI、拖曳狀態與放大鏡，並通知選取範圍改變。
    /// </summary>
    public void ClearSelection()
    {
        Selection = null;
        anchor = null;
        dragSelection = null;
        Capture = false;
        ResetMagnifier();
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Restore a validated selection in original-image coordinates without simulating a
    /// mouse drag.</summary>
    public void RestoreSelection(Rectangle? selection)
    {
        if (selection is { } value && (value.X < 0 || value.Y < 0 || value.Width <= 0
            || value.Height <= 0))
            throw new ArgumentException("選取範圍必須是有效的原圖矩形。", nameof(selection));
        anchor = null;
        dragSelection = null;
        Capture = false;
        Selection = selection;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 計算目前來源範圍在控件中的等比例顯示矩形。
    /// </summary>
    private Rectangle DisplayBounds()
    {
        var source = DisplayedSourceBounds;
        if (source.IsEmpty || ClientSize.Width < 1 || ClientSize.Height < 1)
            return Rectangle.Empty;
        double scale = Math.Min((double)ClientSize.Width / source.Width,
            (double)ClientSize.Height / source.Height);
        int w = Math.Max(1, (int)Math.Round(source.Width * scale)), h = Math.Max(1,
            (int)Math.Round(source.Height * scale));
        return new((ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h);
    }

    /// <summary>
    /// 將控件中的滑鼠位置換算為原始圖片座標。
    /// </summary>
    private Point ToImage(Point value)
    {
        var bounds = DisplayBounds();
        var source = DisplayedSourceBounds;
        return new(
            source.X + Math.Clamp(
                (int)Math.Round((value.X - bounds.X) * (double)source.Width / bounds.Width),
                0,
                source.Width
            ),
            source.Y + Math.Clamp(
                (int)Math.Round((value.Y - bounds.Y) * (double)source.Height / bounds.Height),
                0,
                source.Height
            )
        );
    }

    /// <summary>
    /// 處理圖片區域內的 ROI 拖曳起點，忽略圖片外留白。
    /// </summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        UpdateMagnifier(e.Location);
        if (SelectionEnabled && e.Button == MouseButtons.Left && Image is not null
            && DisplayBounds().Contains(e.Location))
        {
            anchor = ToImage(e.Location);
            dragSelection = null;
            Capture = true;
        }
    }

    /// <summary>
    /// 更新拖曳範圍與放大鏡使用的原圖滑鼠座標。
    /// </summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateMagnifier(e.Location);
        if (anchor is not { } start || Image is null)
            return;
        var end = ToImage(e.Location);
        dragSelection = Rectangle.FromLTRB(
            Math.Min(start.X, end.X),
            Math.Min(start.Y, end.Y),
            Math.Max(start.X, end.X),
            Math.Max(start.Y, end.Y)
        );
        Invalidate();
    }

    /// <summary>
    /// 結束拖曳並提交有效的原圖選取範圍。
    /// </summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (anchor is null || e.Button != MouseButtons.Left)
            return;
        OnMouseMove(e);
        var completed = dragSelection;
        anchor = null;
        dragSelection = null;
        Capture = false;
        if (completed is { Width: > 0, Height: > 0 })
        {
            Selection = completed;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        Invalidate();
    }

    /// <summary>
    /// 滑鼠擷取中斷時清除未完成的拖曳狀態。
    /// </summary>
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture)
        {
            anchor = null;
            dragSelection = null;
            Invalidate();
        }
    }

    /// <summary>
    /// 繪製來源圖片、ROI、技能記憶框、取樣標記與放大鏡。
    /// </summary>
    protected override void OnPaint(PaintEventArgs pe)
    {
        var source = DisplayedSourceBounds;
        var bounds = DisplayBounds();
        if (Image is null || bounds.IsEmpty)
        {
            base.OnPaint(pe);
            return;
        }

        if (source == new Rectangle(Point.Empty, Image.Size))
            base.OnPaint(pe);
        else
        {
            // Draw only the selected source rectangle. Image remains the full-resolution original
            // preview.
            pe.Graphics.Clear(BackColor);
            var drawingState = pe.Graphics.Save();
            pe.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            pe.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            pe.Graphics.DrawImage(Image, bounds, source, GraphicsUnit.Pixel);
            pe.Graphics.Restore(drawingState);
        }

        float sx = (float)bounds.Width / source.Width, sy = (float)bounds.Height / source.Height;
        var overlayState = pe.Graphics.Save();
        pe.Graphics.SetClip(bounds, CombineMode.Intersect);
        if (trackedBounds is { } tracked)
        {
            using var trackedPen = new Pen(trackedMatches ? Color.LimeGreen : Color.Cyan, 2)
            {
                DashStyle = DashStyle.Dash
            };
            pe.Graphics.DrawRectangle(
                trackedPen,
                bounds.X + (tracked.X - source.X) * sx,
                bounds.Y + (tracked.Y - source.Y) * sy,
                tracked.Width * sx,
                tracked.Height * sy
            );
        }

        if ((dragSelection ?? Selection) is { Width: > 0, Height: > 0 } roi)
        {
            var rect = new RectangleF(
                bounds.X + (roi.X - source.X) * sx,
                bounds.Y + (roi.Y - source.Y) * sy,
                roi.Width * sx,
                roi.Height * sy
            );
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
            if (!source.Contains(marker.Point))
                continue;
            float x = bounds.X + (marker.Point.X - source.X + 0.5f) * sx,
                y = bounds.Y + (marker.Point.Y - source.Y + 0.5f) * sy;
            using var brush = new SolidBrush(marker.Matches ? Color.Lime : Color.Red);
            pe.Graphics.DrawEllipse(Pens.Black, x - 5, y - 5, 10, 10);
            pe.Graphics.FillEllipse(brush, x - 3, y - 3, 6, 6);
            pe.Graphics.DrawString(marker.Number.ToString(), Font, brush, x + 5, y - 10);
        }

        pe.Graphics.Restore(overlayState);
        DrawMagnifier(pe.Graphics);
    }

    /// <summary>
    /// 先解除並釋放目前持有的圖片，再釋放控件資源。
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var previous = Image;
            Image = null;
            previous?.Dispose();
        }

        base.Dispose(disposing);
    }
}
