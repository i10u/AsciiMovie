using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AsciiMovie.Converter;

public sealed class FfmpegRunner
{
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;

    public FfmpegRunner(string ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
        _ffprobePath = DeriveFfprobePath(ffmpegPath);
    }

    public void EnsureAvailable()
    {
        try
        {
            RunAndWait(_ffmpegPath, "-version", captureOutput: true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"ffmpeg が見つかりません（'{_ffmpegPath}'）。ffmpeg をインストールして PATH に追加するか、--ffmpeg <path> を指定してください。README を参照してください。",
                ex);
        }
    }

    public async Task<(int Width, int Height, float Fps)> ProbeVideoAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        var args = $"-v error -select_streams v:0 -show_entries stream=width,height,avg_frame_rate -of csv=p=0:s=x \"{inputPath}\"";
        var output = await RunAndCaptureAsync(_ffprobePath, args, cancellationToken).ConfigureAwait(false);
        var line = output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()
                   ?? throw new InvalidOperationException("ffprobe returned no video stream info.");

        var parts = line.Split('x');
        if (parts.Length < 3
            || !int.TryParse(parts[0], out var width)
            || !int.TryParse(parts[1], out var height))
        {
            throw new InvalidOperationException($"Unexpected ffprobe output: {line}");
        }

        var fps = ParseFrameRate(parts[2]);
        return (width, height, fps);
    }

    public async Task ReadRawVideoFramesAsync(
        string inputPath,
        string videoFilterArgs,
        int frameBytes,
        Func<int, int, Task> onProgress,
        Func<byte[], Task> onFrame,
        CancellationToken cancellationToken = default)
    {
        var args = $"-i \"{inputPath}\" {videoFilterArgs} -f rawvideo -pix_fmt rgb24 -";
        await RunWithBinaryStdoutAsync(
            _ffmpegPath,
            args,
            frameBytes,
            async buffer =>
            {
                await onFrame(buffer).ConfigureAwait(false);
            },
            onProgress,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> ExtractAudioAsync(
        string inputPath,
        string audioArgs,
        CancellationToken cancellationToken = default)
    {
        var args = $"-i \"{inputPath}\" {audioArgs} -";
        return await RunAndCaptureBinaryAsync(_ffmpegPath, args, cancellationToken).ConfigureAwait(false);
    }

    internal static string DeriveFfprobePath(string ffmpegPath)
    {
        if (string.Equals(ffmpegPath, "ffmpeg", StringComparison.OrdinalIgnoreCase))
            return "ffprobe";

        var directory = Path.GetDirectoryName(ffmpegPath);
        var fileName = Path.GetFileName(ffmpegPath);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
            return "ffprobe";

        var probeName = Regex.Replace(fileName, "ffmpeg", "ffprobe", RegexOptions.IgnoreCase);
        return Path.Combine(directory, probeName);
    }

    internal static float ParseFrameRate(string value)
    {
        value = value.Trim();
        if (value.Contains('/'))
        {
            var parts = value.Split('/');
            if (parts.Length == 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den)
                && den > 0)
            {
                return (float)(num / den);
            }
        }

        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps))
            return fps;

        return 24f;
    }

    private static async Task RunWithBinaryStdoutAsync(
        string executable,
        string arguments,
        int chunkSize,
        Func<byte[], Task> onChunk,
        Func<int, int, Task> onProgress,
        CancellationToken cancellationToken)
    {
        using var process = StartProcess(executable, arguments, redirectStdout: true);
        var frameIndex = 0;
        var buffer = new byte[chunkSize];
        var fill = 0;

        var stderrTask = ReadStderrAsync(process);

        await using var stdout = process.StandardOutput.BaseStream;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await stdout.ReadAsync(buffer.AsMemory(fill, chunkSize - fill), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            fill += read;
            while (fill >= chunkSize)
            {
                var frame = buffer.AsSpan(0, chunkSize).ToArray();
                Array.Copy(buffer, chunkSize, buffer, 0, fill - chunkSize);
                fill -= chunkSize;
                frameIndex++;
                await onProgress(frameIndex, -1).ConfigureAwait(false);
                await onChunk(frame).ConfigureAwait(false);
            }
        }

        var stderr = await stderrTask.ConfigureAwait(false);
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(FormatProcessError(executable, process.ExitCode, stderr));
    }

    private static async Task<byte[]> RunAndCaptureBinaryAsync(
        string executable,
        string arguments,
        CancellationToken cancellationToken)
    {
        using var process = StartProcess(executable, arguments, redirectStdout: true);
        var stderrTask = ReadStderrAsync(process);

        await using var stdout = process.StandardOutput.BaseStream;
        using var ms = new MemoryStream();
        await stdout.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(FormatProcessError(executable, process.ExitCode, stderr));

        return ms.ToArray();
    }

    private static async Task<string> RunAndCaptureAsync(
        string executable,
        string arguments,
        CancellationToken cancellationToken)
    {
        using var process = StartProcess(executable, arguments, redirectStdout: true);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = ReadStderrAsync(process);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(FormatProcessError(executable, process.ExitCode, stderr));

        return stdout;
    }

    private static string RunAndWait(string executable, string arguments, bool captureOutput)
    {
        using var process = StartProcess(executable, arguments, redirectStdout: captureOutput);
        var stderr = ReadStderrAsync(process).GetAwaiter().GetResult();
        string stdout = captureOutput ? process.StandardOutput.ReadToEnd() : "";
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(FormatProcessError(executable, process.ExitCode, stderr));

        return stdout;
    }

    private static Process StartProcess(string executable, string arguments, bool redirectStdout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = redirectStdout,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = redirectStdout ? null : Encoding.UTF8,
        };

        try
        {
            return Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {executable}.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new InvalidOperationException($"Failed to start {executable}.", ex);
        }
    }

    private static async Task<string> ReadStderrAsync(Process process)
    {
        var lines = new List<string>();
        try
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
                lines.Add(line);
        }
        catch
        {
            // process exited
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatProcessError(string executable, int exitCode, string stderr)
    {
        var name = Path.GetFileName(executable);
        if (string.IsNullOrWhiteSpace(stderr))
            return $"{name} が終了コード {exitCode} で失敗しました。";

        return $"{name} が終了コード {exitCode} で失敗しました: {stderr.Trim()}";
    }
}
