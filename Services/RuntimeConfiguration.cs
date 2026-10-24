using WOWCV.Models;

namespace WOWCV.Services;

/// <summary>A privately owned snapshot. Consumers receive independent copies.</summary>
public sealed class RuntimeConfiguration
{
    private string? json;
    public bool HasValue => Volatile.Read(ref json) is not null;
    public SkillDocument? Read() => Volatile.Read(ref json) is { } value ? SkillDocumentService.Parse(value) : null;
    public void Import(string text)
    {
        var validated = SkillDocumentService.Parse(text);
        Interlocked.Exchange(ref json, SkillDocumentService.Serialize(validated));
    }
}
