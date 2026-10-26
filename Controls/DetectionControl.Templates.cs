using System.Drawing.Drawing2D;

namespace WOWCV.Controls;

public sealed partial class DetectionControl
{
    private readonly Dictionary<string,
        Bitmap?> templateThumbnails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Font resultTableFont = new("Segoe UI", 12f, FontStyle.Regular,
        GraphicsUnit.Point);
    private (string? Folder, int Revision) thumbnailContext;

    /// <summary>
    /// 設定結果表格字體、縮圖欄及樣版檔名的顯示方式。
    /// </summary>
    private void InitializeTemplateResultDisplay(DataGridView table)
    {
        table.Font = resultTableFont;
        var column = new DataGridViewImageColumn
        {
            Name = "縮圖",
            HeaderText = "縮圖",
            ImageLayout = DataGridViewImageCellLayout.Normal,
            Width = 55,
            MinimumWidth = 50,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                NullValue = null,
                Padding = new Padding(1),
                Alignment = DataGridViewContentAlignment.MiddleCenter
            }
        };
        table.Columns.Insert(0, column);
        table.Columns["樣版"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        table.Columns["樣版"]!.Width = 135;
        table.RowTemplate.Height = Math.Max(22, table.Font.Height + 6);
        table.ShowCellToolTips = true;
    }

    /// <summary>
    /// 縮短過長樣版名稱，讓結果表格保留可掃讀的檔名摘要。
    /// </summary>
    private static string ShortTemplateName(string name)
    {
        string stem = Path.GetFileNameWithoutExtension(name);
        return stem.Length > 16 ? stem[..8] + "…" + Path.GetExtension(name) : name;
    }

    /// <summary>
    /// 樣版來源或版本改變時釋放舊縮圖快取。
    /// </summary>
    private void ResetTemplateThumbnailsIfChanged()
    {
        var context = (templateFolder, templateRevision);
        if (thumbnailContext == context)
            return;
        DisposeTemplateThumbnails();
        thumbnailContext = context;
    }

    /// <summary>
    /// 取得或建立樣版縮圖，讀取後立即釋放來源圖片檔案。
    /// </summary>
    private Bitmap? GetTemplateThumbnail(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        ResetTemplateThumbnailsIfChanged();
        if (templateThumbnails.TryGetValue(name, out var cached))
            return cached;
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
                    double scale = Math.Min((double)size / source.Width,
                        (double)size / source.Height);
                    int width = Math.Max(1, (int)Math.Round(source.Width * scale));
                    int height = Math.Max(1, (int)Math.Round(source.Height * scale));
                    graphics.DrawImage(source, (size - width) / 2, (size - height) / 2, width,
                        height);
                }

                result = new Bitmap(thumbnail);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException
            or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        { /* A missing or unreadable preview must not interrupt recognition results. */
        }

        templateThumbnails[name] = result;
        return result;
    }

    /// <summary>
    /// 釋放縮圖快取持有的圖片資源並清空快取。
    /// </summary>
    private void DisposeTemplateThumbnails()
    {
        foreach (var thumbnail in templateThumbnails.Values)
            thumbnail?.Dispose();
        templateThumbnails.Clear();
    }
}
