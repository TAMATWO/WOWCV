using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WOWCV.Models;

namespace WOWCV.Services;

/// <summary>One debounced writer. Flush and disposal wait for in-flight writes; snapshots are
/// immutable.</summary>
public sealed class WorkbenchConfigurationStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter<SourceKind>(JsonNamingPolicy.CamelCase,
        allowIntegerValues: false),
            new JsonStringEnumConverter<TriggerMode>(JsonNamingPolicy.CamelCase,
        allowIntegerValues: false)
        }
    };
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "config.json");
    public string FilePath
    {
        get;
    }
    public string? LastError => Volatile.Read(ref lastError);
    public bool HasSaved => Volatile.Read(ref hasSaved);

    private readonly object gate = new();
    private readonly System.Threading.Timer timer;
    private readonly int delayMs;
    private string? pending, saved, lastError;
    private bool disposed, hasSaved;

    /// <summary>
    /// 設定配置檔路徑並建立延後保存機制，不在建構時寫入檔案。
    /// </summary>
    public WorkbenchConfigurationStore(string filePath, int delayMs = 500)
    {
        FilePath = Path.GetFullPath(filePath);
        this.delayMs = delayMs;
        timer = new System.Threading.Timer(
            _ =>
            {
                lock (gate)
                {
                    if (!disposed)
                        SavePending();
                }
            },
            null,
            Timeout.Infinite,
            Timeout.Infinite
        );
    }

    /// <summary>
    /// 載入有效配置或備份，失敗時提供預設值及可顯示的警告。
    /// </summary>
    public WorkbenchConfiguration Load(out string? warning)
    {
        warning = null;
        try
        {
            // Read rather than File.Exists: access-denied must not look like a missing file.
            string text = File.ReadAllText(FilePath, Encoding.UTF8);
            var value = Parse(text);
            lock (gate)
                saved = Serialize(value);
            return value;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new();
        }
        catch (Exception ex) when (ex is JsonException or IOException
            or UnauthorizedAccessException)
        {
            warning = $"配置未載入，使用預設值：{ex.Message}";
            return new();
        }
    }

    /// <summary>
    /// 將設定文件序列化為可保存的 JSON 文字。
    /// </summary>
    public static string Serialize(WorkbenchConfiguration configuration)
    {
        configuration.Validate();
        return JsonSerializer.Serialize(configuration, JsonOptions);
    }

    /// <summary>
    /// 解析並驗證 JSON 設定，回傳可供後續操作使用的資料模型。
    /// </summary>
    public static WorkbenchConfiguration Parse(string text)
    {
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("version", out var version)
            || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number)
            || number != 1)
            throw new JsonException("配置需要 version=1。");
        var value = JsonSerializer.Deserialize<WorkbenchConfiguration>(text,
            JsonOptions) ?? throw new JsonException("配置不可為 null。");
        try
        {
            value.Validate();
        }
        catch (ArgumentException ex)
        {
            throw new JsonException(ex.Message, ex);
        }

        return value;
    }

    /// <summary>
    /// 以配置檔資料夾為基準解析相對路徑。
    /// </summary>
    public string ResolveReference(string path) => Path.GetFullPath(path,
        Path.GetDirectoryName(FilePath)!);

    /// <summary>
    /// 記住最新設定並重新排程延後保存，合併短時間內的多次修改。
    /// </summary>
    public void QueueSave(WorkbenchConfiguration configuration)
    {
        string text = Serialize(configuration);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            // Also cancel a pending edit when the user returns to the saved state.
            pending = text == saved ? null : text;
            if (pending is null)
                Volatile.Write(ref lastError, null);
            timer.Change(pending is null ? Timeout.Infinite : delayMs, Timeout.Infinite);
        }
    }

    /// <summary>
    /// 立即保存尚未寫入的設定，供正常關閉前完成最後修改。
    /// </summary>
    public bool Flush()
    {
        lock (gate)
            return SavePending();
    }

    /// <summary>
    /// 將待保存設定寫入暫存檔後替換正式配置，保留備份與錯誤狀態。
    /// </summary>
    private bool SavePending()
    {
        if (pending is null)
            return LastError is null;
        string temporary = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write,
                FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(pending);
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }

            if (File.Exists(FilePath))
                File.Replace(temporary, FilePath, FilePath + ".bak");
            else
                File.Move(temporary, FilePath);
            saved = pending;
            pending = null;
            Volatile.Write(ref lastError, null);
            Volatile.Write(ref hasSaved, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Volatile.Write(ref lastError, $"配置保存失敗：{ex.Message}");
            return false; // Keep the pending snapshot so closing or the next change can retry.
        }
    }

    /// <summary>
    /// 停止延後保存計時器，並完成尚未保存的配置寫入。
    /// </summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            timer.Change(Timeout.Infinite, Timeout.Infinite);
            timer.Dispose();
            SavePending();
        }
    }
}
