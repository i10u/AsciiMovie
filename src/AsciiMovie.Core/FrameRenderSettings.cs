namespace AsciiMovie.Core;

/// <summary>
/// 1 フレームを RGB から .amov セル列へ変換するときの設定。
/// </summary>
public sealed class FrameRenderSettings
{
    public int Cols { get; set; } = 160;
    public int Rows { get; set; } = 90;
    public bool UseEdge { get; set; }
    public double EdgeStrength { get; set; } = EdgeDetectionOptions.DefaultStrength;
    public bool Color { get; set; } = true;
    public bool AllowAdaptiveInvert { get; set; } = true;
    public string Charset { get; set; } = AsciiMapper.DefaultCharset;

    public EdgeDetectionOptions ToEdgeOptions() => new() { Strength = EdgeStrength };

    public FrameRenderSettings Clone() => new()
    {
        Cols = Cols,
        Rows = Rows,
        UseEdge = UseEdge,
        EdgeStrength = EdgeStrength,
        Color = Color,
        AllowAdaptiveInvert = AllowAdaptiveInvert,
        Charset = Charset,
    };
}
