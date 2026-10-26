using System.Globalization;

namespace WOWCV.Models;

public static class SkillValueLimits
{
    public const int MinimumPriority = 1, MaximumPriority = 10;
    public const int MinimumWeight = 1, MaximumWeight = 100;
    public const int MinimumCooldown = 0, MaximumCooldown = 600;

    /// <summary>Validate the original values before normalizing a legacy fractional
    /// cooldown.</summary>
    public static string? Normalize(SkillEntry skill)
    {
        string name = string.IsNullOrWhiteSpace(skill.SkillName) ? "未命名技能" : skill.SkillName;
        if (skill.Priority is < MinimumPriority or > MaximumPriority)
            throw new ArgumentException(
                $"{name}：priority 須為 {MinimumPriority}～{MaximumPriority} 的整數。");
        if (skill.SkillWeight is < MinimumWeight or > MaximumWeight)
            throw new ArgumentException(
                $"{name}：skillweight 須為 {MinimumWeight}～{MaximumWeight} 的整數。");
        if (skill.CdSec is < MinimumCooldown or > MaximumCooldown)
            throw new ArgumentException(
                $"{name}：cd_sec 須在 {MinimumCooldown}～{MaximumCooldown} 秒內。");
        decimal original = skill.CdSec;
        skill.CdSec = decimal.Round(original, 0, MidpointRounding.AwayFromZero);
        return original != skill.CdSec
            ? ($"{name}：cd_sec {original.ToString(CultureInfo.InvariantCulture)} → "
            + $"{skill.CdSec.ToString(CultureInfo.InvariantCulture)}") : null;
    }
}
