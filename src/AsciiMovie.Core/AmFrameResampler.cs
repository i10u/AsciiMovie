namespace AsciiMovie.Core;

public static class AmFrameResampler
{
    public static byte[] Resample(ReadOnlySpan<byte> frame, AmHeader source, int targetCols, int targetRows)
    {
        if (targetCols == source.Cols && targetRows == source.Rows)
            return frame.ToArray();

        var targetHeader = new AmHeader
        {
            Version = source.Version,
            Flags = source.Flags,
            Cols = (ushort)targetCols,
            Rows = (ushort)targetRows,
            Charset = source.Charset,
        };

        var output = new byte[AmFrameLayout.UncompressedFrameSize(targetHeader)];
        for (var y = 0; y < targetRows; y++)
        {
            var srcY = y * source.Rows / targetRows;
            for (var x = 0; x < targetCols; x++)
            {
                var srcX = x * source.Cols / targetCols;
                var srcIndex = srcY * source.Cols + srcX;
                var dstIndex = y * targetCols + x;

                var charIndex = AmFrameLayout.GetCharIndex(frame, srcIndex, source);
                AmFrameLayout.SetCharIndex(output, dstIndex, targetHeader, charIndex);

                if (source.HasColor)
                {
                    var (r, g, b) = AmFrameLayout.GetColor(frame, srcIndex, source);
                    AmFrameLayout.SetColor(output, dstIndex, targetHeader, r, g, b);
                }
            }
        }

        return output;
    }
}
