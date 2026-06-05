namespace AsciiMovie.Core;

/// <summary>
/// エッジ部分だけ通常の ASCII マッピングを適用し、それ以外は空白にする。
/// </summary>
public static class EdgeAsciiMapper
{
    public static byte[] MapFrame(
        ReadOnlySpan<byte> rgb24,
        int cols,
        int rows,
        string charset,
        bool color,
        bool allowAdaptiveInvert = true,
        EdgeDetectionOptions? edgeOptions = null,
        ushort version = AmFrameLayout.Version2)
    {
        if (rgb24.Length != cols * rows * 3)
            throw new ArgumentException("RGB buffer size does not match cols×rows.", nameof(rgb24));

        if (string.IsNullOrEmpty(charset))
            throw new ArgumentException("Charset must not be empty.", nameof(charset));

        var cellCount = cols * rows;
        Span<bool> mask = cellCount <= 4096 ? stackalloc bool[cellCount] : new bool[cellCount];
        EdgeDetector.BuildEdgeMask(rgb24, cols, rows, mask, out _, edgeOptions);

        var mapped = AsciiMapper.MapFrame(
            rgb24, cols, rows, charset, color, version, allowAdaptiveInvert);

        var header = new AmHeader
        {
            Version = version,
            Cols = (ushort)cols,
            Rows = (ushort)rows,
            Flags = color ? AmFlags.Color : AmFlags.None,
            Charset = charset,
        };

        for (var i = 0; i < cellCount; i++)
        {
            if (mask[i])
            {
                if (color)
                {
                    var offset = i * 3;
                    AmFrameLayout.SetColor(
                        mapped, i, header,
                        rgb24[offset], rgb24[offset + 1], rgb24[offset + 2]);
                }

                continue;
            }

            AmFrameLayout.SetCharIndex(mapped, i, header, 0);
            if (color)
                AmFrameLayout.SetColor(mapped, i, header, 0, 0, 0);
        }

        return mapped;
    }
}
