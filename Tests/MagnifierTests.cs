using System.Reflection;
using OpenCvSharp.Extensions;
using WOWCV.Controls;
using WOWCV.Services;

internal static class MagnifierTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        string directory = Path.Combine(root, "magnifier"); Directory.CreateDirectory(directory);
        var image = new Bitmap(400, 200);
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                image.SetPixel(x, y, Color.FromArgb(x * 13 % 256, y * 17 % 256, (x + y) * 9 % 256));
        using var before = BitmapConverter.ToMat(image);
        using var preview = new RoiPictureBox { Dock = DockStyle.None, Size = new(600, 400), Image = image, SelectionEnabled = true };
        preview.CreateControl();
        Move(preview, new(300, 200));
        check(Layout(preview) is null, "Magnifier remains disabled by default on shared image previews");
        preview.MagnifierEnabled = true; Move(preview, new(300, 200));
        var layout = Layout(preview)!;
        check(Property<Point>(layout, "SourcePoint") == new Point(200, 100), "Magnifier uses native source coordinates through image zoom and letterboxing");
        check(preview.ClientRectangle.Contains(Property<Rectangle>(layout, "Bounds"))
            && !Property<Rectangle>(layout, "Bounds").Contains(new Point(300, 200)), "Magnifier stays inside preview bounds and offsets away from the pointer");
        using (var rendered = Render(preview))
        {
            check(Sample(rendered, layout, 4, 3, 4).ToArgb() == image.GetPixel(203, 104).ToArgb(),
                "Magnifier renders original pixels with exact nearest-neighbor colors");
            var content = Property<Rectangle>(layout, "Content");
            check(rendered.GetPixel(content.Left + content.Width / 2, content.Top + content.Height / 2).ToArgb() == Color.Cyan.ToArgb(),
                "Magnifier crosshair marks the same source boundary used by selection");
            rendered.Save(Path.Combine(directory, "hover-4x.png"));
        }
        preview.MagnifierZoom = 8;
        using (var rendered = Render(preview))
        {
            check(Sample(rendered, Layout(preview)!, 8, 3, 4).ToArgb() == image.GetPixel(203, 104).ToArgb(),
                "Changing magnifier zoom preserves the source point while increasing each native pixel's size");
        }
        Move(preview, new(330, 230));
        check(Property<Point>(Layout(preview)!, "SourcePoint") == new Point(220, 120), "Magnifier follows normal hover movement without requiring a mouse button");
        preview.MagnifierZoom = 4;
        Mouse(preview, "OnMouseDown", new(90, 95)); Mouse(preview, "OnMouseMove", new(210, 155));
        check(Property<Point>(Layout(preview)!, "SourcePoint") == new Point(140, 70) && preview.Selection is null,
            "Magnifier follows the active drag endpoint before the selection is committed");
        Mouse(preview, "OnMouseUp", new(210, 155));
        using var plain = new RoiPictureBox { Dock = DockStyle.None, Size = preview.Size, Image = (Bitmap)image.Clone(), SelectionEnabled = true };
        plain.CreateControl();
        Mouse(plain, "OnMouseDown", new(90, 95)); Mouse(plain, "OnMouseUp", new(210, 155));
        check(preview.Selection == new Rectangle(60, 30, 80, 40) && preview.Selection == plain.Selection,
            "Magnifier does not intercept drags or change the selected original-image ROI");
        Move(preview, new(0, 50)); layout = Layout(preview)!;
        using (var rendered = Render(preview))
        {
            var content = Property<Rectangle>(layout, "Content");
            check(Property<Point>(layout, "SourcePoint") == Point.Empty
                && rendered.GetPixel(content.Left + 2, content.Top + 2).ToArgb() == preview.BackColor.ToArgb(),
                "Image-edge magnification leaves out-of-image pixels blank without shifting the cursor coordinate");
        }
        Move(preview, new(599, 349));
        check(preview.ClientRectangle.Contains(Property<Rectangle>(Layout(preview)!, "Bounds")),
            "Bottom-right magnifier placement flips and remains fully visible");
        Move(preview, new(300, 20)); check(Layout(preview) is null, "Letterbox hover hides the magnifier");
        Move(preview, new(300, 200)); Raise(preview, "OnMouseLeave", EventArgs.Empty);
        check(Layout(preview) is null, "Leaving the preview clears the magnifier");
        Move(preview, new(300, 200)); preview.MagnifierEnabled = false;
        check(Layout(preview) is null && preview.Selection == plain.Selection, "Disabling magnification preserves the committed selection");
        preview.MagnifierEnabled = true; Move(preview, new(300, 200)); preview.ClearSelection();
        check(Layout(preview) is null, "Clearing selection for a new source also clears the previous hover image");
        preview.SelectionEnabled = false; preview.SetViewport(new Rectangle(100, 50, 100, 50)); Move(preview, new(300, 200));
        check(Property<Point>(Layout(preview)!, "SourcePoint") == new Point(150, 75), "Magnifier coordinate mapping also respects a focused source viewport");
        preview.SetViewport(null); preview.Size = new Size(1, 1); Move(preview, Point.Empty);
        check(Layout(preview) is null, "A tiny preview safely omits a magnifier that cannot fit");
        using var after = BitmapConverter.ToMat(image);
        check(OpenCvSharp.Cv2.Norm(before, after, OpenCvSharp.NormTypes.INF) == 0, "Hover, dragging and magnifier drawing never modify source image pixels");
        var roi = plain.Selection!.Value;
        string exported = SkillSlicingService.Export(after, roi, 1, 1, directory, CancellationToken.None);
        using var saved = OpenCvSharp.Cv2.ImRead(Directory.GetFiles(exported, "*.png").Single(), OpenCvSharp.ImreadModes.Unchanged);
        using var expected = new OpenCvSharp.Mat(before, new OpenCvSharp.Rect(roi.X, roi.Y, roi.Width, roi.Height));
        check(saved.Size() == expected.Size() && OpenCvSharp.Cv2.Norm(saved, expected, OpenCvSharp.NormTypes.INF) == 0,
            "PNG export contains the original selection without magnifier or crosshair overlays");
        TestSlicingUi(directory, image, check);
    }

    private static void TestSlicingUi(string directory, Bitmap source, Action<bool, string> check)
    {
        using var slicing = new SlicingControl { Dock = DockStyle.Fill };
        using var host = new Form { Size = new(1100, 740), ShowInTaskbar = false, Opacity = 0,
            StartPosition = FormStartPosition.Manual, Location = new(-30000, -30000) };
        host.Controls.Add(slicing); host.Show();
        var preview = Find<RoiPictureBox>(slicing).Single(); preview.Image = (Bitmap)source.Clone();
        var toggle = Find<CheckBox>(slicing).Single(control => control.Name == "SlicingMagnifier");
        var zoom = Find<NumericUpDown>(slicing).Single(control => control.Name == "SlicingMagnifierZoom");
        check(toggle.Checked && preview.MagnifierEnabled && zoom.Value == 4 && zoom.Minimum == 2 && zoom.Maximum == 12,
            "Slicing UI enables its magnifier at 4x with an adjustable 2x to 12x range");
        zoom.Value = 12; check(preview.MagnifierZoom == 12, "Slicing zoom input updates the live magnifier");
        toggle.Checked = false; check(!preview.MagnifierEnabled, "Slicing magnifier checkbox controls only the preview overlay");
        toggle.Checked = true; zoom.Value = 4;
        Move(preview, new(preview.Width / 2, preview.Height / 2));
        using var rendered = new Bitmap(host.Width, host.Height);
        host.DrawToBitmap(rendered, new Rectangle(Point.Empty, rendered.Size)); rendered.Save(Path.Combine(directory, "slicing-magnifier.png"));
        host.Close();
    }

    private static Color Sample(Bitmap bitmap, object layout, int zoom, int dx, int dy)
    {
        var content = Property<Rectangle>(layout, "Content");
        return bitmap.GetPixel(content.Left + content.Width / 2 + dx * zoom + zoom / 2,
            content.Top + content.Height / 2 + dy * zoom + zoom / 2);
    }
    private static object? Layout(RoiPictureBox preview) => typeof(RoiPictureBox)
        .GetMethod("GetMagnifierLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(preview, null);
    private static T Property<T>(object instance, string name) => (T)instance.GetType().GetProperty(name)!.GetValue(instance)!;
    private static Bitmap Render(RoiPictureBox preview)
    {
        var bitmap = new Bitmap(preview.Width, preview.Height);
        using var graphics = Graphics.FromImage(bitmap);
        using var args = new PaintEventArgs(graphics, new Rectangle(Point.Empty, preview.Size));
        Raise(preview, "OnPaint", args); return bitmap;
    }
    private static void Move(RoiPictureBox preview, Point point) => Mouse(preview, "OnMouseMove", point, MouseButtons.None);
    private static void Mouse(RoiPictureBox preview, string method, Point point, MouseButtons button = MouseButtons.Left) =>
        Raise(preview, method, new MouseEventArgs(button, 1, point.X, point.Y, 0));
    private static void Raise(RoiPictureBox preview, string method, EventArgs args) => typeof(RoiPictureBox)
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(preview, [args]);
    private static IEnumerable<T> Find<T>(Control parent) where T : Control => parent.Controls.Cast<Control>()
        .SelectMany(child => (child is T match ? new[] { match } : []).Concat(Find<T>(child)));
}
