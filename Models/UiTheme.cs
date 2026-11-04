using System.Text.Json;
using System.Text.Json.Serialization;

namespace WOWCV.Models;

[JsonConverter(typeof(UiThemeJsonConverter))]
public enum UiTheme
{
    Light,
    Dark
}

/// <summary>容忍舊配置與未知主題值，避免外觀偏好影響其他配置還原。</summary>
public sealed class UiThemeJsonConverter : JsonConverter<UiTheme>
{
    /// <summary>辨識深色偏好，其餘值回退淺色且完整略過非純量內容。</summary>
    public override UiTheme Read(ref Utf8JsonReader reader, Type type,
        JsonSerializerOptions options)
    {
        bool dark = reader.TokenType == JsonTokenType.String
            && reader.GetString() == "dark";
        reader.Skip();
        return dark ? UiTheme.Dark : UiTheme.Light;
    }

    /// <summary>以穩定的小寫字串保存主題，維持配置版本相容。</summary>
    public override void Write(Utf8JsonWriter writer, UiTheme value,
        JsonSerializerOptions options) => writer.WriteStringValue(
            value == UiTheme.Dark ? "dark" : "light");
}
