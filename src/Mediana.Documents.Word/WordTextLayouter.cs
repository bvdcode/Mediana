// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System.Text;

namespace Mediana.Documents.Word
{
    internal class WordTextLayouter(WordFonts fonts, CancellationToken token)
    {
        internal WordFlowBlock Layout(WordParagraph paragraph, double width)
        {
            WordParagraphFormat format = paragraph.Format;
            WordAtom mark = fonts.Mark(paragraph.MarkFormat);
            List<WordAtom> atoms = [];
            foreach (WordInline inline in paragraph.Content)
            {
                if (inline.Format.Hidden) { continue; }
                switch (inline)
                {
                    case WordText text:
                        foreach (Rune character in text.Text.EnumerateRunes())
                        {
                            if (character.Value is 0x00ad or 0xfe0e or 0xfe0f or 0x200d) { continue; }
                            atoms.Add(fonts.Character(text.Format, character));
                        }
                        break;
                    case WordPicture picture:
                        atoms.Add(new(picture.Format, null, 0, picture.Width, picture.Height, 0, Picture: picture));
                        break;
                    case WordTab tab: atoms.Add(fonts.Mark(tab.Format) with { Tab = true, BreakAfter = true }); break;
                    case WordBreak next: atoms.Add(mark with { LineBreak = true, PageBreak = next.Page }); break;
                    default: throw new NotSupportedException("Unsupported Word inline layout.");
                }
            }
            List<WordLine> lines = [];
            int cursor = 0;
            bool emptyAfterBreak = false;
            while (cursor < atoms.Count || lines.Count == 0 || emptyAfterBreak)
            {
                token.ThrowIfCancellationRequested();
                emptyAfterBreak = false;
                double left = format.Left + (lines.Count == 0 ? format.FirstLine : 0);
                double available = width - format.Right - left;
                if (available <= 0 || !double.IsFinite(available)) { throw new InvalidDataException("Word paragraph indentation leaves no line width."); }
                int start = cursor, end = cursor, lastBreak = -1;
                double advance = 0;
                bool explicitBreak = false, page = false;
                while (end < atoms.Count)
                {
                    WordAtom atom = atoms[end];
                    if (atom.LineBreak)
                    {
                        explicitBreak = true;
                        page = atom.PageBreak;
                        emptyAfterBreak = end + 1 == atoms.Count;
                        break;
                    }
                    double current = atom.Tab ? TabAdvance(atoms, end, left + advance, format.Left, format.Tabs) : atom.Advance;
                    if (advance + current > available && end > start)
                    {
                        if (lastBreak >= start) { end = lastBreak + 1; }
                        break;
                    }
                    advance += current;
                    if (atom.BreakAfter) { lastBreak = end; }
                    end++;
                }
                cursor = end + (explicitBreak ? 1 : 0);
                if (!explicitBreak)
                {
                    while (cursor < atoms.Count && atoms[cursor].Space) { cursor++; }
                }
                lines.Add(Line(atoms, start, end, mark, left, available, format, cursor >= atoms.Count || explicitBreak, page));
            }
            return new(lines.AsReadOnly(), format.Before, format.After, format.PageBreakBefore,
                format.KeepLines, format.KeepNext, format.WidowControl);
        }

        private static WordLine Line(List<WordAtom> atoms, int start, int end, WordAtom mark, double left, double width,
            WordParagraphFormat format, bool last, bool page)
        {
            double ascent = mark.Ascent, descent = mark.Descent, advance = 0;
            int visibleEnd = end;
            while (visibleEnd > start && atoms[visibleEnd - 1].Space) { visibleEnd--; }
            int spaces = 0;
            for (int i = start; i < end; i++)
            {
                WordAtom atom = atoms[i];
                ascent = Math.Max(ascent, atom.Ascent);
                descent = Math.Max(descent, atom.Descent);
                if (i < visibleEnd)
                {
                    advance += atom.Tab ? TabAdvance(atoms, i, left + advance, format.Left, format.Tabs) : atom.Advance;
                    if (atom.Space) { spaces++; }
                }
            }
            double natural = ascent + descent;
            double height = format.LineRule switch
            {
                WordLineRule.Multiple => natural * format.Line,
                WordLineRule.AtLeast => Math.Max(natural, format.Line),
                WordLineRule.Exact => format.Line,
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
            if (!double.IsFinite(height) || height <= 0) { throw new InvalidDataException("Word line height must be finite and positive."); }
            double baseline = ascent + (height - natural) / 2;
            double offset = format.Alignment switch
            {
                WordAlignment.Left or WordAlignment.Justify => 0,
                WordAlignment.Center => (width - advance) / 2,
                WordAlignment.Right => width - advance,
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
            double spaceExtra = format.Alignment == WordAlignment.Justify && !last && spaces > 0 ? Math.Max(0, width - advance) / spaces : 0;
            List<WordPaint> background = [], foreground = [];
            if (format.Shading is Rgba32 shading) { background.Add(new WordRectanglePaint(left, 0, width, height, shading)); }
            double x = left + offset;
            for (int i = start; i < end; i++)
            {
                WordAtom atom = atoms[i];
                double current = atom.Tab ? TabAdvance(atoms, i, x, format.Left, format.Tabs) : atom.Advance + (atom.Space ? spaceExtra : 0);
                if (atom.Format.Shading is Rgba32 fill) { background.Add(new WordRectanglePaint(x, 0, current, height, fill)); }
                if (atom.Path is { CommandCount: > 0 } path)
                {
                    foreground.Add(new WordGlyphPaint(path, x, baseline - atom.Format.Baseline * atom.Format.Size, atom.Scale, atom.Format.Color));
                }
                if (atom.Picture is WordPicture picture) { foreground.Add(new WordImagePaint(picture, x, baseline - picture.Height)); }
                if (atom.Format.Underline) { foreground.Add(new WordRectanglePaint(x, baseline + atom.Format.Size * .1, current, Math.Max(.4, atom.Format.Size / 16), atom.Format.Color)); }
                if (atom.Format.Strike) { foreground.Add(new WordRectanglePaint(x, baseline - atom.Format.Size * .3, current, Math.Max(.4, atom.Format.Size / 16), atom.Format.Color)); }
                x += current;
            }
            Border(format.Borders.Left, left, 0, true);
            Border(format.Borders.Right, left + width, 0, true);
            if (start == 0) { Border(format.Borders.Top, left, 0, false); }
            if (last) { Border(format.Borders.Bottom, left, height, false); }
            background.AddRange(foreground);
            return new(background.AsReadOnly(), height, page);

            void Border(WordBorder? border, double bx, double by, bool vertical)
            {
                if (border is null || border.Width == 0) { return; }
                foreground.Add(new WordRectanglePaint(bx, by, vertical ? border.Width : width, vertical ? height : border.Width, border.Color));
            }
        }

        private static double TabAdvance(List<WordAtom> atoms, int position, double x, double left, IReadOnlyList<WordTabStop> tabs)
        {
            if (x < left - .001) { return left - x; }
            foreach (WordTabStop stop in tabs)
            {
                if (stop.Position <= x + .001) { continue; }
                double following = 0, decimalPrefix = 0;
                bool decimalSeen = false;
                for (int next = position + 1; next < atoms.Count && !atoms[next].Tab && !atoms[next].LineBreak; next++)
                {
                    if (atoms[next].Codepoint is '.' or ',') { decimalSeen = true; }
                    if (!decimalSeen) { decimalPrefix += atoms[next].Advance; }
                    following += atoms[next].Advance;
                }
                double adjustment = stop.Alignment switch
                {
                    WordTabAlignment.Left => 0,
                    WordTabAlignment.Center => following / 2,
                    WordTabAlignment.Right => following,
                    WordTabAlignment.Decimal => decimalPrefix,
                    _ => throw new ArgumentOutOfRangeException(nameof(tabs))
                };
                return Math.Max(0, stop.Position - x - adjustment);
            }
            const double defaultStop = 36;
            return (Math.Floor(x / defaultStop) + 1) * defaultStop - x;
        }
    }
}
