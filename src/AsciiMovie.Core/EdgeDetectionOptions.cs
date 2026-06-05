namespace AsciiMovie.Core;

/// <summary>
/// エッジ検出の感度。Strength が大きいほどエッジが多く残る。
/// </summary>
public sealed class EdgeDetectionOptions
{
    public const double DefaultStrength = 1.0;

    /// <summary>0.05～3.0。既定 1.0。</summary>
    public double Strength { get; init; } = DefaultStrength;

    public double ClampedStrength => Math.Clamp(Strength, 0.05, 3.0);
}
