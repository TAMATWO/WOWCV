namespace WOWCV.Models;

public enum SourceKind { Screen, Image }

public sealed record SourceConfiguration
{
    public SourceKind Kind { get; init; }
    public string? ScreenDeviceName { get; init; }
    public string? ImagePath { get; init; }
    public string? TemplateFolderPath { get; init; }
}

public sealed record RoiConfiguration(int X, int Y, int Width, int Height, int SourceWidth, int SourceHeight)
{
    public bool Fits(Size size) => SourceWidth == size.Width && SourceHeight == size.Height
        && X >= 0 && Y >= 0 && Width > 0 && Height > 0
        && (long)X + Width <= size.Width && (long)Y + Height <= size.Height;
}

public sealed record SearchConfiguration
{
    public double Threshold { get; init; } = 0.8;
    public double ProcessScale { get; init; } = 1;
    public bool MultiScale { get; init; }
    public bool UseRoi { get; init; }
    public RoiConfiguration? Roi { get; init; }
}

/// <summary>References and immutable parameters only; no image bytes, flow contents, or execution state.</summary>
public sealed record WorkbenchConfiguration
{
    public int Version { get; init; } = 1;
    public SourceConfiguration Source { get; init; } = new();
    public SearchConfiguration Search { get; init; } = new();
    public PixelVerificationOptions Verification { get; init; } = new();
    public string? SkillFlowPath { get; init; }

    public void Validate()
    {
        if (Version != 1) throw new ArgumentException("不支援的配置版本，需要 version=1。");
        if (Source is null || Search is null || Verification is null) throw new ArgumentException("配置分類不可為 null。");
        if (!Enum.IsDefined(Source.Kind)) throw new ArgumentException("來源模式必須為 screen 或 image。");
        if (!double.IsFinite(Search.Threshold) || Search.Threshold is < 0 or > 1)
            throw new ArgumentException("門檻必須為 0～1。");
        if (!double.IsFinite(Search.ProcessScale) || Search.ProcessScale is < 0.1 or > 1)
            throw new ArgumentException("處理倍率必須為 0.1～1。");
        Verification.Validate();
    }
}
