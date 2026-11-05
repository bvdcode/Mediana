// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Word;
using Xunit;

namespace Mediana.Tests
{
    public class WordReaderTests
    {
        [Fact]
        public void ReadsMovedMainPartAndUniversalFontSizes() => WordTestInput.Verify();

        [Fact]
        public void CascadesStylesAndDirectFormattingWithoutApplyingParagraphMarkToText()
        {
            const string styles = """
                <w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Canada1500"/><w:sz w:val="20"/></w:rPr></w:rPrDefault></w:docDefaults>
                <w:style w:type="paragraph" w:default="1" w:styleId="base"><w:pPr><w:spacing w:before="100" w:after="200"/></w:pPr><w:rPr><w:b/></w:rPr></w:style>
                <w:style w:type="paragraph" w:styleId="child"><w:basedOn w:val="base"/><w:pPr><w:spacing w:after="300"/></w:pPr><w:rPr><w:i/></w:rPr></w:style>
                <w:style w:type="character" w:styleId="plain"><w:rPr><w:b/></w:rPr></w:style>
                """;
            WordDocument document = WordReader.Read(WordTestInput.Create("""
                <w:p><w:pPr><w:pStyle w:val="child"/><w:rPr><w:sz w:val="40"/></w:rPr></w:pPr>
                <w:r><w:rPr><w:rStyle w:val="plain"/><w:i w:val="0"/></w:rPr><w:t>Text</w:t></w:r></w:p>
                """, styles));
            WordParagraph paragraph = Assert.IsType<WordParagraph>(Assert.Single(document.Sections.Single().Blocks));
            WordText text = Assert.IsType<WordText>(Assert.Single(paragraph.Content));
            Assert.Equal(5, paragraph.Format.Before);
            Assert.Equal(15, paragraph.Format.After);
            Assert.Equal(20, paragraph.MarkFormat.Size);
            Assert.Equal(10, text.Format.Size);
            Assert.False(text.Format.Bold);
            Assert.False(text.Format.Italic);
            Assert.Equal("Canada1500", text.Format.Family);
        }

        [Fact]
        public void MissingPageSizeRequiresConfiguredDefaultAndReportsIt()
        {
            byte[] input = WordTestInput.Create("<w:p/>", page: "<w:sectPr/>");
            Assert.Throws<InvalidDataException>(() => WordReader.Read(input));
            WordDocument document = WordReader.Read(input, new() { DefaultPageSize = WordPageSize.A4 });
            Assert.Equal(WordPageSize.A4, document.PageSize);
            Assert.Single(document.Warnings);
        }

        [Fact]
        public void PreservesFractionalIndentWithExplicitRecovery()
        {
            byte[] input = WordTestInput.Create("<w:p><w:pPr><w:ind w:left=\"283.4645\"/></w:pPr></w:p>");
            Assert.Throws<InvalidDataException>(() => WordReader.Read(input));
            WordDocument document = WordReader.Read(input, new() { AllowFractionalMeasurements = true });
            Assert.Equal(14.173225, Assert.IsType<WordParagraph>(document.Sections.Single().Blocks[0]).Format.Left, 10);
            Assert.Single(document.Warnings);
        }

        [Fact]
        public void ReadsNumberingCountersAndRestart()
        {
            const string numbering = """
                <w:abstractNum w:abstractNumId="1"><w:lvl w:ilvl="0"><w:start w:val="3"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/>
                <w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr></w:lvl></w:abstractNum>
                <w:num w:numId="2"><w:abstractNumId w:val="1"/></w:num>
                """;
            const string paragraph = "<w:p><w:pPr><w:numPr><w:numId w:val=\"2\"/></w:numPr></w:pPr><w:r><w:t>List</w:t></w:r></w:p>";
            WordDocument document = WordReader.Read(WordTestInput.Create(paragraph + paragraph, numbering: numbering));
            WordParagraph first = Assert.IsType<WordParagraph>(document.Sections.Single().Blocks[0]), second = Assert.IsType<WordParagraph>(document.Sections.Single().Blocks[1]);
            Assert.Equal("3.", Assert.IsType<WordText>(first.Content[0]).Text);
            Assert.Equal("4.", Assert.IsType<WordText>(second.Content[0]).Text);
            Assert.Equal(36, first.Format.Left);
            Assert.Equal(-18, first.Format.FirstLine);
        }

        [Fact]
        public void ReadsTableGridCellsAndMargins()
        {
            WordDocument document = WordReader.Read(WordTestInput.Create("""
                <w:tbl><w:tblPr><w:tblCellMar><w:left w:w="100"/><w:right w:w="100"/></w:tblCellMar></w:tblPr>
                <w:tblGrid><w:gridCol w:w="3000"/><w:gridCol w:w="4000"/></w:tblGrid><w:tr>
                <w:tc><w:p><w:r><w:t>A</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>B</w:t></w:r></w:p></w:tc>
                </w:tr></w:tbl>
                """));
            WordTable table = Assert.IsType<WordTable>(document.Sections.Single().Blocks[0]);
            Assert.Equal(new double[] { 150, 200 }, table.Columns);
            Assert.Equal(2, Assert.Single(table.Rows).Cells.Count);
            Assert.Equal(5, table.Rows[0].Cells[0].Left);
        }

        [Fact]
        public void RejectsStyleCyclesAndUnrecognizedPaintingContent()
        {
            const string styles = "<w:style w:type=\"paragraph\" w:styleId=\"a\"><w:basedOn w:val=\"a\"/></w:style>";
            Assert.Throws<InvalidDataException>(() => WordReader.Read(WordTestInput.Create("<w:p><w:pPr><w:pStyle w:val=\"a\"/></w:pPr></w:p>", styles)));
            Assert.Throws<NotSupportedException>(() => WordReader.Read(WordTestInput.Create("<w:p><w:r><w:object/></w:r></w:p>")));
        }

        [Fact]
        public void ResolvesDuplicatedAndMissingStylesAccordingToTheStandard()
        {
            const string styles = """
                <w:style w:type="paragraph" w:styleId="same"><w:rPr><w:b/></w:rPr></w:style>
                <w:style w:type="paragraph" w:styleId="same" w:default="1"><w:rPr><w:i/></w:rPr></w:style>
                """;
            WordDocument document = WordReader.Read(WordTestInput.Create("""
                <w:p><w:pPr><w:pStyle w:val="same"/></w:pPr><w:r><w:t>A</w:t></w:r></w:p>
                <w:p><w:r><w:t>B</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="missing"/></w:pPr><w:r><w:t>C</w:t></w:r></w:p>
                """, styles));
            WordText first = Assert.IsType<WordText>(Assert.IsType<WordParagraph>(document.Sections.Single().Blocks[0]).Content[0]);
            WordText second = Assert.IsType<WordText>(Assert.IsType<WordParagraph>(document.Sections.Single().Blocks[1]).Content[0]);
            WordText third = Assert.IsType<WordText>(Assert.IsType<WordParagraph>(document.Sections.Single().Blocks[2]).Content[0]);
            Assert.True(first.Format.Bold);
            Assert.False(first.Format.Italic);
            Assert.True(second.Format.Italic);
            Assert.False(third.Format.Bold);
            Assert.False(third.Format.Italic);
            Assert.Equal(2, document.Warnings.Count);
        }

        [Fact]
        public void AppliesHangingIndentPriorityAndReadsOnlyQualifiedBooleanAttributes()
        {
            WordDocument document = WordReader.Read(WordTestInput.Create("""
                <w:p><w:pPr><w:ind w:firstLine="300" w:hanging="200"/></w:pPr>
                <w:r><w:rPr><w:b xmlns:other="urn:other" other:val="0"/></w:rPr><w:t>Text</w:t></w:r></w:p>
                """));
            WordParagraph paragraph = Assert.IsType<WordParagraph>(document.Sections.Single().Blocks[0]);
            Assert.Equal(-10, paragraph.Format.FirstLine);
            Assert.True(Assert.IsType<WordText>(paragraph.Content[0]).Format.Bold);
        }

        [Fact]
        public void EnforcesTextAndBlockBudgetsAndCancellation()
        {
            byte[] input = WordTestInput.Create("<w:p><w:r><w:t>Text</w:t></w:r></w:p><w:p/>");
            Assert.Throws<InvalidDataException>(() => WordReader.Read(input, new() { MaximumCharacters = 3 }));
            Assert.Throws<InvalidDataException>(() => WordReader.Read(input, new() { MaximumBlocks = 1 }));
            Assert.Throws<OperationCanceledException>(() => WordReader.Read(input, cancellationToken: new(true)));
        }
    }
}
