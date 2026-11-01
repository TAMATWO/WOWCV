namespace WOWCV.Models;

public enum SourceKind
{
    Screen,
    Image
}

public enum TriggerMode
{
    Hold,
    Toggle
}

public sealed record CastingConfiguration
{
    public TriggerMode Mode { get; init; } = TriggerMode.Hold;
    public int MinimumIntervalMs { get; init; } = 150;
    public int KeyHoldMs { get; init; } = 30;

    /// <summary>
    /// 驗證設定值與允許範圍，不合法時回報錯誤以避免套用。
    /// </summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Mode))
            throw new ArgumentException("無效的技能觸發模式。");
        if (MinimumIntervalMs is < 0 or > 60000)
            throw new ArgumentException("施放間隔須為 0～60000 ms。");
        if (KeyHoldMs is < 1 or > 5000)
            throw new ArgumentException("按鍵持續時間須為 1～5000 ms。");
    }
}

public sealed record SourceConfiguration
{
    public SourceKind Kind
    {
        get; init;
    }
    public string? ScreenDeviceName
    {
        get; init;
    }
    public string? ImagePath
    {
        get; init;
    }
    public string? TemplateFolderPath
    {
        get; init;
    }
}

public sealed record RoiConfiguration(int X, int Y, int Width, int Height, int SourceWidth,
    int SourceHeight)
{
    /// <summary>
    /// 判斷已保存的 ROI 是否與指定來源尺寸相符且位於有效範圍內。
    /// </summary>
    public bool Fits(Size size) => SourceWidth == size.Width && SourceHeight == size.Height
        && X >= 0 && Y >= 0 && Width > 0 && Height > 0 && (long)X + Width <= size.Width
        && (long)Y + Height <= size.Height;
}

public sealed record SearchConfiguration
{
    public double Threshold { get; init; } = 0.8;
    public double ProcessScale { get; init; } = 1;
    public double MultiScaleMin { get; init; } = 0.8;
    public double MultiScaleMax { get; init; } = 1.2;
    public bool MultiScale
    {
        get; init;
    }
    public bool UseRoi
    {
        get; init;
    }
    public RoiConfiguration? Roi
    {
        get; init;
    }
}

/// <summary>References and immutable parameters only; no image bytes, flow contents, or execution
/// state.</summary>
public sealed record WorkbenchConfiguration
{
    public int Version { get; init; } = 1;
    public SourceConfiguration Source { get; init; } = new();
    public SearchConfiguration Search { get; init; } = new();
    public PixelVerificationOptions Verification { get; init; } = new();
    public string? SkillFlowPath
    {
        get; init;
    }
    public CastingConfiguration Casting { get; init; } = new();

    /// <summary>
    /// 驗證設定值與允許範圍，不合法時回報錯誤以避免套用。
    /// </summary>
    public void Validate()
    {
        if (Version != 1)
            throw new ArgumentException("不支援的配置版本，需要 version=1。");
        if (Source is null || Search is null || Verification is null || Casting is null)
            throw new ArgumentException("配置分類不可為 null。");
        if (!Enum.IsDefined(Source.Kind))
            throw new ArgumentException("來源模式必須為 screen 或 image。");
        if (!double.IsFinite(Search.Threshold) || Search.Threshold is < 0 or > 1)
            throw new ArgumentException("門檻必須為 0～1。");
        if (!double.IsFinite(Search.ProcessScale) || Search.ProcessScale is < 0.1 or > 1)
            throw new ArgumentException("處理倍率必須為 0.1～1。");
        WOWCV.Services.TemplateMatchingService.ValidateScaleRange(Search.MultiScaleMin, Search.MultiScaleMax);
        Verification.Validate();
        Casting.Validate();
    }
}
