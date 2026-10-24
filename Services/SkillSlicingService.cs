using OpenCvSharp;

namespace WOWCV.Services;

public static class SkillSlicingService
{
    public static IReadOnlyList<Rectangle> GetCells(Rectangle roi, int rows, int columns)
    {
        if (rows < 1 || columns < 1 || roi.Width < columns || roi.Height < rows)
            throw new ArgumentException("選取範圍每格至少需要 1×1 像素。");
        var cells = new List<Rectangle>();
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
            {
                int x = roi.X + (int)Math.Round(col * (double)roi.Width / columns);
                int y = roi.Y + (int)Math.Round(row * (double)roi.Height / rows);
                int right = roi.X + (int)Math.Round((col + 1) * (double)roi.Width / columns);
                int bottom = roi.Y + (int)Math.Round((row + 1) * (double)roi.Height / rows);
                cells.Add(Rectangle.FromLTRB(x, y, right, bottom));
            }
        return cells;
    }
    public static string Export(Mat source, Rectangle roi, int rows, int columns, string parent, CancellationToken token)
    {
        if (!new Rectangle(0, 0, source.Width, source.Height).Contains(roi)) throw new ArgumentException("範圍超出來源圖片。");
        var cells = GetCells(roi, rows, columns);
        token.ThrowIfCancellationRequested();
        var directory = Path.Combine(parent, $"Skills_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        for (int index = 0; index < cells.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var rect = cells[index];
            using var cell = new Mat(source, new Rect(rect.X, rect.Y, rect.Width, rect.Height));
            if (!Cv2.ImWrite(Path.Combine(directory, $"Skill_R{index / columns + 1}_C{index % columns + 1}.png"), cell))
                throw new IOException("無法寫入技能圖片。");
        }
        return directory;
    }
}
