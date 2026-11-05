// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Documents.OpenXml;
using Mediana.Documents.Word;
using Mediana.Drawing;

namespace Mediana.Tests
{
    internal static class WordRenderTestInput
    {
        internal const string Fonts = "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Canada1500\"/></w:rPr></w:rPrDefault></w:docDefaults>";
        internal const string SmallPage = "<w:sectPr><w:pgSz w:w=\"2400\" w:h=\"2400\"/><w:pgMar w:top=\"200\" w:bottom=\"200\" w:left=\"200\" w:right=\"200\"/></w:sectPr>";

        internal static async Task VerifyAsync()
        {
            byte[] encoded = WordTestInput.Create("<w:p><w:r><w:t>First</w:t><w:br w:type=\"page\"/><w:t>Second</w:t></w:r></w:p>", Fonts, page: SmallPage);
            WordDocument document = WordReader.Read(encoded);
            WordLayout layout = WordRenderer.Layout(document);
            Require(layout.PageCount == 2 && layout.Warnings.Count == 0);
            encoded.AsSpan().Clear();
            MediaBufferAllocator allocator = new(512 * 1024);
            byte[] first;
            using (PixelBuffer pixels = await WordRenderer.RenderAsync(layout, 1, new() { Dpi = 72 }, allocator: allocator))
            {
                Require(pixels.Width == 120 && pixels.Height == 120 && pixels[0, 0] == new Rgba32(255, 255, 255));
                Require(pixels.Pixels.Span.Contains((byte)0));
                first = pixels.Pixels.ToArray();
                using VideoFrame frame = pixels.PublishVideoFrame();
                using MemoryStream png = new();
                await PngEncoder.EncodeAsync(frame, png, allocator);
                using VideoFrame decoded = await new PngImageDecoder().DecodeAsync(png.ToArray(), new(), allocator);
                Require(decoded.Data.Span.SequenceEqual(first));
            }
            Require(allocator.AllocatedBytes == 0);
            await Task.WhenAll(Task.Run(() => RenderAgain(1)), Task.Run(() => RenderAgain(2)));

            OpenXmlImageDecoder adapter = new([new WordPageDecoder()]);
            encoded = WordTestInput.Create("<w:p><w:r><w:t>Registered</w:t></w:r></w:p>", Fonts, page: SmallPage);
            Require(adapter.CanDecode(encoded));
            Mediana.Core.Metadata.ImageInfo info = await adapter.ReadInfoAsync(encoded);
            Require(info.FormatId == "docx" && info.Width == 120 && info.PageCount is null);
            using (VideoFrame frame = await adapter.DecodeAsync(encoded, new(), allocator))
            {
                Require(frame.Format.Width == 160 && frame.Format.Height == 160);
            }
            Require(allocator.AllocatedBytes == 0);

            async Task RenderAgain(int page)
            {
                MediaBufferAllocator budget = new(512 * 1024);
                using (PixelBuffer pixels = await WordRenderer.RenderAsync(layout, page, new() { Dpi = 72 }, allocator: budget))
                {
                    if (page == 1) { Require(pixels.Pixels.Span.SequenceEqual(first)); }
                    else { Require(!pixels.Pixels.Span.SequenceEqual(first)); }
                }
                Require(budget.AllocatedBytes == 0);
            }
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("Word rendering contract failed."); }
        }
    }
}
