// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Documents.OpenXml;
using Mediana.Documents.Word;
using Mediana.Drawing;
using Mediana.Drawing.Fonts;

namespace Mediana.Tests
{
    public class WordRenderTests
    {
        [Fact]
        public Task PublicApiAndRegisteredPagesRenderWithoutRetainingPackageOrLeakingPixels() => WordRenderTestInput.VerifyAsync();

        [Fact]
        public async Task TableShadingUsesPhysicalCoordinatesAndPageSelection()
        {
            WordDocument document = Read("""
                <w:tbl><w:tblGrid><w:gridCol w:w="800"/><w:gridCol w:w="800"/></w:tblGrid>
                <w:tr><w:trPr><w:trHeight w:val="400" w:hRule="exact"/></w:trPr>
                <w:tc><w:tcPr><w:shd w:fill="FF0000"/></w:tcPr><w:p/></w:tc>
                <w:tc><w:tcPr><w:shd w:fill="0000FF"/></w:tcPr><w:p/></w:tc></w:tr></w:tbl>
                """);
            MediaBufferAllocator allocator = new(100_000);
            using (PixelBuffer pixels = await WordRenderer.RenderAsync(document, 1, new() { Dpi = 72, Supersampling = 1 }, allocator: allocator))
            {
                Assert.Equal(new Rgba32(255, 0, 0), pixels[15, 15]);
                Assert.Equal(new Rgba32(0, 0, 255), pixels[55, 15]);
                Assert.Equal(new Rgba32(255, 255, 255), pixels[95, 15]);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task LargeInlinePngIsSampledWithinTheSharedPageBudget()
        {
            const int size = 1024;
            byte[] scanlines = new byte[(size * 4 + 1) * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int offset = y * (size * 4 + 1) + 1 + x * 4;
                    scanlines[offset] = 255;
                    scanlines[offset + 3] = 255;
                }
            }
            byte[] image = PngDecodeTestData.File(PngDecodeTestData.Chunk("IHDR", PngDecodeTestData.Header(size, size)),
                PngDecodeTestData.Chunk("sRGB", [0]), PngDecodeTestData.Chunk("IDAT", PngDecodeTestData.Compressed(scanlines)), PngDecodeTestData.Chunk("IEND", []));
            const string body = """
                <w:p><w:r><w:drawing><wp:inline xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing">
                  <wp:extent cx="254000" cy="254000"/><wp:docPr id="1" name="Picture"/>
                  <a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                    <pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:nvPicPr><pic:cNvPr id="0" name="Picture"/><pic:cNvPicPr/></pic:nvPicPr>
                    <pic:blipFill><a:blip r:embed="picture"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
                    <pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="254000" cy="254000"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>
                    </pic:pic></a:graphicData></a:graphic>
                </wp:inline></w:drawing></w:r></w:p>
                """;
            WordDocument document = WordReader.Read(WordTestInput.Create(body, WordRenderTestInput.Fonts, page: WordRenderTestInput.SmallPage, picture: image));
            MediaBufferAllocator allocator = new(400_000);
            using (PixelBuffer pixels = await WordRenderer.RenderAsync(document, 1, new() { Dpi = 72, Supersampling = 1 }, allocator: allocator))
            {
                Assert.Equal(new Rgba32(255, 0, 0), pixels[12, 12]);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ExactCallerFontsAreResolvedOncePerFaceAndRemainIndependentOfCallerStorage()
        {
            byte[] program = BundledFontPrograms.GetFont("Aileron", true, false).ToArray();
            WordDocument document = Read("<w:p><w:r><w:rPr><w:rFonts w:ascii=\"Custom\"/><w:b/></w:rPr><w:t>Repeated repeated</w:t></w:r></w:p>");
            int calls = 0;
            WordLayout layout = WordRenderer.Layout(document, new()
            {
                FontResolver = request => { calls++; return request.Family == "Custom" ? program : BundledFontPrograms.GetFont("Canada1500"); }
            });
            Assert.Equal(2, calls);
            Assert.Single(layout.Warnings);
            program.AsSpan().Clear();
            using PixelBuffer result = await WordRenderer.RenderAsync(layout, 1);
            Assert.True(result.Pixels.Span.Contains((byte)0));
        }

        [Fact]
        public async Task FailuresCancellationAndInvalidPageDoNotLeaveLiveBuffers()
        {
            WordLayout layout = WordRenderer.Layout(Read("<w:p><w:r><w:t>Text</w:t></w:r></w:p>"));
            MediaBufferAllocator budget = new(60_000);
            await Assert.ThrowsAsync<MediaMemoryLimitException>(async () => await WordRenderer.RenderAsync(layout, 1, allocator: budget));
            Assert.Equal(0, budget.AllocatedBytes);
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await WordRenderer.RenderAsync(layout, 1, cancellationToken: new(true), allocator: budget));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await WordRenderer.RenderAsync(layout, 0, allocator: budget));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await WordRenderer.RenderAsync(layout, 2, allocator: budget));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await WordRenderer.RenderAsync(layout, 1, new() { MaximumSamples = 1 }, allocator: budget));
            Assert.Equal(0, budget.AllocatedBytes);
        }

        [Fact]
        public void FontSubstitutionAndLayoutBoundsAreExplicit()
        {
            WordDocument unavailable = Read("<w:p><w:r><w:rPr><w:rFonts w:ascii=\"Unavailable face\"/></w:rPr><w:t>Text</w:t></w:r></w:p>");
            Assert.Throws<NotSupportedException>(() => WordRenderer.Layout(unavailable));
            Assert.Single(WordRenderer.Layout(unavailable, new() { SubstituteMissingFonts = true }).Warnings);
            Assert.Throws<InvalidDataException>(() => WordRenderer.Layout(unavailable, new() { SubstituteMissingFonts = true, MaximumGlyphs = 2 }));
            Assert.Throws<InvalidDataException>(() => WordRenderer.Layout(Read("<w:p><w:r><w:br w:type=\"page\"/><w:t>Second</w:t></w:r></w:p>"), new() { MaximumPages = 1 }));
        }

        [Fact]
        public async Task WarningCallbackFailureOccursBeforePixelAllocation()
        {
            WordLayout layout = WordRenderer.Layout(WordReader.Read(WordTestInput.Create("<w:p/>")), new() { SubstituteMissingFonts = true });
            MediaBufferAllocator allocator = new(1024);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await WordRenderer.RenderAsync(layout, 1,
                new() { WarningHandler = _ => throw new InvalidOperationException("diagnostic") }, allocator: allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task MetadataDoesNotResolveFontsOrValidatePaintingContent()
        {
            OpenXmlImageDecoder decoder = new([new WordPageDecoder(layoutOptions: new()
            {
                FontResolver = _ => throw new InvalidOperationException("Font resolution is not part of probing.")
            })]);
            Assert.Throws<ArgumentException>(() => new OpenXmlImageDecoder([new WordPageDecoder(), new WordPageDecoder()]));
            Assert.False(decoder.CanDecode("not a package"u8));
            byte[] input = WordTestInput.Create("<w:p><w:r><w:object/></w:r></w:p>", page: WordRenderTestInput.SmallPage);
            Core.Metadata.ImageInfo info = await decoder.ReadInfoAsync(input);
            Assert.Equal(120, info.Width);
            Assert.Null(info.PageCount);
            MediaBufferAllocator allocator = new(100_000);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await decoder.DecodeAsync(input, new(), allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
            byte[] text = WordTestInput.Create("<w:p><w:r><w:t>Text</w:t></w:r></w:p>", page: WordRenderTestInput.SmallPage);
            Assert.Equal(120, (await decoder.ReadInfoAsync(text)).Height);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await decoder.DecodeAsync(text, new(), allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static WordDocument Read(string body) => WordReader.Read(WordTestInput.Create(body, WordRenderTestInput.Fonts, page: WordRenderTestInput.SmallPage));
    }
}
