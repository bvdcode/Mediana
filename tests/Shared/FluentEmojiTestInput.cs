// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Documents.Word;
using Mediana.Drawing;
using Mediana.Drawing.Emoji.Fluent;
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
            WordLayoutOptions options = new() { InlineGraphicProvider = shared };
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
