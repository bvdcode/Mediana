// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    internal class WordPaginator(WordPageGeometry geometry, int maximumPages, CancellationToken token)
    {
        internal IReadOnlyList<IReadOnlyList<WordPaint>> Paginate(IReadOnlyList<WordFlowBlock> blocks)
        {
            List<IReadOnlyList<WordPaint>> pages = [];
            List<WordPaint> page = [];
            double bodyHeight = geometry.Height - geometry.Top - geometry.Bottom;
            double y = 0, after = 0;
            for (int index = 0; index < blocks.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                WordFlowBlock block = blocks[index];
                double spacing = Math.Max(after, block.Before), height = block.Lines.Sum(line => line.Height);
                if (block.PageBreakBefore && y > 0) { Next(); spacing = block.Before; }
                double keepHeight = block.KeepTogether ? height : block.Lines[0].Height;
                if (block.WidowControl && block.Lines.Count > 1) { keepHeight = Math.Max(keepHeight, block.Lines[0].Height + block.Lines[1].Height); }
                if (block.KeepNext)
                {
                    keepHeight = height;
                    for (int next = index + 1; next < blocks.Count; next++)
                    {
                        keepHeight += Math.Max(blocks[next - 1].After, blocks[next].Before);
                        keepHeight += blocks[next].KeepNext ? blocks[next].Lines.Sum(line => line.Height) : blocks[next].Lines[0].Height;
                        if (!blocks[next].KeepNext || keepHeight > bodyHeight) { break; }
                    }
                }
                if (y > 0 && keepHeight <= bodyHeight && y + spacing + keepHeight > bodyHeight) { Next(); spacing = 0; }
                y += spacing;
                for (int lineIndex = 0; lineIndex < block.Lines.Count; lineIndex++)
                {
                    WordLine line = block.Lines[lineIndex];
                    if (y > 0 && y + line.Height > bodyHeight) { Next(); }
                    if (block.WidowControl && block.Lines.Count - lineIndex == 2 && y > 0
                        && line.Height + block.Lines[lineIndex + 1].Height <= bodyHeight
                        && y + line.Height + block.Lines[lineIndex + 1].Height > bodyHeight) { Next(); }
                    double consumed = 0;
                    do
                    {
                        token.ThrowIfCancellationRequested();
                        double chunk = Math.Min(line.Height - consumed, bodyHeight - y);
                        if (chunk <= 0) { Next(); continue; }
                        bool split = consumed != 0 || chunk < line.Height;
                        IReadOnlyList<WordPaint> content = line.Paint;
                        if (split) { content = Array.AsReadOnly<WordPaint>([new WordGroupPaint(content, 0, -consumed)]); }
                        page.Add(new WordGroupPaint(content, geometry.Left, geometry.Top + y, split ? geometry.Width - geometry.Left - geometry.Right : null, split ? chunk : null));
                        consumed += chunk;
                        y += chunk;
                        if (consumed < line.Height) { Next(); }
                    } while (consumed < line.Height);
                    if (line.PageBreakAfter) { Next(); }
                }
                after = block.After;
            }
            pages.Add(page.AsReadOnly());
            if (pages.Count > maximumPages) { throw new InvalidDataException("Word pagination exceeds MaximumPages."); }
            return pages.AsReadOnly();

            void Next()
            {
                if (pages.Count >= maximumPages - 1) { throw new InvalidDataException("Word pagination exceeds MaximumPages."); }
                pages.Add(page.AsReadOnly());
                page = [];
                y = 0;
                after = 0;
            }
        }
    }
}
