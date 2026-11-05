// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    internal class WordFlowBuilder(WordTextLayouter text, CancellationToken token)
    {
        internal IReadOnlyList<WordFlowBlock> Build(IReadOnlyList<WordBlock> blocks, double width, int depth = 0)
        {
            if (depth > 32) { throw new InvalidDataException("Word tables exceed the layout nesting limit."); }
            List<WordFlowBlock> result = [];
            foreach (WordBlock block in blocks)
            {
                token.ThrowIfCancellationRequested();
                switch (block)
                {
                    case WordParagraph paragraph: result.Add(text.Layout(paragraph, width)); break;
                    case WordTable table: result.AddRange(Table(table, width, depth)); break;
                    default: throw new NotSupportedException("Unsupported Word block layout.");
                }
            }
            return result.AsReadOnly();
        }

        internal static (IReadOnlyList<WordPaint> Paint, double Height) Stack(IReadOnlyList<WordFlowBlock> blocks)
        {
            List<WordPaint> result = [];
            double y = 0, after = 0;
            foreach (WordFlowBlock block in blocks)
            {
                y += Math.Max(after, block.Before);
                foreach (WordLine line in block.Lines)
                {
                    result.Add(new WordGroupPaint(line.Paint, 0, y));
                    y += line.Height;
                }
                after = block.After;
            }
            return (result.AsReadOnly(), y + after);
        }

        private IReadOnlyList<WordFlowBlock> Table(WordTable table, double available, int depth)
        {
            double tableWidth = table.Columns.Sum();
            double indent = table.Alignment switch
            {
                WordAlignment.Left => table.Indent,
                WordAlignment.Center => (available - tableWidth) / 2,
                WordAlignment.Right => available - tableWidth,
                WordAlignment.Justify => throw new NotSupportedException("Justified Word tables are not supported."),
                _ => throw new ArgumentOutOfRangeException(nameof(table))
            };
            List<WordFlowBlock> rows = [];
            foreach (WordTableRow row in table.Rows)
            {
                token.ThrowIfCancellationRequested();
                List<(WordTableCell Cell, double X, double Width, IReadOnlyList<WordPaint> Paint)> cells = [];
                double height = row.MinimumHeight, x = indent;
                int column = 0;
                foreach (WordTableCell cell in row.Cells)
                {
                    double cellWidth = 0;
                    for (int i = 0; i < cell.Span; i++) { cellWidth += table.Columns[column++]; }
                    if (cellWidth <= cell.Left + cell.Right) { throw new InvalidDataException("Word cell margins leave no content width."); }
                    (IReadOnlyList<WordPaint> paint, double contentHeight) = Stack(Build(cell.Blocks, cellWidth - cell.Left - cell.Right, depth + 1));
                    if (!row.ExactHeight) { height = Math.Max(height, contentHeight + cell.Top + cell.Bottom); }
                    cells.Add((cell, x, cellWidth, paint));
                    x += cellWidth;
                }
                if (!double.IsFinite(height) || height <= 0) { throw new InvalidDataException("Word table row height must be finite and positive."); }
                List<WordPaint> commands = [];
                foreach ((WordTableCell cell, double cellX, double cellWidth, IReadOnlyList<WordPaint> paint) in cells)
                {
                    if (cell.Shading is Drawing.Rgba32 fill) { commands.Add(new WordRectanglePaint(cellX, 0, cellWidth, height, fill)); }
                    commands.Add(new WordGroupPaint(paint, cellX + cell.Left, cell.Top,
                        row.ExactHeight ? cellWidth - cell.Left - cell.Right : null, row.ExactHeight ? Math.Max(0, height - cell.Top - cell.Bottom) : null));
                    Border(cell.Borders.Left, cellX, 0, true);
                    Border(cell.Borders.Right, cellX + cellWidth, 0, true);
                    Border(cell.Borders.Top, cellX, 0, false);
                    Border(cell.Borders.Bottom, cellX, height, false);
                    void Border(WordBorder? border, double bx, double by, bool vertical)
                    {
                        if (border is null || border.Width == 0) { return; }
                        commands.Add(new WordRectanglePaint(bx, by, vertical ? border.Width : cellWidth, vertical ? height : border.Width, border.Color));
                    }
                }
                rows.Add(new(Array.AsReadOnly(new[] { new WordLine(commands.AsReadOnly(), height, false) }), 0, 0, false, true, false, false));
            }
            return rows.AsReadOnly();
        }
    }
}
