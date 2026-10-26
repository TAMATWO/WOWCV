using System.Drawing.Drawing2D;

namespace WOWCV.Controls;

public sealed partial class DetectionControl
{
    private readonly Dictionary<string, Bitmap?> templateThumbnails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Font resultTableFont = new("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Point);
    private (string? Folder, int Revision) thumbnailContext;

    private void InitializeTemplateResultDisplay(DataGridView table)
    {
        table.Font = resultTableFont;
        var column = new DataGridViewImageColumn
        {
            Name = "縮圖", HeaderText = "縮圖", ImageLayout = DataGridViewImageCellLayout.Normal,
            Width = 55, MinimumWidth = 50, AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            DefaultCellStyle = new DataGridViewCellStyle { NullValue = null, Padding = new Padding(1), Alignment = DataGridViewContentAlignment.MiddleCenter }
        };
        table.Columns.Insert(0, column);
        table.Columns["樣版"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        table.Columns["樣版"]!.Width = 135;
        table.RowTemplate.Height = Math.Max(22, table.Font.Height + 6);
        table.ShowCellToolTips = true;
    }

    private static string ShortTemplateName(string name)
    {
        string stem = Path.GetFileNameWithoutExtension(name);
        return stem.Length > 16 ? stem[..8] + "…" + Path.GetExtension(name) : name;
    }

    private void ResetTemplateThumbnailsIfChanged()
    {
        var context = (templateFolder, templateRevision);
        if (thumbnailContext == context) return;
        DisposeTemplateThumbnails(); thumbnailContext = context;
    }

    private Bitmap? GetTemplateThumbnail(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        ResetTemplateThumbnailsIfChanged();
        if (templateThumbnails.TryGetValue(name, out var cached)) return cached;
        Bitmap? result = null;
        try
        {
            if (templateFolder is not null)
            {
                using var source = Image.FromFile(Path.Combine(templateFolder, name));
                int size = resultTableFont.Height;
                using var thumbnail = new Bitmap(size, size);
                using (var graphics = Graphics.FromImage(thumbnail))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    double scale = Math.Min((double)size / source.Width, (double)size / source.Height);
                    int width = Math.Max(1, (int)Math.Round(source.Width * scale));
                    int height = Math.Max(1, (int)Math.Round(source.Height * scale));
                    graphics.DrawImage(source, (size - width) / 2, (size - height) / 2, width, height);
                }
                result = new Bitmap(thumbnail);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        { /* A missing or unreadable preview must not interrupt recognition results. */ }
        templateThumbnails[name] = result;
        return result;
    }

    private void DisposeTemplateThumbnails()
    {
        foreach (var thumbnail in templateThumbnails.Values) thumbnail?.Dispose();
        templateThumbnails.Clear();
    }
}
