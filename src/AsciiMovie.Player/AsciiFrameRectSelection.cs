using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AsciiMovie.Core;

namespace AsciiMovie.Player;

public sealed class AsciiFrameRectSelection
{
    private static readonly Brush SelectionBrush = CreateSelectionBrush();

    private readonly RichTextBox _editor;
    private readonly Func<bool> _isPaused;
    private readonly Func<AmHeader?> _getHeader;
    private readonly Func<string[,]?> _getCellGrid;

    private bool _dragging;
    private bool _hasSelection;
    private int _anchorRow = -1;
    private int _anchorCol = -1;
    private int _endRow = -1;
    private int _endCol = -1;

    public AsciiFrameRectSelection(
        RichTextBox editor,
        Func<bool> isPaused,
        Func<AmHeader?> getHeader,
        Func<string[,]?> getCellGrid)
    {
        _editor = editor;
        _isPaused = isPaused;
        _getHeader = getHeader;
        _getCellGrid = getCellGrid;

        _editor.PreviewMouseLeftButtonDown += OnMouseDown;
        _editor.PreviewMouseMove += OnMouseMove;
        _editor.PreviewMouseLeftButtonUp += OnMouseUp;
        _editor.LostMouseCapture += (_, _) => _dragging = false;
    }

    public bool HasSelection => _isPaused() && _hasSelection;

    public bool TryCopyToClipboard()
    {
        var text = GetSelectedText();
        if (text == null)
            return false;

        Clipboard.SetText(text);
        return true;
    }

    public string? GetSelectedText()
    {
        if (!_isPaused() || !_hasSelection)
            return null;

        var grid = _getCellGrid();
        if (grid == null)
            return null;

        var (top, left, bottom, right) = GetNormalizedBounds();
        if (top < 0 || left < 0 || top > bottom || left > right)
            return null;

        return BuildRectangleText(grid, top, left, bottom, right);
    }

    public void ClearSelection()
    {
        _dragging = false;
        _hasSelection = false;
        _anchorRow = _anchorCol = _endRow = _endCol = -1;
        ClearHighlight(_editor.Document);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isPaused() || !TryGetCell(e.GetPosition(_editor), out var row, out var col))
            return;

        _dragging = true;
        _hasSelection = true;
        _anchorRow = _endRow = row;
        _anchorCol = _endCol = col;
        _editor.CaptureMouse();
        ApplyHighlight();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed)
            return;

        if (!TryGetCell(e.GetPosition(_editor), out var row, out var col))
            return;

        _endRow = row;
        _endCol = col;
        ApplyHighlight();
        e.Handled = true;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;
        _editor.ReleaseMouseCapture();

        if (TryGetCell(e.GetPosition(_editor), out var row, out var col))
        {
            _endRow = row;
            _endCol = col;
        }

        ApplyHighlight();
        e.Handled = true;
    }

    private void ApplyHighlight()
    {
        var doc = _editor.Document;
        if (doc == null)
            return;

        var (top, left, bottom, right) = GetNormalizedBounds();
        AsciiFrameText.ApplyRectangleHighlight(doc, top, left, bottom, right, SelectionBrush);
    }

    private (int Top, int Left, int Bottom, int Right) GetNormalizedBounds()
    {
        var top = Math.Min(_anchorRow, _endRow);
        var bottom = Math.Max(_anchorRow, _endRow);
        var left = Math.Min(_anchorCol, _endCol);
        var right = Math.Max(_anchorCol, _endCol);
        return (top, left, bottom, right);
    }

    private bool TryGetCell(Point positionInEditor, out int row, out int col)
    {
        row = col = -1;
        var header = _getHeader();
        if (header == null)
            return false;

        return AsciiFrameText.TryGetCellFromPoint(_editor, header, positionInEditor, out row, out col);
    }

    private static string BuildRectangleText(string[,] grid, int top, int left, int bottom, int right)
    {
        var rows = grid.GetLength(0);
        var cols = grid.GetLength(1);
        var sb = new StringBuilder();

        for (var row = top; row <= bottom; row++)
        {
            for (var col = left; col <= right; col++)
            {
                if (row >= 0 && row < rows && col >= 0 && col < cols)
                    sb.Append(grid[row, col]);
            }

            if (row < bottom)
                sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void ClearHighlight(FlowDocument? doc)
    {
        if (doc != null)
            AsciiFrameText.ApplyRectangleHighlight(doc, -1, -1, -1, -1, null);
    }

    private static Brush CreateSelectionBrush()
    {
        var brush = new SolidColorBrush(Color.FromArgb(96, 100, 149, 237));
        brush.Freeze();
        return brush;
    }

    public static string[,] BuildCellGrid(AmHeader header, byte[] frameData)
    {
        var cols = header.Cols;
        var rows = header.Rows;
        var grid = new string[rows, cols];
        var frame = frameData.AsSpan();

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
                grid[row, col] = AsciiFrameText.GetCellText(header, frame, row * cols + col);
        }

        return grid;
    }
}
