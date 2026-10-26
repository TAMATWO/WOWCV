using System.Text.Json;
using System.Text.Json.Serialization;

namespace WOWCV.Models;

public sealed class SkillDocument
{
    [JsonPropertyName("data")]
    public List<SkillBinding> Data { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra
    {
        get; set;
    }
}

public sealed class SkillBinding
{
    [JsonPropertyName("press")]
    public string Press { get; set; } = "E";

    [JsonPropertyName("process")]
    public List<SkillEntry> Process { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra
    {
        get; set;
    }
}

public sealed class SkillEntry
{
    [JsonPropertyName("skillname")]
    public string SkillName { get; set; } = "新技能";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Y";

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = SkillValueLimits.MinimumPriority;

    [JsonPropertyName("cd_sec")]
    public decimal CdSec
    {
        get; set;
    }

    [JsonPropertyName("skillweight")]
    public int SkillWeight { get; set; } = SkillValueLimits.MaximumWeight;

    [JsonPropertyName("cooldown_check")]
    public string CooldownCheck { get; set; } = "";

    [JsonPropertyName("template_file")]
    public string TemplateFile { get; set; } = "";

    [JsonPropertyName("cast_key")]
    public string CastKey { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra
    {
        get; set;
    }
}
