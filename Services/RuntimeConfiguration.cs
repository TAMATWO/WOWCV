using WOWCV.Models;

namespace WOWCV.Services;

/// <summary>A privately owned snapshot. Consumers receive independent copies.</summary>
public sealed class RuntimeConfiguration
{
    private sealed record State(string Json, SkillBindingSnapshot Bindings);
    private readonly object gate = new();
    private State? state;
    public bool HasValue => Volatile.Read(ref state) is not null;
    public SkillDocument? Read() => Volatile.Read(ref state) is { } value ? SkillDocumentService.Parse(value.Json) : null;
    public SkillBindingSnapshot Bindings => Volatile.Read(ref state)?.Bindings ?? SkillBindingSnapshot.Empty;
    public void Import(string text)
    {
        var validated = SkillDocumentService.Parse(text);
        string json = SkillDocumentService.Serialize(validated);
        lock (gate)
        {
            var bindings = SkillImageBindings.Resolve(validated, (state?.Bindings.Revision ?? 0) + 1);
            Volatile.Write(ref state, new State(json, bindings));
        }
    }
}
