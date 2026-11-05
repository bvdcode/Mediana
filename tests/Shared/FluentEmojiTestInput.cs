// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Documents.Word;
using Mediana.Drawing;
using Mediana.Drawing.Emoji.Fluent;
using Mediana.Drawing.Fonts;
using Mediana.Images.Svg;
using Mediana.Documents.Pdf;
using System.Numerics;

namespace Mediana.Tests
{
    internal static class FluentEmojiTestInput
    {
        internal static async Task VerifyAsync()
        {
            using FluentEmojiProvider provider = new(new() { MaximumCachedGraphics = 1 });
            Require(provider.Count == 1595);
            foreach (string sequence in new[] { "😀", "👍", "👩‍💻", "❤️", "1️⃣", "🏳️‍🌈", "👨‍🚀" })
            {
                Require(provider.Resolve(sequence) is not null);
            }
            foreach (string sequence in new[] { "", "A", "❤", "❤︎", "😀︎", "😀😀", "👍🏽", "🇺🇸", "😀️️", "\ud83d\ufe0f\ude00" })
            {
                Require(provider.Resolve(sequence) is null);
            }
            Require(ReferenceEquals(provider.Resolve("👩‍💻"), provider.Resolve("👩‍💻")));
            InlineGraphic retained = provider.Resolve("😀")!;
            provider.Resolve("👍");
            provider.Dispose();
            MediaBufferAllocator allocator = new(4096);
            using (PixelBuffer pixels = new(32, 32, allocator))
            {
                retained.Draw(new RasterCanvas(pixels), Matrix3x2.CreateScale(32));
                Require(ColoredPixels(pixels) > 100);
            }
            Require(allocator.AllocatedBytes == 0);

            using (FluentEmojiProvider layers = new())
            {
                MediaBufferAllocator workspace = new(128 * 1024);
                using (PixelBuffer pixels = new(32, 32, workspace))
                {
                    layers.Resolve("🎅")!.Draw(new RasterCanvas(pixels), Matrix3x2.CreateScale(32));
                    Require(ColoredPixels(pixels) > 100 && workspace.AllocatedBytes == 4096);
                }
                Require(workspace.AllocatedBytes == 0);
            }

            using FluentEmojiProvider shared = new();
            WordLayoutOptions options = new() { Text = new() { InlineGraphicProvider = shared } };
            WordLayout whole = WordRenderer.Layout(Read("<w:p><w:r><w:t>A👩‍💻B</w:t></w:r></w:p>"), options);
            WordLayout split = WordRenderer.Layout(Read("<w:p><w:r><w:t>A👩</w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>‍</w:t></w:r><w:r><w:t>💻B</w:t></w:r></w:p>"), options);
            Require(whole.Warnings.Count == 0 && split.Warnings.Count == 0);
            byte[] expected;
            MediaBufferAllocator pageBudget = new(300_000);
            using (PixelBuffer pixels = await WordRenderer.RenderAsync(whole, 1, new() { Dpi = 72, Supersampling = 1 }, allocator: pageBudget))
            {
                expected = pixels.Pixels.ToArray();
                Require(ColoredPixels(pixels) > 20);
            }
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                MediaBufferAllocator budget = new(300_000);
                using (PixelBuffer pixels = await WordRenderer.RenderAsync(split, 1, new() { Dpi = 72, Supersampling = 1 }, allocator: budget))
                {
                    Require(pixels.Pixels.Span.SequenceEqual(expected));
                }
                Require(budget.AllocatedBytes == 0);
            })));
            Require(pageBudget.AllocatedBytes == 0);
        }

        internal static async Task VerifySharedTextAsync()
        {
            using FluentEmojiProvider emoji = new();
            List<FontRequest> requests = [];
            TextOptions text = new()
            {
                FontResolver = request => { requests.Add(request); return BundledFontPrograms.GetFont("Aileron", request.Bold, request.Italic); },
                InlineGraphicProvider = emoji
            };
            WordLayout word = WordRenderer.Layout(Read("<w:p><w:r><w:t>A👩‍💻B</w:t></w:r></w:p>"), new() { Text = text });
            using PixelBuffer wordPixels = await WordRenderer.RenderAsync(word, 1, new() { Dpi = 72, Supersampling = 1 });
            Require(ColoredPixels(wordPixels) > 20);
            SvgReaderOptions options = new() { Text = text };
            const string start = "<svg xmlns='http://www.w3.org/2000/svg' width='120' height='60'><text x='4' y='44' font-family='Aileron' font-size='32'>";
            const string end = "</text></svg>";
            SvgDocument whole = SvgReader.Read(start + "A👩‍💻B" + end, options);
            SvgDocument split = SvgReader.Read(start + "A<tspan>👩</tspan><tspan>‍</tspan><tspan>💻</tspan>B" + end, options);
            Require(whole.UnsupportedFeatures.Count == 0 && split.UnsupportedFeatures.Count == 0);
            emoji.Dispose();
            MediaBufferAllocator allocator = new(120 * 60 * 4);
            using (PixelBuffer pixels = SvgRenderer.Render(whole, new() { Supersampling = 1 }, allocator: allocator))
            using (PixelBuffer reference = SvgRenderer.Render(split, new() { Supersampling = 1 }))
            {
                Require(ColoredPixels(pixels) > 100);
                Require(pixels.Pixels.Span.SequenceEqual(reference.Pixels.Span));
                Require(allocator.AllocatedBytes == 120 * 60 * 4);
            }
            Require(allocator.AllocatedBytes == 0);
            byte[] pdf = PdfTestDocument.Create("BT /F 20 Tf 10 40 Td (A) Tj ET", resources: "/Font << /F 5 0 R >>",
                extraObjects: PdfTestDocument.Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-BoldOblique >>"));
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(pdf), new() { Text = text });
            using PixelBuffer pdfPixels = PdfRenderer.Render(document, 1, new() { Dpi = 72, Supersampling = 1 });
            Require(pdfPixels.Pixels.Span.Contains((byte)0));
            Require(requests.Contains(new FontRequest("Helvetica", true, true)));
        }

        internal static int ColoredPixels(PixelBuffer pixels)
        {
            int count = 0;
            ReadOnlySpan<byte> bytes = pixels.Pixels.Span;
            for (int offset = 0; offset < bytes.Length; offset += 4)
            {
                if (bytes[offset + 3] != 0 && (bytes[offset] != bytes[offset + 1] || bytes[offset + 1] != bytes[offset + 2])) { count++; }
            }
            return count;
        }

        internal static WordDocument Read(string body) => WordReader.Read(WordTestInput.Create(body, WordRenderTestInput.Fonts, page: WordRenderTestInput.SmallPage));
        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidOperationException("Fluent emoji rendering assertion failed."); }
        }
    }
}
