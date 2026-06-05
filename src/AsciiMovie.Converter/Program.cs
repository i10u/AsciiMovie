using AsciiMovie.Core;

namespace AsciiMovie.Converter;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--verify-core")
        {
            RoundTripSample.Verify();
            Console.Error.WriteLine("Core 往復テスト OK.");
            return 0;
        }

        if (args.Length == 2 && args[0] == "--write-sample")
        {
            RoundTripSample.WriteSample(args[1]);
            Console.Error.WriteLine($"サンプル出力: {args[1]}");
            return 0;
        }

        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        if (!ConverterOptions.TryParse(args, out var options, out var error))
        {
            if (error is "--help" or "-h")
            {
                PrintUsage();
                return 0;
            }

            Console.Error.WriteLine(error);
            PrintUsage();
            return 1;
        }

        try
        {
            await ConvertAsync(options!).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"エラー: {ex.Message}");
            return 1;
        }
    }

    private static async Task ConvertAsync(ConverterOptions options)
    {
        if (!File.Exists(options.InputPath))
            throw new FileNotFoundException($"入力ファイルが見つかりません: {Path.GetFullPath(options.InputPath)}");

        var outputDir = Path.GetDirectoryName(options.OutputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        var ffmpeg = new FfmpegRunner(options.FfmpegPath);
        ffmpeg.EnsureAvailable();

        var (_, _, probeFps) = await ffmpeg.ProbeVideoAsync(options.InputPath).ConfigureAwait(false);
        var fps = options.Fps ?? probeFps;
        if (fps <= 0)
            fps = 24f;

        var frameBytes = options.Cols * options.Rows * 3;
        var frames = new List<byte[]>();
        var color = !options.Mono;
        var adaptiveInvert = !options.NoInvert;
        var charset = options.Charset;
        var coverage = AsciiMapper.BuildCoverageTable(charset);
        var denseFallback = AsciiMapper.FindDensestIndex(coverage);
        var invertedCells = 0;
        var denseFallbackCells = 0;
        var maxIndex = charset.Length - 1;

        await ffmpeg.ReadRawVideoFramesAsync(
            options.InputPath,
            options.BuildVideoFilterArgs(fps),
            frameBytes,
            onProgress: (current, _) =>
            {
                Console.Error.WriteLine($"frame {current}/?");
                return Task.CompletedTask;
            },
            onFrame: frame =>
            {
                var renderSettings = new FrameRenderSettings
                {
                    Cols = options.Cols,
                    Rows = options.Rows,
                    UseEdge = options.Edge,
                    EdgeStrength = options.EdgeStrength,
                    Color = color,
                    AllowAdaptiveInvert = adaptiveInvert,
                    Charset = charset,
                };
                var mapped = FrameRenderer.MapRgbFrame(frame, renderSettings);

                if (!options.Edge && frames.Count == 0)
                {
                    for (var i = 0; i < options.Cols * options.Rows; i++)
                    {
                        var rgbOffset = i * 3;
                        var luma = 0.299 * frame[rgbOffset] + 0.587 * frame[rgbOffset + 1] + 0.114 * frame[rgbOffset + 2];
                        var normal = (int)Math.Round(luma / 255.0 * maxIndex);
                        normal = Math.Clamp(normal, 0, maxIndex);
                        var chosen = AmFrameLayout.GetCharIndex(mapped, i, new AmHeader
                        {
                            Version = AmHeader.CurrentVersion,
                            Cols = (ushort)options.Cols,
                            Rows = (ushort)options.Rows,
                            Flags = color ? AmFlags.Color : AmFlags.None,
                        });
                        if (adaptiveInvert && chosen != normal && luma >= 1)
                            invertedCells++;
                        if (color && luma >= 1 && chosen == denseFallback && coverage[chosen] >= 0.75)
                            denseFallbackCells++;
                    }
                }

                frames.Add(mapped);
                return Task.CompletedTask;
            }).ConfigureAwait(false);

        if (frames.Count == 0)
            throw new InvalidOperationException("入力動画からフレームをデコードできませんでした。");

        for (var i = 0; i < frames.Count; i++)
            Console.Error.WriteLine($"frame {i + 1}/{frames.Count}");

        byte[] audio = Array.Empty<byte>();
        AmAudioCodec audioCodec = AmAudioCodec.None;
        var flags = AmFlags.FramesDeflated;

        if (color)
            flags |= AmFlags.Color;

        if (!options.NoAudio)
        {
            audio = await ffmpeg.ExtractAudioAsync(options.InputPath, options.BuildAudioArgs()).ConfigureAwait(false);
            if (audio.Length > 0)
            {
                flags |= AmFlags.Audio;
                audioCodec = options.AudioCodec == "aac" ? AmAudioCodec.Aac : AmAudioCodec.Mp3;
            }
        }

        var header = new AmHeader
        {
            Flags = flags,
            Cols = (ushort)options.Cols,
            Rows = (ushort)options.Rows,
            Fps = fps,
            FrameCount = (uint)frames.Count,
            Charset = charset,
            AudioCodec = audioCodec,
            Audio = audio,
        };

        await using var output = File.Create(options.OutputPath);
        AmWriter.Write(output, header, frames);
        var invertNote = adaptiveInvert ? "反転文字自動選択 ON" : "反転文字自動選択 OFF (--noinvert)";
        var edgeNote = options.Edge
            ? "エッジのみ ASCII 化 ON"
            : "エッジのみ ASCII 化 OFF";
        if (adaptiveInvert && frames.Count > 0)
            Console.Error.WriteLine($"  1フレーム目: 反転選択 {invertedCells}/{options.Cols * options.Rows} セル, 高密度文字 {denseFallbackCells} セル");
        Console.Error.WriteLine($"出力完了: {options.OutputPath}（{frames.Count} フレーム、{options.Cols}x{options.Rows}、{fps:0.##} fps、文字ランプ {options.Charset.Length} 文字、{edgeNote}、{invertNote}）");
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("""
            AsciiMovie.Converter <入力動画> -o <出力.amov> [オプション]
              -o, --output <path>     出力 .amov パス（必須）
                  --cols <n>          横セル数（既定 160）
                  --rows <n>          縦セル数（既定 90）
                  --fps <n>           出力 fps（既定: 入力に合わせる、取得できなければ 24）
                  --charset <str>     文字ランプ（暗→明、最大 65536 文字、既定: 高密度 256 段）
                  --noinvert          セルごとの反転文字自動選択を無効化（既定: 有効）
                  --edge              エッジ部分だけ ASCII 化（他は空白、文字ランプは通常通り）
                  --edge-strength <n> エッジ検出の強さ（既定 1.0、大きいほどエッジ多め）
                  --mono              カラーを無効化
                  --no-audio          音声を含めない
                  --audio-codec <c>   mp3|aac（既定 mp3）
                  --ffmpeg <path>     ffmpeg のパス（既定: PATH 上の ffmpeg）
            """);
    }
}
