using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace AsciiMovie.Player;

internal sealed record VideoProbeResult(float Fps, double DurationSeconds, int Width, int Height);

internal static class VideoProbe
{
    public static async Task<VideoProbeResult> ProbeAsync(
        string ffmpegPath,
        string videoPath,
        CancellationToken cancellationToken = default)
    {
        var ffprobe = DeriveFfprobePath(ffmpegPath);
        var streamArgs =
            $"-v error -select_streams v:0 -show_entries stream=avg_frame_rate -of csv=p=0 \"{videoPath}\"";
        var fpsText = (await RunAndCaptureAsync(ffprobe, streamArgs, cancellationToken).ConfigureAwait(false)).Trim();
        var fps = ParseFrameRate(fpsText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? fpsText);

        var durationArgs = $"-v error -show_entries format=duration -of csv=p=0 \"{videoPath}\"";
        var durationText = (await RunAndCaptureAsync(ffprobe, durationArgs, cancellationToken).ConfigureAwait(false)).Trim();
        if (!double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) || duration <= 0)
            duration = 1;

        var sizeArgs = "-v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0:s=x " + $"\"{videoPath}\"";
        var sizeText = (await RunAndCaptureAsync(ffprobe, sizeArgs, cancellationToken).ConfigureAwait(false)).Trim();
        var (width, height) = ParseSize(sizeText);

        return new VideoProbeResult(fps, duration, width, height);
    }

    public static void EnsureFfmpegAvailable(string ffmpegPath)
    {
        try
        {
            using var process = StartProcess(ffmpegPath, "-version", redirectStdout: true);
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg exited with code {process.ExitCode}.");
        }
        catch (Exception ex) when (ex is FileNotFoundException or System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(
                "ffmpeg が見つかりません。PATH に追加するか、README を参照してください。", ex);
        }
    }

    private static string DeriveFfprobePath(string ffmpegPath)
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

    private static float ParseFrameRate(string value)
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

        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps) && fps > 0
            ? fps
            : 24f;
    }

    private static (int Width, int Height) ParseSize(string value)
    {
        var lastLine = value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? value;
        var parts = lastLine.Split('x', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            && width > 0 && height > 0)
        {
            return (width, height);
        }

        return (1920, 1080);
    }

    private static async Task<string> RunAndCaptureAsync(
        string executable,
        string arguments,
        CancellationToken cancellationToken)
    {
        using var process = StartProcess(executable, arguments, redirectStdout: true);
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var name = Path.GetFileName(executable);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                ? $"{name} failed with exit code {process.ExitCode}."
                : $"{name} failed: {stderr.Trim()}");
        }

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
        };

        return Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {executable}.");
    }
}
