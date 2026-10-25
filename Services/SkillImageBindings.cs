using WOWCV.Models;

namespace WOWCV.Services;

public sealed record SkillImageBinding(string SkillName, string TemplateFile, string CastKey, string? Issue = null);

public sealed record SkillBindingSnapshot(long Revision, IReadOnlyList<SkillImageBinding> Items)
{
    public static SkillBindingSnapshot Empty { get; } = new(0, Array.Empty<SkillImageBinding>());
}

public static class SkillImageBindings
{
    public static bool IsComplete(SkillEntry skill) => !string.IsNullOrWhiteSpace(skill.SkillName)
        && !string.IsNullOrWhiteSpace(skill.TemplateFile) && !string.IsNullOrWhiteSpace(skill.CastKey);

    public static SkillBindingSnapshot Resolve(SkillDocument document, long revision)
    {
        var entries = document.Data.SelectMany(group => group.Process).Select(skill => new SkillImageBinding(
            skill.SkillName.Trim(), skill.TemplateFile.Trim(), skill.CastKey.Trim())).ToList();
        // Normalize Windows filenames and key labels only for comparison. Preserve user spelling for display.
        static string Mapping(SkillImageBinding entry) => entry.TemplateFile.ToUpperInvariant() + "\0" + entry.CastKey.ToUpperInvariant();
        var nameConflicts = entries.Where(entry => entry.SkillName.Length > 0 && entry.TemplateFile.Length > 0 && entry.CastKey.Length > 0)
            .GroupBy(entry => entry.SkillName, StringComparer.Ordinal)
            .Where(group => group.Select(Mapping).Distinct().Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var templateConflicts = entries.Where(entry => entry.SkillName.Length > 0 && entry.TemplateFile.Length > 0 && entry.CastKey.Length > 0)
            .GroupBy(entry => entry.TemplateFile, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(entry => entry.SkillName).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resolved = new List<SkillImageBinding>();
        var seen = new HashSet<(string, string)>();
        foreach (var entry in entries)
        {
            if (!seen.Add((entry.SkillName, Mapping(entry)))) continue;
            string? issue = entry.SkillName.Length == 0 ? "未設定技能名稱"
                : entry.TemplateFile.Length == 0 ? "未綁定樣版圖片"
                : entry.CastKey.Length == 0 ? "未設定施放按鍵"
                : nameConflicts.Contains(entry.SkillName) ? "同一技能的樣版或按鍵綁定衝突"
                : templateConflicts.Contains(entry.TemplateFile) ? "同一樣版綁定了不同技能名稱" : null;
            resolved.Add(entry with { Issue = issue });
        }
        return new(revision, resolved.AsReadOnly());
    }
}
