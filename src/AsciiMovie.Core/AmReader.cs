using System.Text;

namespace AsciiMovie.Core;

public sealed class AmReader : IDisposable
{
    private readonly Stream _stream;
    private readonly BinaryReader _reader;
    private readonly (uint Offset, uint Size)[] _frameTable;
    private readonly long _framesBaseOffset;

    public AmHeader Header { get; }

    public AmReader(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        Header = ReadHeader(_reader);
        _frameTable = ReadFrameTable(_reader, Header.FrameCount);
        _framesBaseOffset = stream.Position;
    }

    public byte[] ReadFrame(int index)
    {
        if (index < 0 || index >= _frameTable.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        var entry = _frameTable[index];
        _stream.Seek(_framesBaseOffset + entry.Offset, SeekOrigin.Begin);
        var data = _reader.ReadBytes((int)entry.Size);

        if (Header.FramesDeflated)
            data = Compression.InflateRaw(data);

        if (data.Length != Header.UncompressedFrameSize)
            throw new InvalidDataException(
                $"Frame {index} decompressed to {data.Length} bytes, expected {Header.UncompressedFrameSize}.");

        return data;
    }

    public static AmHeader ReadHeader(BinaryReader reader)
    {
        var magicBytes = reader.ReadBytes(4);
        var magic = Encoding.ASCII.GetString(magicBytes);
        if (magic != AmHeader.Magic)
            throw new InvalidDataException($"Invalid magic: expected {AmHeader.Magic}, got {magic}.");

        var version = reader.ReadUInt16();
        if (version is not AmFrameLayout.Version1 and not AmFrameLayout.Version2)
            throw new InvalidDataException($"Unsupported version: {version}.");

        var flags = (AmFlags)reader.ReadUInt16();
        var cols = reader.ReadUInt16();
        var rows = reader.ReadUInt16();
        var fps = reader.ReadSingle();
        var frameCount = reader.ReadUInt32();
        var charsetLen = reader.ReadUInt16();
        var charsetBytes = reader.ReadBytes(charsetLen);
        var charset = Encoding.UTF8.GetString(charsetBytes);
        var audioCodec = (AmAudioCodec)reader.ReadByte();
        var audioLen = reader.ReadUInt32();
        var audio = audioLen > 0 ? reader.ReadBytes((int)audioLen) : Array.Empty<byte>();

        return new AmHeader
        {
            Version = version,
            Flags = flags,
            Cols = cols,
            Rows = rows,
            Fps = fps,
            FrameCount = frameCount,
            Charset = charset,
            AudioCodec = audioCodec,
            Audio = audio,
        };
    }

    private static (uint Offset, uint Size)[] ReadFrameTable(BinaryReader reader, uint frameCount)
    {
        var table = new (uint Offset, uint Size)[frameCount];
        for (var i = 0; i < frameCount; i++)
            table[i] = (reader.ReadUInt32(), reader.ReadUInt32());
        return table;
    }

    public void Dispose()
    {
        _reader.Dispose();
    }
}
