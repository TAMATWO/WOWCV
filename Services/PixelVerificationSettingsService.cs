using System.Text.Json;
using System.Text.Json.Serialization;
using WOWCV.Models;

namespace WOWCV.Services;

/// <summary>Independent recognition settings; skill flow files are deliberately not accepted.</summary>
public static class PixelVerificationSettingsService
{
    private sealed record SettingsFile(int Version, PixelVerificationOptions Verification);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string Serialize(PixelVerificationOptions options)
    {
        options.Validate();
        return JsonSerializer.Serialize(new SettingsFile(1, options), JsonOptions);
    }

    public static PixelVerificationOptions Parse(string text)
    {
        var file = JsonSerializer.Deserialize<SettingsFile>(text, JsonOptions);
        if (file is null || file.Version != 1 || file.Verification is null)
            throw new JsonException("需要 version=1 與 verification 的獨立辨識設定檔。");
        try { file.Verification.Validate(); }
        catch (ArgumentException ex) { throw new JsonException(ex.Message, ex); }
        return file.Verification;
    }
}
