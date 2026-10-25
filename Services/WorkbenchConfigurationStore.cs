using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WOWCV.Models;

namespace WOWCV.Services;

/// <summary>One debounced writer. Flush and disposal wait for in-flight writes; snapshots are immutable.</summary>
public sealed class WorkbenchConfigurationStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<SourceKind>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new JsonStringEnumConverter<TriggerMode>(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "config.json");
    public string FilePath { get; }
    public string? LastError => Volatile.Read(ref lastError);
    public bool HasSaved => Volatile.Read(ref hasSaved);
    private readonly object gate = new();
    private readonly System.Threading.Timer timer;
    private readonly int delayMs;
    private string? pending, saved, lastError;
    private bool disposed, hasSaved;

    public WorkbenchConfigurationStore(string filePath, int delayMs = 500)
    {
        FilePath = Path.GetFullPath(filePath);
        this.delayMs = delayMs;
        timer = new System.Threading.Timer(_ => { lock (gate) { if (!disposed) SavePending(); } }, null, Timeout.Infinite, Timeout.Infinite);
    }

    public WorkbenchConfiguration Load(out string? warning)
    {
        warning = null;
        try
        {
            // Read rather than File.Exists: access-denied must not look like a missing file.
            string text = File.ReadAllText(FilePath, Encoding.UTF8);
            var value = Parse(text);
            lock (gate) saved = Serialize(value);
            return value;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return new(); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            warning = $"配置未載入，使用預設值：{ex.Message}";
            return new();
        }
    }

    public static string Serialize(WorkbenchConfiguration configuration)
    {
        configuration.Validate();
        return JsonSerializer.Serialize(configuration, JsonOptions);
    }

    public static WorkbenchConfiguration Parse(string text)
    {
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out int number) || number != 1)
            throw new JsonException("配置需要 version=1。");
        var value = JsonSerializer.Deserialize<WorkbenchConfiguration>(text, JsonOptions) ?? throw new JsonException("配置不可為 null。");
        try { value.Validate(); }
        catch (ArgumentException ex) { throw new JsonException(ex.Message, ex); }
        return value;
    }

    public string ResolveReference(string path) => Path.GetFullPath(path, Path.GetDirectoryName(FilePath)!);

    public void QueueSave(WorkbenchConfiguration configuration)
    {
        string text = Serialize(configuration);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            // Also cancel a pending edit when the user returns to the saved state.
            pending = text == saved ? null : text;
            if (pending is null) Volatile.Write(ref lastError, null);
            timer.Change(pending is null ? Timeout.Infinite : delayMs, Timeout.Infinite);
        }
    }

    public bool Flush()
    {
        lock (gate) return SavePending();
    }

    private bool SavePending()
    {
        if (pending is null) return LastError is null;
        string temporary = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(pending);
                file.Write(bytes); file.Flush(flushToDisk: true);
            }
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak");
            else File.Move(temporary, FilePath);
            saved = pending; pending = null; Volatile.Write(ref lastError, null); Volatile.Write(ref hasSaved, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Volatile.Write(ref lastError, $"配置保存失敗：{ex.Message}");
            return false; // Keep the pending snapshot so closing or the next change can retry.
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; timer.Change(Timeout.Infinite, Timeout.Infinite); timer.Dispose();
            SavePending();
        }
    }
}
