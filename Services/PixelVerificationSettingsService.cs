using System.Text.Json;
using System.Text.Json.Serialization;
using WOWCV.Models;

namespace WOWCV.Services;

/// <summary>Independent recognition settings; skill flow files are deliberately not
/// accepted.</summary>
public static class PixelVerificationSettingsService
{
    private sealed record SettingsFile(int Version, PixelVerificationOptions Verification);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>
    /// 將設定文件序列化為可保存的 JSON 文字。
    /// </summary>
    public static string Serialize(PixelVerificationOptions options)
    {
        options.Validate();
        return JsonSerializer.Serialize(new SettingsFile(1, options), JsonOptions);
    }

    /// <summary>
    /// 解析並驗證 JSON 設定，回傳可供後續操作使用的資料模型。
    /// </summary>
    public static PixelVerificationOptions Parse(string text)
    {
        var file = JsonSerializer.Deserialize<SettingsFile>(text, JsonOptions);
        if (file is null || file.Version != 1 || file.Verification is null)
            throw new JsonException("需要 version=1 與 verification 的獨立辨識設定檔。");
        try
        {
            file.Verification.Validate();
        }
        catch (ArgumentException ex)
        {
            throw new JsonException(ex.Message, ex);
        }

        return file.Verification;
    }
}
