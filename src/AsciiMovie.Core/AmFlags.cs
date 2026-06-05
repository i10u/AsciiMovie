namespace AsciiMovie.Core;

[Flags]
public enum AmFlags : ushort
{
    None = 0,
    Color = 1 << 0,
    Audio = 1 << 1,
    FramesDeflated = 1 << 2,
    DeltaEncoded = 1 << 3,
}
