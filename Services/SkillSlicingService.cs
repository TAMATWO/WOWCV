using OpenCvSharp;
using System.Globalization;

namespace WOWCV.Services;

public enum SlicingExportMode { NewFolder, ExistingFolder }

public static class SkillSlicingService
{
    private static readonly object exportDirectoryGate = new();
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
    public static string Export(Mat source, Rectangle roi, int rows, int columns, string parent, CancellationToken token,
        TimeProvider? clock = null, SlicingExportMode mode = SlicingExportMode.NewFolder)
    {
        if (!new Rectangle(0, 0, source.Width, source.Height).Contains(roi)) throw new ArgumentException("範圍超出來源圖片。");
        var cells = GetCells(roi, rows, columns);
        token.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(mode)) throw new ArgumentException("無效的切圖匯出方式。", nameof(mode));
        string timestamp = (clock ?? TimeProvider.System).GetLocalNow().ToString("yyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string directory;
        if (mode == SlicingExportMode.ExistingFolder)
        {
            if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("請選擇已存在的樣版資料夾。");
            directory = parent;
        }
        else lock (exportDirectoryGate)
        {
            string name = "Skills_" + timestamp;
            directory = Path.Combine(parent, name);
            int suffix = 0;
            while (Directory.Exists(directory) || File.Exists(directory))
                directory = Path.Combine(parent, name + "_" + (++suffix).ToString("D2", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(directory);
        }
        for (int index = 0; index < cells.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var rect = cells[index];
            using var cell = new Mat(source, new Rect(rect.X, rect.Y, rect.Width, rect.Height));
            if (!Cv2.ImEncode(".png", cell, out var encoded))
                throw new IOException("無法寫入技能圖片。");
            using var file = new FileStream(Path.Combine(directory, $"{Guid.NewGuid():N}.png"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(encoded);
        }
        return directory;
    }
}
