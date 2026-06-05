namespace AsciiMovie.Core;

public enum AmAudioCodec : byte
{
    None = 0,
    Mp3 = 1,
    Aac = 2,
}

public sealed class AmHeader
{
    public const ushort CurrentVersion = AmFrameLayout.Version2;
    public const string Magic = "AMOV";

    public ushort Version { get; set; } = CurrentVersion;
    public AmFlags Flags { get; set; }
    public ushort Cols { get; set; }
    public ushort Rows { get; set; }
    public float Fps { get; set; }
    public uint FrameCount { get; set; }
    public string Charset { get; set; } = AsciiMapper.DefaultCharset;
    public AmAudioCodec AudioCodec { get; set; } = AmAudioCodec.None;
    public byte[] Audio { get; set; } = Array.Empty<byte>();

    public int CellCount => Cols * Rows;

    public int UncompressedFrameSize => AmFrameLayout.UncompressedFrameSize(this);

    public bool HasColor => (Flags & AmFlags.Color) != 0;
    public bool HasAudio => (Flags & AmFlags.Audio) != 0;
    public bool FramesDeflated => (Flags & AmFlags.FramesDeflated) != 0;
}
