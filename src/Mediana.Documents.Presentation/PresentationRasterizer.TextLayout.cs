// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System.Numerics;
using System.Xml.Linq;

namespace Mediana.Documents.Presentation
{
    internal partial class PresentationRasterizer
    {
        private (double Height, bool FitsWidth) LayoutText(IReadOnlyList<PresentationTextParagraph> paragraphs, double width, double left, double right,
            bool wrap, double lineReduction, double scale, List<PresentationTextLine>? lines = null)
        {
            double y = 0;
            bool fitsWidth = true;
            foreach (PresentationTextParagraph paragraph in paragraphs)
            {
                IReadOnlyList<PresentationTextAtom> atoms = paragraph.Atoms;
                double markSize = paragraph.MarkSize * scale;
                y += Spacing(paragraph.Styles, "spcBef", markSize);
                XElement? lineSpacing = paragraph.Styles.SelectMany(item => item.Elements()).LastOrDefault(item => item.Name.LocalName == "lnSpc")?.Elements().FirstOrDefault();
                int cursor = 0, lineIndex = 0;
                bool empty = atoms.Count == 0;
                do
                {
                    token.ThrowIfCancellationRequested();
                    int start = cursor, end = cursor, lastBreak = -1;
                    double x = left + paragraph.MarginLeft + (lineIndex == 0 ? paragraph.Indent : 0);
                    double available = width - right - paragraph.MarginRight - x, advance = 0;
                    if (available <= 0) { throw new InvalidDataException("Presentation paragraph has no available line width."); }
                    bool hardBreak = false;
                    while (end < atoms.Count)
                    {
                        PresentationTextAtom atom = atoms[end];
                        if (atom.Break) { hardBreak = true; break; }
                        if (wrap && advance + atom.Advance * scale > available && end > start)
                        { if (lastBreak >= start) { end = lastBreak + 1; } break; }
                        advance += atom.Advance * scale;
                        if (atom.Space) { lastBreak = end; }
                        end++;
                    }
                    cursor = end + (hardBreak ? 1 : 0);
                    if (!hardBreak) { while (cursor < atoms.Count && atoms[cursor].Space) { cursor++; } }
                    int visible = end;
                    while (visible > start && atoms[visible - 1].Space) { visible--; }
                    double ascent = visible == start ? markSize * .8 : 0, descent = visible == start ? markSize * .2 : 0, lineWidth = 0;
                    int spaces = 0;
                    for (int i = start; i < visible; i++)
                    {
                        PresentationTextAtom atom = atoms[i];
                        ascent = Math.Max(ascent, atom.Ascent * scale); descent = Math.Max(descent, atom.Descent * scale);
                        lineWidth += atom.Advance * scale;
                        if (atom.Space) { spaces++; }
                    }
                    fitsWidth &= lineWidth <= available;
                    double natural = ascent + descent;
                    double lineHeight = Spacing(paragraph.Styles, "lnSpc", natural, natural);
                    if (lineSpacing is null || lineSpacing.Name.LocalName == "spcPct") { lineHeight *= 1 - lineReduction; }
                    if (lines is not null)
                    {
                        double offset = paragraph.Alignment switch
                        {
                            "l" or "just" or "dist" or "justLow" => 0,
                            "ctr" => (available - lineWidth) / 2,
                            "r" => available - lineWidth,
                            _ => throw new NotSupportedException("Unsupported presentation paragraph alignment: " + paragraph.Alignment)
                        };
                        double extra = paragraph.Alignment == "just" && cursor < atoms.Count && !hardBreak && spaces > 0 ? Math.Max(0, available - lineWidth) / spaces : 0;
                        PresentationTextAtom[] content = new PresentationTextAtom[visible - start];
                        for (int i = 0; i < content.Length; i++) { content[i] = atoms[start + i]; }
                        lines.Add(new(content, x + offset, y, ascent + (lineHeight - natural) / 2, lineHeight, extra));
                    }
                    y += lineHeight; lineIndex++; empty = false;
                    if (hardBreak && cursor == atoms.Count) { empty = true; }
                } while (cursor < atoms.Count || empty);
                y += Spacing(paragraph.Styles, "spcAft", markSize);
            }
            return (y, fitsWidth);
        }

        private void ClipText(XElement properties, double width, double height, Matrix3x2 transform)
        {
            bool horizontal = (string?)properties.Attribute("horzOverflow") switch
            {
                null or "overflow" => false,
                "clip" => true,
                _ => throw new NotSupportedException("Unsupported presentation horizontal text overflow.")
            };
            bool vertical = (string?)properties.Attribute("vertOverflow") switch
            {
                null or "overflow" => false,
                "clip" => true,
                _ => throw new NotSupportedException("Unsupported presentation vertical text overflow.")
            };
            if (!horizontal && !vertical) { return; }
            if (!Matrix3x2.Invert(transform, out Matrix3x2 inverse)) { throw new InvalidDataException("Singular presentation text transform."); }
            Vector2[] corners = [Vector2.Zero, new(canvas.Buffer.Width, 0), new(0, canvas.Buffer.Height), new(canvas.Buffer.Width, canvas.Buffer.Height)];
            for (int i = 0; i < corners.Length; i++) { corners[i] = Vector2.Transform(corners[i], inverse); }
            float left = horizontal ? 0 : corners.Min(point => point.X), right = horizontal ? (float)width : corners.Max(point => point.X);
            float top = vertical ? 0 : corners.Min(point => point.Y), bottom = vertical ? (float)height : corners.Max(point => point.Y);
            canvas.ClipPath(new VectorPath().Rectangle(left, top, right - left, bottom - top), transform, cancellationToken: token);
        }
    }
}
