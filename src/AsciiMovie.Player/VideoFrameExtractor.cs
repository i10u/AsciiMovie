using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace AsciiMovie.Player;

internal static class VideoFrameExtractor
{
    public static async Task<byte[]?> ExtractFrameAsync(
        string ffmpegPath,
        string videoPath,
        double timeSeconds,
        int cols,
        int rows,
        CancellationToken cancellationToken = default)
    {
        var frameBytes = cols * rows * 3;
        var time = Math.Max(0, timeSeconds).ToString("0.###", CultureInfo.InvariantCulture);
        var args =
            $"-ss {time} -i \"{videoPath}\" -vf scale={cols}:{rows} -frames:v 1 -an -f rawvideo -pix_fmt rgb24 -";

        using var process = StartProcess(ffmpegPath, args);
        var buffer = new byte[frameBytes];
        var fill = 0;

        await using var stdout = process.StandardOutput.BaseStream;
        while (fill < frameBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await stdout.ReadAsync(buffer.AsMemory(fill, frameBytes - fill), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;
            fill += read;
        }

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(FormatError(ffmpegPath, process.ExitCode, stderr));

        return fill == frameBytes ? buffer : null;
    }

    private static Process StartProcess(string executable, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        return Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {executable}.");
    }

    private static string FormatError(string executable, int exitCode, string stderr)
    {
        var name = Path.GetFileName(executable);
        return string.IsNullOrWhiteSpace(stderr)
            ? $"{name} exited with code {exitCode}."
            : $"{name} exited with code {exitCode}: {stderr.Trim()}";
    }
}
