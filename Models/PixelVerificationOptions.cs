using System.Text.Json.Serialization;

namespace WOWCV.Models;

public readonly record struct SampleOffset(int X, int Y);

/// <summary>Immutable, global sampling configuration. Offsets are measured from each template's
/// center.</summary>
public sealed record PixelVerificationOptions
{
    public bool Enabled { get; init; } = true;
    public int RgbTolerance
    {
        get; init;
    }
    public int MinimumMatches { get; init; } = 5;
    public SampleOffset Center { get; init; } = new(0, 0);
    public SampleOffset Up { get; init; } = new(0, -10);
    public SampleOffset Down { get; init; } = new(0, 10);
    public SampleOffset Left { get; init; } = new(-10, 0);
    public SampleOffset Right { get; init; } = new(10, 0);

    [JsonIgnore]
    public IReadOnlyList<SampleOffset> Offsets => new[]
    {
        Center,
        Up,
        Down,
        Left,
        Right
    };

    /// <summary>
    /// 驗證設定值與允許範圍，不合法時回報錯誤以避免套用。
    /// </summary>
    public void Validate()
    {
        if (RgbTolerance is < 0 or > 255)
            throw new ArgumentException("RGB 容差必須為 0～255。");
        if (MinimumMatches is < 1 or > 5)
            throw new ArgumentException("最低通過點數必須為 1～5。");
    }
}
