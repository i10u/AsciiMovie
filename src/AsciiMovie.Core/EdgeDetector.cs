namespace AsciiMovie.Core;

/// <summary>
/// Sobel エッジ検出と非極大値抑制。
/// </summary>
public static class EdgeDetector
{
    public static byte[] ApplyLineArt(ReadOnlySpan<byte> rgb24, int cols, int rows)
    {
        var output = new byte[rgb24.Length];
        ApplyLineArt(rgb24, cols, rows, output);
        return output;
    }

    public static void ApplyLineArt(ReadOnlySpan<byte> rgb24, int cols, int rows, Span<byte> output)
    {
        var cellCount = cols * rows;
        var mask = new bool[cellCount];
        BuildEdgeMask(rgb24, cols, rows, mask, out var edgeGray);

        for (var i = 0; i < cellCount; i++)
        {
            var offset = i * 3;
            var value = mask[i] ? edgeGray[i] : (byte)0;
            output[offset] = value;
            output[offset + 1] = value;
            output[offset + 2] = value;
        }
    }

    public static void BuildEdgeMask(
        ReadOnlySpan<byte> rgb24,
        int cols,
        int rows,
        Span<bool> mask,
        out byte[] edgeGray,
        EdgeDetectionOptions? options = null)
    {
        if (rgb24.Length != cols * rows * 3)
            throw new ArgumentException("RGB buffer size does not match cols×rows.", nameof(rgb24));
        if (mask.Length != cols * rows)
            throw new ArgumentException("Mask size does not match cols×rows.", nameof(mask));

        var cellCount = cols * rows;
        edgeGray = new byte[cellCount];
        var luma = new double[cellCount];
        var blurred = new double[cellCount];
        var magnitude = new double[cellCount];
        var gxField = new double[cellCount];
        var gyField = new double[cellCount];

        for (var i = 0; i < cellCount; i++)
            luma[i] = GetLuma(rgb24, i);

        BoxBlur3x3(luma, cols, rows, blurred);

        var maxMag = 0.0;
        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var index = row * cols + col;
                var (gx, gy, mag) = Sobel(blurred, cols, rows, col, row);
                gxField[index] = gx;
                gyField[index] = gy;
                magnitude[index] = mag;
                if (mag > maxMag)
                    maxMag = mag;
            }
        }

        var threshold = ComputeThreshold(magnitude, maxMag, options);
        var scale = Math.Max(1.0, maxMag - threshold);

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var index = row * cols + col;
                var isEdge = IsEdgeCell(magnitude, gxField, gyField, cols, rows, col, row, threshold);
                mask[index] = isEdge;
                edgeGray[index] = isEdge
                    ? (byte)Math.Clamp((magnitude[index] - threshold) / scale * 255.0, 0, 255)
                    : (byte)0;

            }
        }
    }

    private static double ComputeThreshold(
        ReadOnlySpan<double> magnitude,
        double maxMag,
        EdgeDetectionOptions? options)
    {
        if (maxMag <= 0)
            return double.PositiveInfinity;

        var strength = options?.ClampedStrength ?? EdgeDetectionOptions.DefaultStrength;

        Span<double> samples = magnitude.Length <= 4096
            ? stackalloc double[magnitude.Length]
            : new double[magnitude.Length];
        magnitude.CopyTo(samples);
        samples.Sort();

        var percentileRank = Math.Clamp(0.92 - strength * 0.10, 0.55, 0.97);
        var percentile = samples[(int)(samples.Length * percentileRank)];
        var floor = Math.Max(8.0, 15.0 / strength);
        var relative = Math.Max(floor, maxMag * (0.22 / strength));
        return Math.Max(percentile, relative);
    }

    private static bool IsEdgeCell(
        ReadOnlySpan<double> magnitude,
        ReadOnlySpan<double> gxField,
        ReadOnlySpan<double> gyField,
        int cols,
        int rows,
        int col,
        int row,
        double threshold)
    {
        var index = row * cols + col;
        var mag = magnitude[index];
        if (mag < threshold)
            return false;

        if (col <= 0 || col >= cols - 1 || row <= 0 || row >= rows - 1)
            return true;

        var gx = gxField[index];
        var gy = gyField[index];
        var angle = Math.Atan2(gy, gx) * 180.0 / Math.PI;
        if (angle < 0)
            angle += 180;

        double n1;
        double n2;
        if (angle < 22.5 || angle >= 157.5)
        {
            n1 = magnitude[index - 1];
            n2 = magnitude[index + 1];
        }
        else if (angle < 67.5)
        {
            n1 = magnitude[index - cols - 1];
            n2 = magnitude[index + cols + 1];
        }
        else if (angle < 112.5)
        {
            n1 = magnitude[index - cols];
            n2 = magnitude[index + cols];
        }
        else
        {
            n1 = magnitude[index - cols + 1];
            n2 = magnitude[index + cols - 1];
        }

        return mag >= n1 && mag >= n2;
    }

    private static double GetLuma(ReadOnlySpan<byte> rgb24, int cellIndex)
    {
        var offset = cellIndex * 3;
        return 0.299 * rgb24[offset] + 0.587 * rgb24[offset + 1] + 0.114 * rgb24[offset + 2];
    }

    private static void BoxBlur3x3(ReadOnlySpan<double> source, int cols, int rows, Span<double> dest)
    {
        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var sum = 0.0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                        sum += source[CellIndex(cols, rows, col + dx, row + dy)];
                }

                dest[row * cols + col] = sum / 9.0;
            }
        }
    }

    private static (double Gx, double Gy, double Magnitude) Sobel(
        ReadOnlySpan<double> luma,
        int cols,
        int rows,
        int col,
        int row)
    {
        var nw = luma[CellIndex(cols, rows, col - 1, row - 1)];
        var n = luma[CellIndex(cols, rows, col, row - 1)];
        var ne = luma[CellIndex(cols, rows, col + 1, row - 1)];
        var w = luma[CellIndex(cols, rows, col - 1, row)];
        var e = luma[CellIndex(cols, rows, col + 1, row)];
        var sw = luma[CellIndex(cols, rows, col - 1, row + 1)];
        var s = luma[CellIndex(cols, rows, col, row + 1)];
        var se = luma[CellIndex(cols, rows, col + 1, row + 1)];

        var gx = -nw + ne - 2 * w + 2 * e - sw + se;
        var gy = -nw - 2 * n - ne + sw + 2 * s + se;
        return (gx, gy, Math.Sqrt(gx * gx + gy * gy));
    }

    private static int CellIndex(int cols, int rows, int col, int row)
    {
        col = Math.Clamp(col, 0, cols - 1);
        row = Math.Clamp(row, 0, rows - 1);
        return row * cols + col;
    }
}
