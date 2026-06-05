namespace AsciiMovie.Core;

/// <summary>
/// .amov フォーマットの write→read 往復検証。
/// </summary>
public static class RoundTripSample
{
    public static void Verify()
    {
        VerifyColor();
        VerifyMono();
        VerifyWithAudio();
        VerifyV1Legacy();
        VerifyAdaptiveInvert();
    }

    public static void WriteSample(string path)
    {
        var header = new AmHeader
        {
            Cols = 4,
            Rows = 3,
            Fps = 2,
            FrameCount = 2,
            Flags = AmFlags.Color | AmFlags.FramesDeflated,
            Charset = AsciiMapper.DefaultCharset,
        };

        var frames = new List<byte[]>();
        for (var f = 0; f < 2; f++)
        {
            var rgb = new byte[4 * 3 * 3];
            for (var i = 0; i < rgb.Length; i += 3)
            {
                rgb[i] = (byte)((i + f * 40) % 256);
                rgb[i + 1] = (byte)((i * 2) % 256);
                rgb[i + 2] = (byte)((i * 3 + f * 80) % 256);
            }
            frames.Add(AsciiMapper.MapFrame(rgb, 4, 3, header.Charset, color: true));
        }

        using var fs = File.Create(path);
        AmWriter.Write(fs, header, frames);
    }

    private static void VerifyColor()
    {
        var header = new AmHeader
        {
            Cols = 2,
            Rows = 2,
            Fps = 24,
            FrameCount = 2,
            Flags = AmFlags.Color | AmFlags.FramesDeflated,
            Charset = AsciiMapper.DefaultCharset,
        };

        var rgb1 = new byte[]
        {
            0, 0, 0, 255, 255, 255,
            128, 128, 128, 64, 64, 64,
        };
        var rgb2 = new byte[]
        {
            255, 0, 0, 0, 255, 0,
            0, 0, 255, 255, 255, 255,
        };

        var frames = new[]
        {
            AsciiMapper.MapFrame(rgb1, 2, 2, header.Charset, color: true),
            AsciiMapper.MapFrame(rgb2, 2, 2, header.Charset, color: true),
        };

        RoundTrip(header, frames);
    }

    private static void VerifyMono()
    {
        var header = new AmHeader
        {
            Cols = 3,
            Rows = 2,
            Fps = 30,
            FrameCount = 1,
            Flags = AmFlags.FramesDeflated,
            Charset = AsciiMapper.DefaultCharset,
        };

        var rgb = new byte[3 * 2 * 3];
        for (var i = 0; i < rgb.Length; i += 3)
        {
            rgb[i] = (byte)(i * 7);
            rgb[i + 1] = (byte)(i * 3);
            rgb[i + 2] = (byte)(i * 5);
        }

        var frames = new[] { AsciiMapper.MapFrame(rgb, 3, 2, header.Charset, color: false) };
        RoundTrip(header, frames);
    }

    private static void VerifyWithAudio()
    {
        var header = new AmHeader
        {
            Cols = 1,
            Rows = 1,
            Fps = 1,
            FrameCount = 1,
            Flags = AmFlags.Color | AmFlags.Audio | AmFlags.FramesDeflated,
            Charset = "@",
            AudioCodec = AmAudioCodec.Mp3,
            Audio = new byte[] { 0xFF, 0xFB, 0x90, 0x00 },
        };

        var rgb = new byte[] { 200, 100, 50 };
        var frames = new[] { AsciiMapper.MapFrame(rgb, 1, 1, header.Charset, color: true) };
        RoundTrip(header, frames);
    }

    private static void VerifyV1Legacy()
    {
        var header = new AmHeader
        {
            Version = AmFrameLayout.Version1,
            Cols = 2,
            Rows = 1,
            Fps = 1,
            FrameCount = 1,
            Flags = AmFlags.FramesDeflated,
            Charset = ".@",
        };

        var frames = new[] { AsciiMapper.MapFrame(new byte[] { 0, 0, 0, 255, 255, 255 }, 2, 1, header.Charset, color: false, version: AmFrameLayout.Version1) };
        RoundTrip(header, frames);
    }

    private static void VerifyAdaptiveInvert()
    {
        var charset = " .#@";
        var coverage = AsciiMapper.BuildCoverageTable(charset);
        var dense = AsciiMapper.FindDensestIndex(coverage);

        var midAdaptive = AsciiMapper.ChooseCharIndex(80, charset.Length - 1, coverage, allowAdaptiveInvert: true);
        var midPlain = AsciiMapper.ChooseCharIndex(80, charset.Length - 1, coverage, allowAdaptiveInvert: false);
        if (coverage[midAdaptive] <= coverage[midPlain])
            throw new InvalidOperationException("Adaptive invert should prefer denser glyph for mid-tones.");

        var darkColor = AsciiMapper.ChooseCharIndex(30, charset.Length - 1, coverage, allowAdaptiveInvert: true, color: true, denseFallback: dense);
        if (coverage[darkColor] < 0.75)
            throw new InvalidOperationException("Color mode should fall back to dense glyph for visible dark tones.");
    }

    private static void RoundTrip(AmHeader header, byte[][] frames)
    {
        using var ms = new MemoryStream();
        AmWriter.Write(ms, header, frames);
        ms.Position = 0;

        using var reader = new AmReader(ms);
        AssertHeader(header, reader.Header);

        for (var i = 0; i < frames.Length; i++)
        {
            var read = reader.ReadFrame(i);
            if (!frames[i].AsSpan().SequenceEqual(read))
                throw new InvalidOperationException($"Frame {i} round-trip mismatch.");
        }
    }

    private static void AssertHeader(AmHeader expected, AmHeader actual)
    {
        if (expected.Version != actual.Version
            || expected.Flags != actual.Flags
            || expected.Cols != actual.Cols
            || expected.Rows != actual.Rows
            || Math.Abs(expected.Fps - actual.Fps) > 0.001f
            || expected.FrameCount != actual.FrameCount
            || expected.Charset != actual.Charset
            || expected.AudioCodec != actual.AudioCodec
            || !expected.Audio.AsSpan().SequenceEqual(actual.Audio))
        {
            throw new InvalidOperationException("Header round-trip mismatch.");
        }
    }
}
