// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Documents.OpenXml;
using Mediana.Documents.Presentation;
using Mediana.Drawing;
using Mediana.Tests;
using Xunit;

namespace Mediana.Documents.Tests
{
    public class PresentationTextGeometryTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void MissingSizeDoesNotPreventSlideAndNotesText(bool strict, bool streamInput)
        {
            byte[] input = DocumentPackageTestData.PresentationNotes(strict: strict, slideSize: "");
            using MemoryStream stream = new(input);
            using PresentationDocument document = streamInput ? PresentationReader.Open(stream) : PresentationReader.Open(input);
            Assert.Equal(2, document.Slides.Count);
            DocumentTextOptions options = new() { IncludeSpeakerNotes = true, PartCount = 1 };
            DocumentTextBlock[] blocks = document.ReadText(options).ToArray();
            Assert.Equal(new[] { "Slide", "Заметки 😀 é\n終" }, blocks.Select(block => block.Text));
            Assert.Equal(DocumentTextSourceKind.Body, blocks[0].SourceKind);
            Assert.Equal(DocumentTextSourceKind.SpeakerNotes, blocks[1].SourceKind);
            Assert.Equal("ppt/notes/one.xml", blocks[1].PartName);
            Assert.Equal(DocumentTextNodeKind.SpeakerNotes, blocks[1].Path[1].Kind);
            Assert.All(blocks, block =>
            {
                Assert.Equal(0, block.PartIndex);
                Assert.Equal(DocumentTextStructure.Declared, block.Structure);
            });
            Assert.Equal("Slide", document.ReadText(options with { PartCount = null }).First().Text);
            using CancellationTokenSource cancellation = new();
            using IEnumerator<DocumentTextBlock> reader = document.ReadText(options, cancellation.Token).GetEnumerator();
            Assert.True(reader.MoveNext());
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => reader.MoveNext());
            Assert.Throws<InvalidDataException>(() => document.ReadText(options with { MaximumBlocks = 1 }).ToArray());
        }

        [Theory]
        [InlineData("")]
        [InlineData("<p:sldSz/>")]
        [InlineData("<p:sldSz cx='0' cy='100'/>")]
        [InlineData("<p:sldSz cx='100' cy='-1'/>")]
        [InlineData("<p:sldSz cx='100'/>")]
        [InlineData("<p:sldSz cx='NaN' cy='100'/>")]
        [InlineData("<p:sldSz cx='100' cy='invalid'/>")]
        public async Task UnusableGeometryFailsOnlyWhenDimensionsOrRenderingAreRequested(string slideSize)
        {
            byte[] input = DocumentPackageTestData.PresentationNotes(slideSize: slideSize);
            using PresentationDocument document = PresentationReader.Open(input);
            Assert.Equal("Slide", document.ReadText().First().Text);
            Assert.Throws<InvalidDataException>(() => document.Width);
            Assert.Throws<InvalidDataException>(() => document.Height);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                using PixelBuffer pixels = await PresentationRenderer.RenderAsync(document, 0, allocator: allocator);
            });
            Assert.Equal(0, allocator.AllocatedBytes);
            using OpenXmlPackage package = OpenXmlPackage.Open(input);
            PresentationPageDecoder decoder = new();
            Assert.Throws<InvalidDataException>(() => decoder.ReadInfo(package, package.MainPart()));
            Assert.Equal(2, document.ReadText(new() { IncludeSpeakerNotes = true, PartCount = 1 }).Count());
        }
    }
}
