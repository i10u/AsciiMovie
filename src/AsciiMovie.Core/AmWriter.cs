using System.Text;

namespace AsciiMovie.Core;

public static class AmWriter
{
    public static void Write(Stream stream, AmHeader header, IReadOnlyList<byte[]> frames)
    {
        if (frames.Count != header.FrameCount)
            throw new ArgumentException("Frame count mismatch.", nameof(frames));

        var expectedSize = header.UncompressedFrameSize;
        foreach (var frame in frames)
        {
            if (frame.Length != expectedSize)
                throw new ArgumentException($"Frame size must be {expectedSize} bytes.", nameof(frames));
        }

        var compressedFrames = new byte[frames.Count][];
        for (var i = 0; i < frames.Count; i++)
        {
            compressedFrames[i] = header.FramesDeflated
                ? Compression.DeflateRaw(frames[i])
                : frames[i].ToArray();
        }

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        WriteHeader(writer, header);

        uint offset = 0;
        for (var i = 0; i < compressedFrames.Length; i++)
        {
            writer.Write(offset);
            writer.Write((uint)compressedFrames[i].Length);
            offset += (uint)compressedFrames[i].Length;
        }

        foreach (var block in compressedFrames)
            writer.Write(block);
    }

    internal static void WriteHeader(BinaryWriter writer, AmHeader header)
    {
        var charsetBytes = Encoding.UTF8.GetBytes(header.Charset);

        writer.Write(Encoding.ASCII.GetBytes(AmHeader.Magic));
        writer.Write(header.Version);
        writer.Write((ushort)header.Flags);
        writer.Write(header.Cols);
        writer.Write(header.Rows);
        writer.Write(header.Fps);
        writer.Write(header.FrameCount);
        writer.Write((ushort)charsetBytes.Length);
        writer.Write(charsetBytes);
        writer.Write((byte)header.AudioCodec);
        writer.Write((uint)header.Audio.Length);
        if (header.Audio.Length > 0)
            writer.Write(header.Audio);
    }
}
