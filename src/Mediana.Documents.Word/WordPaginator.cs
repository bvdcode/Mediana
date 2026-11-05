// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    internal class WordPaginator(int maximumPages, CancellationToken token)
    {
        private readonly List<WordPaginatedPage> _pages = [];
        private List<WordPaint> _page = [];
        private WordSection _section = null!;
        private WordSection _pageSection = null!;
        private bool _first;
        private double _y;
        private double _after;
        private WordPageGeometry Geometry => _section.Geometry;
        private double BodyHeight => Geometry.Height - Geometry.Top - Geometry.Bottom;

        internal IReadOnlyList<WordPaginatedPage> Paginate(IReadOnlyList<WordSection> sections, WordFlowBuilder flow)
        {
            foreach (WordSection section in sections)
            {
                token.ThrowIfCancellationRequested();
                BeginSection(section);
                Add(flow.Build(section.Blocks, Geometry.Width - Geometry.Left - Geometry.Right));
            }
            Append(false);
            return _pages.AsReadOnly();
        }

        private void BeginSection(WordSection section)
        {
            if (_section is null)
            {
                _section = _pageSection = section;
                _first = true;
                return;
            }
            switch (section.Start)
            {
                case WordSectionStart.Continuous:
                    if (section.Geometry != Geometry) { throw new NotSupportedException("Continuous Word sections with changed page geometry are not supported."); }
                    _section = section;
                    if (_page.Count == 0) { _pageSection = section; _first = true; }
                    return;
                case WordSectionStart.NextPage:
                case WordSectionStart.EvenPage:
                case WordSectionStart.OddPage:
                    if (_page.Count > 0 || _y > 0) { Next(); }
                    _section = _pageSection = section;
                    _first = true;
                    break;
                default: throw new InvalidDataException("Invalid Word section start.");
            }
            bool blank = section.Start switch
            {
                WordSectionStart.NextPage => false,
                WordSectionStart.EvenPage => (_pages.Count + 1) % 2 != 0,
                WordSectionStart.OddPage => (_pages.Count + 1) % 2 != 1,
                WordSectionStart.Continuous => throw new InvalidDataException("Unexpected continuous Word section."),
                _ => throw new InvalidDataException("Invalid Word section start.")
            };
            if (blank) { Append(true); }
        }

        private void Add(IReadOnlyList<WordFlowBlock> blocks)
        {
            for (int index = 0; index < blocks.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                WordFlowBlock block = blocks[index];
                double spacing = Math.Max(_after, block.Before), height = block.Lines.Sum(line => line.Height);
                if (block.PageBreakBefore && _y > 0) { Next(); spacing = block.Before; }
                double keepHeight = block.KeepTogether ? height : block.Lines[0].Height;
                if (block.WidowControl && block.Lines.Count > 1) { keepHeight = Math.Max(keepHeight, block.Lines[0].Height + block.Lines[1].Height); }
                if (block.KeepNext)
                {
                    keepHeight = height;
                    for (int next = index + 1; next < blocks.Count; next++)
                    {
                        keepHeight += Math.Max(blocks[next - 1].After, blocks[next].Before);
                        keepHeight += blocks[next].KeepNext ? blocks[next].Lines.Sum(line => line.Height) : blocks[next].Lines[0].Height;
                        if (!blocks[next].KeepNext || keepHeight > BodyHeight) { break; }
                    }
                }
                if (_y > 0 && keepHeight <= BodyHeight && _y + spacing + keepHeight > BodyHeight) { Next(); spacing = 0; }
                _y += spacing;
                for (int lineIndex = 0; lineIndex < block.Lines.Count; lineIndex++)
                {
                    WordLine line = block.Lines[lineIndex];
                    if (_y > 0 && _y + line.Height > BodyHeight) { Next(); }
                    if (block.WidowControl && block.Lines.Count - lineIndex == 2 && _y > 0
                        && line.Height + block.Lines[lineIndex + 1].Height <= BodyHeight
                        && _y + line.Height + block.Lines[lineIndex + 1].Height > BodyHeight) { Next(); }
                    double consumed = 0;
                    do
                    {
                        token.ThrowIfCancellationRequested();
                        double chunk = Math.Min(line.Height - consumed, BodyHeight - _y);
                        if (chunk <= 0) { Next(); continue; }
                        bool split = consumed != 0 || chunk < line.Height;
                        IReadOnlyList<WordPaint> content = line.Paint;
                        if (split) { content = Array.AsReadOnly<WordPaint>([new WordGroupPaint(content, 0, -consumed)]); }
                        _page.Add(new WordGroupPaint(content, Geometry.Left, Geometry.Top + _y,
                            split ? Geometry.Width - Geometry.Left - Geometry.Right : null, split ? chunk : null));
                        consumed += chunk;
                        _y += chunk;
                        if (consumed < line.Height) { Next(); }
                    } while (consumed < line.Height);
                    if (line.PageBreakAfter) { Next(); }
                }
                _after = block.After;
            }
        }

        private void Append(bool blank)
        {
            if (_pages.Count >= maximumPages) { throw new InvalidDataException("Word pagination exceeds MaximumPages."); }
            _pages.Add(new(_pageSection, _first, blank, _page.AsReadOnly()));
            _page = [];
        }

        private void Next()
        {
            Append(false);
            if (_pages.Count >= maximumPages) { throw new InvalidDataException("Word pagination exceeds MaximumPages."); }
            _pageSection = _section;
            _first = false;
            _y = 0;
            _after = 0;
        }
    }
}
