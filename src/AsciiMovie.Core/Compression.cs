using System.IO.Compression;

namespace AsciiMovie.Core;

public static class Compression
{
    public static byte[] DeflateRaw(ReadOnlySpan<byte> input)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(input);
        }

        return output.ToArray();
    }

    public static byte[] InflateRaw(ReadOnlySpan<byte> input)
    {
        using var inputStream = new MemoryStream(input.ToArray());
        using var deflate = new DeflateStream(inputStream, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }
}
