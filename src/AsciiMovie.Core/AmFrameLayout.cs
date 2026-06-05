using System.Buffers.Binary;

namespace AsciiMovie.Core;

public static class AmFrameLayout
{
    public const ushort Version1 = 1;
    public const ushort Version2 = 2;

    public static int CharIndexSize(ushort version) => version >= Version2 ? 2 : 1;

    public static int CellStride(AmHeader header) =>
        CharIndexSize(header.Version) + (header.HasColor ? 3 : 0);

    public static int UncompressedFrameSize(AmHeader header) =>
        header.CellCount * CellStride(header);

    public static ushort GetCharIndex(ReadOnlySpan<byte> frame, int cellIndex, AmHeader header)
    {
        var offset = cellIndex * CellStride(header);
        return header.Version >= Version2
            ? BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(offset, 2))
            : frame[offset];
    }

    public static (byte R, byte G, byte B) GetColor(ReadOnlySpan<byte> frame, int cellIndex, AmHeader header)
    {
        var offset = cellIndex * CellStride(header) + CharIndexSize(header.Version);
        return (frame[offset], frame[offset + 1], frame[offset + 2]);
    }

    public static void SetCharIndex(Span<byte> frame, int cellIndex, AmHeader header, int charIndex)
    {
        var offset = cellIndex * CellStride(header);
        if (header.Version >= Version2)
            BinaryPrimitives.WriteUInt16LittleEndian(frame.Slice(offset, 2), (ushort)charIndex);
        else
            frame[offset] = (byte)charIndex;
    }

    public static void SetColor(Span<byte> frame, int cellIndex, AmHeader header, byte r, byte g, byte b)
    {
        var offset = cellIndex * CellStride(header) + CharIndexSize(header.Version);
        frame[offset] = r;
        frame[offset + 1] = g;
        frame[offset + 2] = b;
    }
}
