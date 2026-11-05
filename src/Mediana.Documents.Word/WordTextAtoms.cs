// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text;

namespace Mediana.Documents.Word
{
    internal class WordTextAtoms(WordFonts fonts, CancellationToken token)
    {
        internal List<WordAtom> Build(IReadOnlyList<WordInline> content, WordAtom mark)
        {
            List<WordAtom> atoms = [];
            for (int index = 0; index < content.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                WordInline inline = content[index];
                if (inline is WordText) { Text(content, ref index, atoms); continue; }
                if (inline.Format.Hidden) { continue; }
                switch (inline)
                {
                    case WordPicture picture:
                        atoms.Add(new(picture.Format, null, 0, picture.Width, picture.Height, 0, Picture: picture));
                        break;
                    case WordTab tab: atoms.Add(fonts.Mark(tab.Format) with { Tab = true, BreakAfter = true }); break;
                    case WordBreak next: atoms.Add(mark with { LineBreak = true, PageBreak = next.Page }); break;
                    default: throw new NotSupportedException("Unsupported Word inline layout.");
                }
            }
            return atoms;
        }

        private void Text(IReadOnlyList<WordInline> content, ref int index, List<WordAtom> atoms)
        {
            if ((index + 1 == content.Count || content[index + 1] is not WordText) && content[index] is WordText single)
            {
                if (!single.Format.Hidden && single.Text.Length != 0) { Elements(single.Text, [(single.Text.Length, single.Format)], atoms); }
                return;
            }
            StringBuilder builder = new();
            List<(int End, WordRunFormat Format)> runs = [];
            while (index < content.Count && content[index] is WordText text)
            {
                if (!text.Format.Hidden && text.Text.Length != 0)
                {
                    builder.Append(text.Text);
                    runs.Add((builder.Length, text.Format));
                }
                index++;
            }
            index--;
            Elements(builder.ToString(), runs, atoms);
        }

        private void Elements(string value, IReadOnlyList<(int End, WordRunFormat Format)> runs, List<WordAtom> atoms)
        {
            int run = 0;
            for (int offset = 0; offset < value.Length;)
            {
                token.ThrowIfCancellationRequested();
                while (offset >= runs[run].End) { run++; }
                int length = StringInfo.GetNextTextElementLength(value.AsSpan(offset));
                ReadOnlySpan<char> element = value.AsSpan(offset, length);
                if (fonts.Graphic(runs[run].Format, element) is WordAtom graphic) { atoms.Add(graphic); }
                else
                {
                    int start = atoms.Count, characterOffset = offset;
                    foreach (Rune character in element.EnumerateRunes())
                    {
                        while (characterOffset >= runs[run].End) { run++; }
                        characterOffset += character.Utf16SequenceLength;
                        if (character.Value is 0x00ad or 0xfe0e or 0xfe0f or 0x200d) { continue; }
                        atoms.Add(fonts.Character(runs[run].Format, character) with { ClusterContinuation = atoms.Count > start });
                    }
                    for (int atom = start; atom < atoms.Count - 1; atom++) { atoms[atom] = atoms[atom] with { BreakAfter = false }; }
                }
                offset += length;
            }
        }
    }
}
