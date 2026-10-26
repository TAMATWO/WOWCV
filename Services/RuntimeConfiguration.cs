using WOWCV.Models;

namespace WOWCV.Services;

/// <summary>A privately owned snapshot. Consumers receive independent copies.</summary>
public sealed class RuntimeConfiguration
{
    private sealed record State(string Json, SkillBindingSnapshot Bindings);
    private readonly object gate = new();
    private State? state;
    public bool HasValue => Volatile.Read(ref state) is not null;

    /// <summary>
    /// 回傳主頁流程的獨立文件副本，避免外部修改已發布設定。
    /// </summary>
    public SkillDocument? Read() => Volatile.Read(ref state) is { } value
        ? SkillDocumentService.Parse(value.Json) : null;
    public SkillBindingSnapshot Bindings => Volatile.Read(
        ref state)?.Bindings ?? SkillBindingSnapshot.Empty;

    /// <summary>
    /// 驗證並發布新的流程快照與圖片綁定，失敗時保留原設定。
    /// </summary>
    public void Import(string text) => Import(text, out _);

    /// <summary>
    /// 驗證並發布新的流程快照與圖片綁定，失敗時保留原設定。
    /// </summary>
    public void Import(string text, out IReadOnlyList<string> adjustments)
    {
        var validated = SkillDocumentService.Parse(text, out adjustments);
        string json = SkillDocumentService.Serialize(validated);
        lock (gate)
        {
            var bindings = SkillImageBindings.Resolve(validated,
                (state?.Bindings.Revision ?? 0) + 1);
            Volatile.Write(ref state, new State(json, bindings));
        }
    }
}
