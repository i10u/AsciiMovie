using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AsciiMovie.Core;

namespace AsciiMovie.Player;

public static class AsciiFrameText
{
    public const double EditorFontSize = 12;
    public static readonly Thickness DocumentPadding = new(8);

    public static string BuildPlainText(AmHeader header, byte[] frameData)
    {
        var cols = header.Cols;
        var rows = header.Rows;
        var frame = frameData.AsSpan();
        var sb = new StringBuilder(cols * rows + rows);

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
                sb.Append(GetCellTextCore(header, frame, row * cols + col));

            if (row < rows - 1)
                sb.AppendLine();
        }

        return sb.ToString();
    }

    public static FlowDocument BuildDocument(
        AmHeader header,
        byte[] frameData,
        bool forceMonoDisplay = false,
        double? fontSize = null,
        FontFamily? fontFamily = null)
    {
        var cols = header.Cols;
        var rows = header.Rows;
        var frame = frameData.AsSpan();
        var size = fontSize ?? EditorFontSize;
        var family = fontFamily ?? new FontFamily("Consolas");

        var doc = new FlowDocument
        {
            PagePadding = DocumentPadding,
            Background = Brushes.Black,
            Foreground = Brushes.White,
            FontFamily = family,
            FontSize = size,
        };

        for (var row = 0; row < rows; row++)
        {
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0),
                LineHeight = size,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };

            for (var col = 0; col < cols; col++)
            {
                var cellIndex = row * cols + col;
                paragraph.Inlines.Add(new Run(GetCellTextCore(header, frame, cellIndex))
                {
                    Foreground = GetCellBrush(header, frame, cellIndex, forceMonoDisplay),
                });
            }

            doc.Blocks.Add(paragraph);
        }

        return doc;
    }

    public static bool TryGetCellFromPoint(
        RichTextBox editor,
        AmHeader header,
        Point positionInEditor,
        out int row,
        out int col)
    {
        row = col = -1;
        if (editor.Document == null)
            return false;

        var position = editor.GetPositionFromPoint(positionInEditor, snapToText: true)
            ?? editor.GetPositionFromPoint(
                new Point(
                    positionInEditor.X + editor.HorizontalOffset,
                    positionInEditor.Y + editor.VerticalOffset),
                snapToText: true);
        if (position == null)
            return false;

        return TryGetCellFromTextPointer(editor.Document, header, position, out row, out col);
    }

    public static bool TryGetCellFromTextPointer(
        FlowDocument doc,
        AmHeader header,
        TextPointer position,
        out int row,
        out int col)
    {
        row = col = -1;
        var rowIdx = 0;

        foreach (var block in doc.Blocks)
        {
            if (block is not Paragraph paragraph)
                continue;

            var colIdx = 0;
            foreach (var inline in paragraph.Inlines)
            {
                if (inline is not Run run)
                    continue;

                if (IsPointerInRun(position, run))
                {
                    row = rowIdx;
                    col = colIdx;
                    return row >= 0 && row < header.Rows && col >= 0 && col < header.Cols;
                }

                colIdx++;
            }

            rowIdx++;
        }

        return false;
    }

    public static void ApplyRectangleHighlight(
        FlowDocument doc,
        int top,
        int left,
        int bottom,
        int right,
        Brush? selectionBrush)
    {
        var rowIdx = 0;
        foreach (var block in doc.Blocks)
        {
            if (block is not Paragraph paragraph)
                continue;

            var colIdx = 0;
            foreach (var inline in paragraph.Inlines)
            {
                if (inline is not Run run)
                    continue;

                var selected = selectionBrush != null
                               && rowIdx >= top
                               && rowIdx <= bottom
                               && colIdx >= left
                               && colIdx <= right;
                run.Background = selected ? selectionBrush : null;
                colIdx++;
            }

            rowIdx++;
        }
    }

    public static string GetCellText(AmHeader header, ReadOnlySpan<byte> frame, int cellIndex) =>
        GetCellTextCore(header, frame, cellIndex);

    private static bool IsPointerInRun(TextPointer position, Run run)
    {
        if (position.CompareTo(run.ElementStart) < 0)
            return false;

        return position.CompareTo(run.ElementEnd) < 0;
    }

    private static string GetCellTextCore(AmHeader header, ReadOnlySpan<byte> frame, int cellIndex)
    {
        var charIndex = AmFrameLayout.GetCharIndex(frame, cellIndex, header);
        if (charIndex >= header.Charset.Length)
            return " ";

        return header.Charset[charIndex].ToString(CultureInfo.InvariantCulture);
    }

    private static Brush GetCellBrush(
        AmHeader header,
        ReadOnlySpan<byte> frame,
        int cellIndex,
        bool forceMonoDisplay)
    {
        if (header.HasColor && !forceMonoDisplay)
        {
            var (r, g, b) = AmFrameLayout.GetColor(frame, cellIndex, header);
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        var charIndex = AmFrameLayout.GetCharIndex(frame, cellIndex, header);
        var gray = (byte)(charIndex * 255 / Math.Max(1, header.Charset.Length - 1));
        var grayBrush = new SolidColorBrush(Color.FromRgb(gray, gray, gray));
        grayBrush.Freeze();
        return grayBrush;
    }
}
