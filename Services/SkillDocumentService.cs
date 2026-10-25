using System.Text.Json;
using System.Text.Encodings.Web;
using WOWCV.Models;
namespace WOWCV.Services;
public static class SkillDocumentService
{
public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
public static string Serialize(SkillDocument document) => JsonSerializer.Serialize(document, Options);
    public static SkillDocument Parse(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object || !parsed.RootElement.TryGetProperty("data", out _)) throw new JsonException("根節點需要 data 陣列。");
        var data = parsed.RootElement.GetProperty("data");
        if (data.ValueKind != JsonValueKind.Array) throw new JsonException("data 必須是陣列。");
        foreach (var binding in data.EnumerateArray())
            if (binding.ValueKind != JsonValueKind.Object || !binding.TryGetProperty("press", out _) ||
                !binding.TryGetProperty("process", out var process) || process.ValueKind != JsonValueKind.Array)
                throw new JsonException("每組資料必須包含 press 與 process 陣列。");
        var result = JsonSerializer.Deserialize<SkillDocument>(json) ?? throw new JsonException("沒有設定內容。");
        if (result.Data is null) throw new JsonException("data 不可為 null。");
        foreach (var binding in result.Data)
        {
            if (binding is null || string.IsNullOrWhiteSpace(binding.Press) || binding.Process is null) throw new JsonException("每組資料需有非空白 press 與 process 陣列。");
            foreach (var skill in binding.Process)
            {
                if (skill is null || skill.SkillName is null || skill.CooldownCheck is null || skill.TemplateFile is null || skill.CastKey is null
                    || skill.Status is not ("Y" or "A") || skill.CdSec < 0)
                    throw new JsonException("技能文字不可為 null；status 必須為 Y 或 A；cd_sec 不可為負數。");
                if (skill.TemplateFile.Length > 0 && (skill.TemplateFile.IndexOfAny(['/', '\\', ':']) >= 0 || skill.TemplateFile is "." or ".."))
                    throw new JsonException("template_file 只保存樣版檔名，不可包含資料夾或完整路徑。");
            }
        }
        return result;
    }

}
