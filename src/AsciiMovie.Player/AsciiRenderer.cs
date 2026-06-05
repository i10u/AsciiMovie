using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AsciiMovie.Core;

namespace AsciiMovie.Player;

public sealed class AsciiRenderer
{
    private const double RefFontSize = 64;

    private readonly Typeface _typeface;
    private readonly Brush _background;

    public AsciiRenderer(string fontFamily = "Consolas")
    {
        _typeface = new Typeface(new FontFamily(fontFamily), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        _background = Brushes.Black;
    }

    public ImageSource Render(AmHeader header, byte[] frameData, double targetWidth, double targetHeight)
    {
        var cols = header.Cols;
        var rows = header.Rows;
        var charset = header.Charset;
        var frame = frameData.AsSpan();

        var cellW = targetWidth / cols;
        var cellH = targetHeight / rows;
        var width = (int)Math.Ceiling(cols * cellW);
        var height = (int)Math.Ceiling(rows * cellH);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(_background, null, new Rect(0, 0, width, height));

            for (var row = 0; row < rows; row++)
            {
                for (var col = 0; col < cols; col++)
                {
                    var cellIndex = row * cols + col;
                    var charIndex = AmFrameLayout.GetCharIndex(frame, cellIndex, header);
                    Brush brush;

                    if (header.HasColor)
                    {
                        var (r, g, b) = AmFrameLayout.GetColor(frame, cellIndex, header);
                        brush = new SolidColorBrush(Color.FromRgb(r, g, b));
                    }
                    else
                    {
                        var gray = (byte)(charIndex * 255 / Math.Max(1, charset.Length - 1));
                        brush = new SolidColorBrush(Color.FromRgb(gray, gray, gray));
                    }

                    brush.Freeze();

                    if (charIndex >= charset.Length)
                        continue;

                    var ch = charset[charIndex].ToString(CultureInfo.InvariantCulture);
                    if (ch == " ")
                        continue;

                    DrawCellFilled(dc, ch, brush, col, row, cellW, cellH);
                }
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private void DrawCellFilled(DrawingContext dc, string ch, Brush brush, int col, int row, double cellW, double cellH)
    {
        var text = new FormattedText(
            ch,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _typeface,
            RefFontSize,
            brush,
            1.0);

        var scaleX = cellW / Math.Max(text.Width, 1);
        var scaleY = cellH / Math.Max(text.Height, 1);

        dc.PushTransform(new TranslateTransform(col * cellW, row * cellH));
        dc.PushTransform(new ScaleTransform(scaleX, scaleY));
        dc.DrawText(text, new Point(0, 0));
        dc.Pop();
        dc.Pop();
    }
}
