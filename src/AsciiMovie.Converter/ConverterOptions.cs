namespace AsciiMovie.Converter;

public sealed class ConverterOptions
{
    public string InputPath { get; init; } = "";
    public string OutputPath { get; init; } = "";
    public int Cols { get; init; } = 160;
    public int Rows { get; init; } = 90;
    public float? Fps { get; init; }
    public string Charset { get; init; } = Core.AsciiMapper.DefaultCharset;
    public bool CharsetExplicit { get; init; }
    public bool Mono { get; init; }
    public bool Edge { get; init; }
    public double EdgeStrength { get; init; } = Core.EdgeDetectionOptions.DefaultStrength;
    public bool NoInvert { get; init; }
    public bool NoAudio { get; init; }
    public string AudioCodec { get; init; } = "mp3";
    public string FfmpegPath { get; init; } = "ffmpeg";

    public static bool TryParse(string[] args, out ConverterOptions? options, out string? error)
    {
        options = null;
        error = null;

        if (args.Length == 1 && args[0] is "--verify-core" or "--help" or "-h")
        {
            error = args[0];
            return false;
        }

        string? input = null;
        string? output = null;
        int cols = 160;
        int rows = 90;
        float? fps = null;
        var charset = Core.AsciiMapper.DefaultCharset;
        var charsetExplicit = false;
        var mono = false;
        var edge = false;
        var edgeStrength = Core.EdgeDetectionOptions.DefaultStrength;
        var noInvert = false;
        var noAudio = false;
        var audioCodec = "mp3";
        var ffmpegPath = "ffmpeg";

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-o":
                case "--output":
                    if (!TryReadValue(args, ref i, out output))
                    {
                        error = "--output の値が指定されていません。";
                        return false;
                    }
                    break;
                case "--cols":
                    if (!TryReadInt(args, ref i, out cols))
                    {
                        error = "--cols の値が不正です。";
                        return false;
                    }
                    break;
                case "--rows":
                    if (!TryReadInt(args, ref i, out rows))
                    {
                        error = "--rows の値が不正です。";
                        return false;
                    }
                    break;
                case "--fps":
                    if (!TryReadFloat(args, ref i, out var fpsValue))
                    {
                        error = "--fps の値が不正です。";
                        return false;
                    }
                    fps = fpsValue;
                    break;
                case "--charset":
                    if (!TryReadValue(args, ref i, out var charsetValue) || string.IsNullOrEmpty(charsetValue))
                    {
                        error = "--charset の値が不正です。";
                        return false;
                    }
                    charset = charsetValue;
                    charsetExplicit = true;
                    break;
                case "--mono":
                    mono = true;
                    break;
                case "--edge":
                    edge = true;
                    break;
                case "--edge-strength":
                    if (!TryReadFloat(args, ref i, out var strengthValue) || strengthValue <= 0)
                    {
                        error = "--edge-strength の値は正の数である必要があります。";
                        return false;
                    }
                    edgeStrength = strengthValue;
                    break;
                case "--noinvert":
                    noInvert = true;
                    break;
                case "--no-audio":
                    noAudio = true;
                    break;
                case "--audio-codec":
                    if (!TryReadValue(args, ref i, out var codec) || codec is not ("mp3" or "aac"))
                    {
                        error = "--audio-codec が不正です（mp3 または aac を指定してください）。";
                        return false;
                    }
                    audioCodec = codec;
                    break;
                case "--ffmpeg":
                    if (!TryReadValue(args, ref i, out var ffmpeg))
                    {
                        error = "--ffmpeg の値が指定されていません。";
                        return false;
                    }
                    ffmpegPath = ffmpeg;
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        error = $"不明なオプション: {arg}";
                        return false;
                    }
                    if (input != null)
                    {
                        error = "入力ファイルが複数指定されています。";
                        return false;
                    }
                    input = arg;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "入力動画のパスが必要です。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            error = "出力パスが必要です（-o/--output）。";
            return false;
        }

        if (cols < 1 || rows < 1)
        {
            error = "cols と rows は正の整数である必要があります。";
            return false;
        }

        options = new ConverterOptions
        {
            InputPath = input!,
            OutputPath = output!,
            Cols = cols,
            Rows = rows,
            Fps = fps,
            Charset = charset,
            CharsetExplicit = charsetExplicit,
            Mono = mono,
            Edge = edge,
            EdgeStrength = edgeStrength,
            NoInvert = noInvert,
            NoAudio = noAudio,
            AudioCodec = audioCodec,
            FfmpegPath = ffmpegPath ?? "ffmpeg",
        };
        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, out string? value)
    {
        value = null;
        if (index + 1 >= args.Length)
            return false;
        value = args[++index];
        return true;
    }

    private static bool TryReadInt(string[] args, ref int index, out int value)
    {
        value = 0;
        if (!TryReadValue(args, ref index, out var text) || !int.TryParse(text, out value))
            return false;
        return true;
    }

    private static bool TryReadFloat(string[] args, ref int index, out float value)
    {
        value = 0;
        if (!TryReadValue(args, ref index, out var text) || !float.TryParse(text, out value))
            return false;
        return true;
    }

    public string BuildVideoFilterArgs(float fps) =>
        $"-vf scale={Cols}:{Rows},fps={fps.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public string BuildAudioArgs() => AudioCodec switch
    {
        "aac" => "-vn -c:a aac -b:a 128k -f adts",
        _ => "-vn -acodec libmp3lame -f mp3",
    };
}
