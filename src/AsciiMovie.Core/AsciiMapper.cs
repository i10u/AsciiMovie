using System.Numerics;

namespace AsciiMovie.Core;

public static class AsciiMapper
{
    /// <summary>旧既定（10 段）。互換テスト用。</summary>
    public const string LegacyCharset = " .:-=+*#%@";

    /// <summary>Unicode ブロック・ASCII 高密度ランプ 256 段（暗→明）。</summary>
    public static readonly string DefaultCharset = CreateDenseCharset256();

    /// <summary>Unicode 点字 256 段（低密度。非推奨）。</summary>
    public static readonly string BrailleCharset256 = CreateBrailleCharset256();

    private const double MinCoverageForColor = 0.75;

    public static byte[] MapFrame(
        ReadOnlySpan<byte> rgb24,
        int cols,
        int rows,
        string charset,
        bool color,
        ushort version = AmFrameLayout.Version2,
        bool allowAdaptiveInvert = true)
    {
        if (rgb24.Length != cols * rows * 3)
            throw new ArgumentException("RGB buffer size does not match cols×rows.", nameof(rgb24));

        if (string.IsNullOrEmpty(charset))
            throw new ArgumentException("Charset must not be empty.", nameof(charset));

        if (charset.Length > ushort.MaxValue)
            throw new ArgumentException($"Charset exceeds {ushort.MaxValue} characters.", nameof(charset));

        var header = new AmHeader
        {
            Version = version,
            Cols = (ushort)cols,
            Rows = (ushort)rows,
            Flags = color ? AmFlags.Color : AmFlags.None,
        };

        var cellCount = cols * rows;
        var output = new byte[AmFrameLayout.UncompressedFrameSize(header)];
        var maxIndex = charset.Length - 1;
        var coverage = BuildCoverageTable(charset);
        var denseFallback = FindDensestIndex(coverage);

        for (var i = 0; i < cellCount; i++)
        {
            var rgbOffset = i * 3;
            var r = rgb24[rgbOffset];
            var g = rgb24[rgbOffset + 1];
            var b = rgb24[rgbOffset + 2];
            var luma = 0.299 * r + 0.587 * g + 0.114 * b;
            var charIndex = ChooseCharIndex(luma, maxIndex, coverage, allowAdaptiveInvert, color, denseFallback);

            AmFrameLayout.SetCharIndex(output, i, header, charIndex);
            if (color)
                AmFrameLayout.SetColor(output, i, header, r, g, b);
        }

        return output;
    }

    internal static int ChooseCharIndex(
        double luma,
        int maxIndex,
        ReadOnlySpan<double> coverage,
        bool allowAdaptiveInvert,
        bool color = false,
        int denseFallback = -1)
    {
        if (maxIndex < 0)
            return 0;

        var normal = (int)Math.Round(luma / 255.0 * maxIndex);
        normal = Math.Clamp(normal, 0, maxIndex);

        if (color && luma < 1)
            return 0;

        if (!allowAdaptiveInvert || maxIndex == 0)
            return ApplyColorDensityFallback(normal, color, coverage, denseFallback);

        var inverted = maxIndex - normal;
        var chosen = coverage[normal] >= coverage[inverted] ? normal : inverted;
        return ApplyColorDensityFallback(chosen, color, coverage, denseFallback);
    }

    private static int ApplyColorDensityFallback(int index, bool color, ReadOnlySpan<double> coverage, int denseFallback)
    {
        if (!color || index < 0 || index >= coverage.Length)
            return index;

        if (coverage[index] >= MinCoverageForColor)
            return index;

        return denseFallback >= 0 ? denseFallback : index;
    }

    public static int FindDensestIndex(ReadOnlySpan<double> coverage)
    {
        var best = 0;
        for (var i = 1; i < coverage.Length; i++)
        {
            if (coverage[i] > coverage[best])
                best = i;
        }
        return best;
    }

    public static double[] BuildCoverageTable(string charset)
    {
        var maxIndex = charset.Length - 1;
        var table = new double[charset.Length];
        for (var i = 0; i < charset.Length; i++)
            table[i] = EstimateCoverage(charset[i], i, maxIndex);
        return table;
    }

    private static double EstimateCoverage(char ch, int index, int maxIndex)
    {
        if (char.IsWhiteSpace(ch))
            return 0;

        return ch switch
        {
            '█' => 1.0,
            '▓' => 0.85,
            '▒' => 0.65,
            '░' => 0.45,
            '@' => 0.9,
            '#' => 0.8,
            '%' => 0.75,
            _ => maxIndex > 0 ? (double)index / maxIndex : 1.0,
        };
    }

    private static string CreateDenseCharset256()
    {
        const string ramp = " .'`^\",:;Il!i><~+_-?][}{1)(|/\\tfjrxnuvczXYUJCLQ0OZMW@%&#░▒▓█";
        var chars = new char[256];
        var max = ramp.Length - 1;
        for (var i = 0; i < 256; i++)
        {
            var idx = (int)Math.Round(i / 255.0 * max);
            chars[i] = ramp[idx];
        }
        return new string(chars);
    }

    private static string CreateBrailleCharset256()
    {
        var patterns = new char[256];
        for (var i = 0; i < 256; i++)
            patterns[i] = (char)(0x2800 + i);

        Array.Sort(patterns, static (a, b) =>
        {
            var da = BrailleDotCount(a);
            var db = BrailleDotCount(b);
            return da != db ? da.CompareTo(db) : ((int)a).CompareTo((int)b);
        });

        return new string(patterns);
    }

    private static int BrailleDotCount(char c)
    {
        var pattern = (byte)((int)c - 0x2800);
        return BitOperations.PopCount(pattern);
    }
}
