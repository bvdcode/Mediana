// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Presentation;
using Mediana.Documents.Word;
using Mediana.Tests;
using System.Xml;
using Xunit;

namespace Mediana.Documents.Tests
{
    public class OfficeTextStoriesTests
    {
        [Fact]
        public void UnicodeAndProvenanceSurviveStrictAndTransitionalPackages() => DocumentReadersTestInput.VerifyOfficeStories();

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void WordVariantsInheritIntoSelectedSectionsAndMatchParsedDocument(bool strict)
        {
            byte[] input = DocumentPackageTestData.WordStories(strict: strict);
            DocumentTextOptions options = new() { IncludeHeaders = true, IncludeFooters = true, StartPartIndex = 1, PartCount = 1 };
            DocumentTextBlock[] actual = WordTextReader.Read(input, options).ToArray();
            Assert.Equal(new[] { "Body 2", "Заголовок 😀 é\t終\nline", "First", "Even", "Footer" }, actual.Select(block => block.Text));
            Assert.All(actual, block => { Assert.Equal(1, block.PartIndex); Assert.Equal(new(DocumentTextNodeKind.Section, 1), block.Path[0]); });
            DocumentTextBlock[] parsed = WordReader.Read(input).ReadText(options).ToArray();
            Assert.Equal(actual.Length, parsed.Length);
            for (int i = 0; i < actual.Length; i++)
            {
                Assert.Equal(actual[i].Text, parsed[i].Text);
                Assert.Equal(actual[i].SourceKind, parsed[i].SourceKind);
                Assert.Equal(actual[i].HeaderFooterVariant, parsed[i].HeaderFooterVariant);
                Assert.Equal(actual[i].PartName, parsed[i].PartName);
                Assert.Equal(actual[i].Path, parsed[i].Path);
            }
        }

        [Fact]
        public void WordExplicitVariantOverridesOnlyThatInheritedVariant()
        {
            byte[] input = DocumentPackageTestData.WordStories(secondReferences: "<w:headerReference w:type='default' r:id='first'/>");
            DocumentTextBlock[] blocks = WordTextReader.Read(input, new() { IncludeHeaders = true, StartPartIndex = 1 }).ToArray();
            Assert.Equal(new[] { "Body 2", "First", "First", "Even" }, blocks.Select(block => block.Text));
            Assert.Equal("word/headers/first.xml", blocks[1].PartName);
            Assert.Equal(DocumentHeaderFooterVariant.Default, blocks[1].HeaderFooterVariant);
            Assert.Equal(DocumentHeaderFooterVariant.First, blocks[2].HeaderFooterVariant);
        }

        [Fact]
        public void DisabledWordHeadersAndEarlyStopDoNotReadBrokenPart()
        {
            byte[] input = DocumentPackageTestData.WordStories("<broken");
            Assert.Equal(new[] { "Body 1", "Body 2" }, WordTextReader.Read(input).Select(block => block.Text));
            Assert.Equal(new[] { "Body 1", "Footer" }, WordTextReader.Read(input, new() { IncludeFooters = true, PartCount = 1 }).Select(block => block.Text));
            Assert.Equal("Body 1", WordTextReader.Read(input, new() { IncludeHeaders = true }).First().Text);
            Assert.Throws<XmlException>(() => WordTextReader.Read(input, new() { IncludeHeaders = true }).ToArray());
        }

        [Fact]
        public void WordStopsWithinHeaderAndPreservesNestedStructureWithoutImages()
        {
            const string header = "<w:p><w:r><w:rPr><w:rFonts w:ascii='Missing font'/></w:rPr><w:t>Title</w:t><w:drawing><broken/></w:drawing></w:r></w:p>"
                + "<w:tbl><w:tr><w:trPr><w:gridBefore w:val='2'/></w:trPr><w:tc><w:tcPr><w:gridSpan w:val='3'/></w:tcPr><w:p><w:r><w:t>Cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>"
                + "<w:p><w:r><w:t>Outer</w:t></w:r><w:txbxContent><w:p><w:r><w:t>Inner</w:t></w:r></w:p></w:txbxContent></w:p>";
            DocumentTextBlock[] blocks = WordTextReader.Read(DocumentPackageTestData.WordStories(header), new() { IncludeHeaders = true, PartCount = 1 }).ToArray();
            Assert.Equal(new[] { "Body 1", "Title", "Cell", "Outer", "Inner", "First", "Even" }, blocks.Select(block => block.Text));
            Assert.Equal(new(DocumentTextNodeKind.Cell, 2), blocks[2].Path[^2]);
            Assert.Contains(blocks[4].Path, segment => segment.Kind == DocumentTextNodeKind.TextBox);
            Assert.All(blocks.Skip(1), block => Assert.Equal(DocumentTextSourceKind.Header, block.SourceKind));
            byte[] broken = DocumentPackageTestData.WordStories("<w:p><w:r><w:t>First header</w:t></w:r></w:p><w:p><w:r><w:sym w:char='ABCD'/></w:r></w:p>");
            Assert.Equal("First header", WordTextReader.Read(broken, new() { IncludeHeaders = true }).Take(2).Last().Text);
            Assert.Throws<NotSupportedException>(() => WordTextReader.Read(broken, new() { IncludeHeaders = true }).ToArray());
        }

        [Theory]
        [InlineData("<w:headerReference w:type='invalid' r:id='h'/>")]
        [InlineData("<w:headerReference w:type='default' r:id='missing'/>")]
        [InlineData("<w:headerReference w:type='default' r:id='f'/>")]
        [InlineData("<w:headerReference w:type='default' r:id='h'/><w:headerReference w:type='default' r:id='first'/>")]
        public void InvalidEnabledWordReferencesFailExplicitly(string references)
        {
            byte[] input = DocumentPackageTestData.WordStories(firstReferences: references);
            Assert.Equal(2, WordTextReader.Read(input).Count());
            Assert.Throws<InvalidDataException>(() => WordTextReader.Read(input, new() { IncludeHeaders = true }).ToArray());
        }

        [Fact]
        public void NotesAreOptionalAndEarlyStopDoesNotReadTheirPart()
        {
            using PresentationDocument document = PresentationReader.Open(DocumentPackageTestData.PresentationNotes("<broken"));
            Assert.Equal("Slide", document.ReadText(new() { PartCount = 1 }).Single().Text);
            Assert.Equal("Slide", document.ReadText(new() { IncludeSpeakerNotes = true }).First().Text);
            Assert.Throws<XmlException>(() => document.ReadText(new() { IncludeSpeakerNotes = true, PartCount = 1 }).ToArray());
        }

        [Fact]
        public void NotesStopWithinPartAndRetainTableAndTextBoxPaths()
        {
            const string notes = "<p:sp><p:txBody><a:p><a:r><a:t>Note</a:t></a:r></a:p></p:txBody></p:sp>"
                + "<p:graphicFrame><a:tbl><a:tr><a:tc><a:txBody><a:p><a:r><a:t>Cell</a:t></a:r></a:p></a:txBody></a:tc></a:tr></a:tbl></p:graphicFrame>";
            using PresentationDocument document = PresentationReader.Open(DocumentPackageTestData.PresentationNotes(notes));
            DocumentTextBlock[] blocks = document.ReadText(new() { IncludeSpeakerNotes = true, PartCount = 1 }).ToArray();
            Assert.Equal(new[] { "Slide", "Note", "Cell" }, blocks.Select(block => block.Text));
            Assert.Contains(blocks[1].Path, segment => segment.Kind == DocumentTextNodeKind.TextBox);
            Assert.Equal(new[] { DocumentTextNodeKind.Slide, DocumentTextNodeKind.SpeakerNotes, DocumentTextNodeKind.Table, DocumentTextNodeKind.Row, DocumentTextNodeKind.Cell, DocumentTextNodeKind.Paragraph }, blocks[2].Path.Select(segment => segment.Kind));
            using PresentationDocument broken = PresentationReader.Open(DocumentPackageTestData.PresentationNotes(notes + "<broken"));
            Assert.Equal("Note", broken.ReadText(new() { IncludeSpeakerNotes = true }).Take(2).Last().Text);
            Assert.Throws<XmlException>(() => broken.ReadText(new() { IncludeSpeakerNotes = true, PartCount = 1 }).ToArray());
        }

        [Theory]
        [InlineData("dt")]
        [InlineData("hdr")]
        [InlineData("ftr")]
        [InlineData("sldNum")]
        [InlineData("sldImg")]
        public void NotesExcludeAdministrativePlaceholders(string kind)
        {
            string notes = "<p:sp><p:nvSpPr><p:nvPr><p:ph type='" + kind + "'/></p:nvPr></p:nvSpPr><p:txBody><a:p><a:r><a:t>Administrative</a:t></a:r></a:p></p:txBody></p:sp>";
            using PresentationDocument document = PresentationReader.Open(DocumentPackageTestData.PresentationNotes(notes));
            Assert.Equal("Slide", document.ReadText(new() { IncludeSpeakerNotes = true, PartCount = 1 }).Single().Text);
        }

        [Fact]
        public void NotesRelationshipsRejectDuplicateAndExternalTargets()
        {
            using PresentationDocument duplicate = PresentationReader.Open(DocumentPackageTestData.PresentationNotes(notesRelationship:
                DocumentPackageTestData.Relations(("one", "notesSlide", "../notes/one.xml"), ("two", "notesSlide", "../notes/one.xml"))));
            Assert.Throws<InvalidDataException>(() => duplicate.ReadText(new() { IncludeSpeakerNotes = true, PartCount = 1 }).ToArray());
            using PresentationDocument external = PresentationReader.Open(DocumentPackageTestData.PresentationNotes(notesRelationship:
                DocumentPackageTestData.Relations(("one", "notesSlide", "https://example.com/notes.xml")).Replace("Target=", "TargetMode='External' Target=", StringComparison.Ordinal)));
            Assert.Throws<NotSupportedException>(() => external.ReadText(new() { IncludeSpeakerNotes = true, PartCount = 1 }).ToArray());
        }

        [Fact]
        public void CancellationAndBudgetsCoverAllSources()
        {
            byte[] word = DocumentPackageTestData.WordStories();
            using PresentationDocument deck = PresentationReader.Open(DocumentPackageTestData.PresentationNotes());
            IDocumentTextSource[] sources = [new WordTextReader(word), WordReader.Read(word), deck];
            foreach (IDocumentTextSource source in sources)
            {
                DocumentTextOptions options = new() { IncludeHeaders = true, IncludeFooters = true, IncludeSpeakerNotes = true, PartCount = 1 };
                using CancellationTokenSource cancellation = new();
                using IEnumerator<DocumentTextBlock> enumerator = source.ReadText(options, cancellation.Token).GetEnumerator();
                Assert.True(enumerator.MoveNext());
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => enumerator.MoveNext());
                Assert.Throws<OperationCanceledException>(() => source.ReadText(options, cancellation.Token).First());
                Assert.Throws<InvalidDataException>(() => source.ReadText(options with { MaximumBlocks = 1 }).ToArray());
                Assert.Throws<InvalidDataException>(() => source.ReadText(options with { MaximumCharacters = 10 }).ToArray());
            }
        }
    }
}
