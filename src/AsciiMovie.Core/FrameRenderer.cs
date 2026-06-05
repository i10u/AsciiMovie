namespace AsciiMovie.Core;

public static class FrameRenderer
{
    public static byte[] MapRgbFrame(ReadOnlySpan<byte> rgb24, FrameRenderSettings settings)
    {
        if (settings.UseEdge)
        {
            return EdgeAsciiMapper.MapFrame(
                rgb24,
                settings.Cols,
                settings.Rows,
                settings.Charset,
                settings.Color,
                settings.AllowAdaptiveInvert,
                edgeOptions: settings.ToEdgeOptions());
        }

        return AsciiMapper.MapFrame(
            rgb24,
            settings.Cols,
            settings.Rows,
            settings.Charset,
            settings.Color,
            allowAdaptiveInvert: settings.AllowAdaptiveInvert);
    }
}
