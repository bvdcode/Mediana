// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;

namespace Mediana.Documents.Word
{
    public partial class WordDocument
    {
        public IEnumerable<DocumentTextBlock> ReadText(DocumentTextOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new();
            DocumentTextBudget budget = new(options);
            for (int section = 0; section < Sections.Count; section++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!options.IncludesPart(section)) { continue; }
                DocumentTextPathSegment[] path = [new(DocumentTextNodeKind.Section, section)];
                foreach (DocumentTextBlock block in Blocks(Sections[section].Blocks, section, path, PartName))
                { budget.Consume(block.Text, cancellationToken); yield return block; }
                if (options.IncludeHeaders)
                {
                    foreach (DocumentTextBlock block in Margins(Sections[section].Header, section, path, DocumentTextSourceKind.Header, DocumentTextNodeKind.Header)) { yield return block; }
                }
                if (options.IncludeFooters)
                {
                    foreach (DocumentTextBlock block in Margins(Sections[section].Footer, section, path, DocumentTextSourceKind.Footer, DocumentTextNodeKind.Footer)) { yield return block; }
                }
            }

            IEnumerable<DocumentTextBlock> Margins(WordMarginContent content, int section, DocumentTextPathSegment[] parent, DocumentTextSourceKind kind, DocumentTextNodeKind node)
            {
                foreach (DocumentHeaderFooterVariant variant in new[] { DocumentHeaderFooterVariant.Default, DocumentHeaderFooterVariant.First, DocumentHeaderFooterVariant.Even })
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (DocumentTextBlock block in Blocks(content.GetBlocks(variant), section, [.. parent, new(node, (int)variant)], content.PartNames.GetValueOrDefault(variant)))
                    {
                        budget.Consume(block.Text, cancellationToken);
                        yield return block with { SourceKind = kind, HeaderFooterVariant = variant };
                    }
                }
            }

            IEnumerable<DocumentTextBlock> Blocks(IReadOnlyList<WordBlock> blocks, int section, DocumentTextPathSegment[] parent, string? partName)
            {
                for (int i = 0; i < blocks.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    switch (blocks[i])
                    {
                        case WordParagraph paragraph:
                            StringBuilder text = new();
                            foreach (WordInline inline in paragraph.Content)
                            {
                                switch (inline)
                                {
                                    case WordText value: text.Append(value.Text); break;
                                    case WordTab: text.Append('\t'); break;
                                    case WordBreak: text.Append('\n'); break;
                                    case WordPicture: break;
                                    default: throw new NotSupportedException("Unknown Word inline content.");
                                }
                            }
                            yield return new(text.ToString(), section, partName,
                                Array.AsReadOnly(parent.Append(new(DocumentTextNodeKind.Paragraph, i)).ToArray()), DocumentTextStructure.Declared);
                            break;
                        case WordTable table:
                            for (int row = 0; row < table.Rows.Count; row++)
                            {
                                int column = 0;
                                foreach (WordTableCell cell in table.Rows[row].Cells)
                                {
                                    DocumentTextPathSegment[] nested = [..parent, new(DocumentTextNodeKind.Table, i),
                                        new(DocumentTextNodeKind.Row, row), new(DocumentTextNodeKind.Cell, column)];
                                    foreach (DocumentTextBlock child in Blocks(cell.Blocks, section, nested, partName)) { yield return child; }
                                    column += cell.Span;
                                }
                            }
                            break;
                        default: throw new NotSupportedException("Unknown Word block content.");
                    }
                }
            }
        }
    }
}
