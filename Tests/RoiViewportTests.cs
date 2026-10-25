using System.Reflection;
using WOWCV.Controls;

internal static class RoiViewportTests
{
    public static void Run(Action<bool, string> check)
    {
        var original = new Bitmap(400, 200);
        using (var graphics = Graphics.FromImage(original))
        {
            graphics.Clear(Color.Red);
            graphics.FillRectangle(Brushes.CornflowerBlue, new Rectangle(100, 60, 80, 40));
        }
        using var preview = new RoiPictureBox { Image = original, Dock = DockStyle.None, Size = new Size(200, 200) };
        var roi = new Rectangle(100, 60, 80, 40);
        preview.SetViewport(roi);
        check(preview.DisplayedSourceBounds == roi, "Viewport retains the original-image ROI rectangle");
        using (var rendered = Render(preview))
        {
            check(rendered.GetPixel(100, 100).ToArgb() == Color.CornflowerBlue.ToArgb(), "Focused preview draws ROI rather than the full image");
            check(rendered.GetPixel(100, 25).ToArgb() == preview.BackColor.ToArgb()
                && rendered.GetPixel(100, 175).ToArgb() == preview.BackColor.ToArgb(), "ROI focus preserves aspect ratio and letterboxing");
        }
        preview.SetSampleMarkers([(new Point(120, 70), true, 1), (new Point(20, 20), false, 2)]);
        using (var rendered = Render(preview))
            check(rendered.GetPixel(51, 76).G > 240 && rendered.GetPixel(51, 76).R < 20,
                "Five-point marker positions account for ROI origin and zoom");
        preview.Size = new Size(400, 200);
        using (var rendered = Render(preview))
        {
            check(rendered.GetPixel(10, 190).ToArgb() == Color.CornflowerBlue.ToArgb(), "Resizing keeps the entire ROI fitted to the preview");
            check(rendered.GetPixel(103, 53).G > 240 && rendered.GetPixel(103, 53).R < 20,
                "Sample markers follow the viewport when resizing");
        }
        check(ReferenceEquals(preview.Image, original) && original.GetPixel(120, 70).ToArgb() == Color.CornflowerBlue.ToArgb(),
            "Viewport rendering and markers do not crop or modify original pixels");
        preview.SetSampleMarkers([]);
        preview.SetViewport(null);
        using (var rendered = Render(preview))
            check(rendered.GetPixel(10, 10).ToArgb() == Color.Red.ToArgb(), "Clearing viewport restores the complete source image");
        preview.SetViewport(new Rectangle(-20, 50, 40, 40));
        check(preview.DisplayedSourceBounds == new Rectangle(0, 50, 20, 40), "Viewport clips safely at image boundaries");
        preview.SetViewport(new Rectangle(1000, 1000, 20, 20));
        check(preview.DisplayedSourceBounds == new Rectangle(0, 0, 400, 200), "An out-of-image viewport falls back to full preview");

        preview.Size = new Size(200, 200);
        preview.SetViewport(roi); preview.SelectionEnabled = true;
        check(preview.DisplayedSourceBounds == new Rectangle(0, 0, 400, 200), "ROI selection temporarily displays the entire image");
        Mouse(preview, "OnMouseDown", new Point(60, 85));
        Mouse(preview, "OnMouseUp", new Point(100, 105));
        var originalSelection = new Rectangle(120, 70, 80, 40);
        check(preview.Selection == originalSelection, "Reselection maps mouse input to full-image coordinates");
        preview.SetViewport(preview.Selection); preview.SelectionEnabled = false;
        preview.SelectionEnabled = true;
        Mouse(preview, "OnMouseDown", new Point(20, 60)); Mouse(preview, "OnMouseMove", new Point(70, 85));
        check(preview.Selection == originalSelection, "Dragging does not replace the committed ROI before completion");
        preview.SelectionEnabled = false;
        check(preview.Selection == originalSelection && preview.DisplayedSourceBounds == originalSelection,
            "Canceling a drag restores the prior ROI focus without changing selection");
        preview.SelectionEnabled = true;
        Mouse(preview, "OnMouseDown", new Point(20, 60)); Mouse(preview, "OnMouseUp", new Point(70, 85));
        preview.SetViewport(preview.Selection); preview.SelectionEnabled = false;
        check(preview.Selection == new Rectangle(40, 20, 100, 50) && preview.DisplayedSourceBounds == preview.Selection,
            "Completing a new selection focuses the new original-image ROI");
        preview.ClearSelection(); preview.SetViewport(null);
        check(preview.Selection is null && preview.DisplayedSourceBounds == new Rectangle(0, 0, 400, 200),
            "Clearing selection can restore full preview");
    }

    public static void Select(RoiPictureBox preview, Rectangle sourceRectangle)
    {
        var source = preview.DisplayedSourceBounds;
        double scale = Math.Min((double)preview.ClientSize.Width / source.Width, (double)preview.ClientSize.Height / source.Height);
        int w = (int)Math.Round(source.Width * scale), h = (int)Math.Round(source.Height * scale);
        Point ClientPoint(int x, int y) => new((preview.ClientSize.Width - w) / 2 + (int)Math.Round((x - source.X) * scale),
            (preview.ClientSize.Height - h) / 2 + (int)Math.Round((y - source.Y) * scale));
        Mouse(preview, "OnMouseDown", ClientPoint(sourceRectangle.Left, sourceRectangle.Top));
        Mouse(preview, "OnMouseUp", ClientPoint(sourceRectangle.Right, sourceRectangle.Bottom));
    }

    private static Bitmap Render(RoiPictureBox preview)
    {
        var bitmap = new Bitmap(preview.Width, preview.Height);
        using var graphics = Graphics.FromImage(bitmap);
        using var args = new PaintEventArgs(graphics, new Rectangle(Point.Empty, preview.Size));
        typeof(RoiPictureBox).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(preview, [args]);
        return bitmap;
    }

    private static void Mouse(RoiPictureBox preview, string method, Point point) => typeof(RoiPictureBox)
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(preview, [new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)]);
}
