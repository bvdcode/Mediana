// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Documents.Word;
using Mediana.Drawing;
using Mediana.Drawing.Emoji.Fluent;
using Mediana.Images.Svg;
using System.Numerics;

namespace Mediana.Tests
{
    public class FluentEmojiTests
    {
        [Fact]
        public Task WordSvgAndPdfUseTheSameLibraryTextResources() => FluentEmojiTestInput.VerifySharedTextAsync();

        [Fact]
        public Task SequencesSelectorsBorrowedCanvasOwnershipAndConcurrentWordPages() => FluentEmojiTestInput.VerifyAsync();

        [Fact]
        public async Task EveryBundledSequenceDrawsOnTheBorrowedCanvasAndReleasesItsWorkspace()
        {
            string[] sequences = await File.ReadAllLinesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Emoji", "sequences.txt"));
            using FluentEmojiProvider provider = new(new() { MaximumCachedGraphics = 2 });
            Assert.Equal(provider.Count, sequences.Length);
            MediaBufferAllocator allocator = new(128 * 1024);
            using (PixelBuffer pixels = new(32, 32, allocator))
            {
                foreach (string sequence in sequences)
                {
                    InlineGraphic graphic = Assert.IsAssignableFrom<InlineGraphic>(provider.Resolve(sequence));
                    pixels.Clear(default);
                    graphic.Draw(new RasterCanvas(pixels), Matrix3x2.CreateScale(32));
                    Assert.True(pixels.Pixels.Span.ContainsAnyExcept((byte)0), sequence);
                    Assert.Equal(4096, allocator.AllocatedBytes);
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void OpacityWorkspaceCannotBypassTheCanvasBudgetAndFailureReleasesReservations()
        {
            using FluentEmojiProvider provider = new();
            InlineGraphic graphic = Assert.IsAssignableFrom<InlineGraphic>(provider.Resolve("🎅"));
            MediaBufferAllocator allocator = new(4096);
            using (PixelBuffer pixels = new(32, 32, allocator))
            {
                Assert.Throws<MediaMemoryLimitException>(() => graphic.Draw(new RasterCanvas(pixels), Matrix3x2.CreateScale(32)));
                Assert.Equal(4096, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void CompleteEmojiIsOneLayoutUnitAndGraphicsAreBounded()
        {
            using FluentEmojiProvider provider = new();
            WordLayout layout = WordRenderer.Layout(FluentEmojiTestInput.Read("<w:p><w:r><w:rPr><w:sz w:val=\"180\"/></w:rPr><w:t>👩‍💻👩‍💻</w:t></w:r></w:p>"),
                new() { Text = new() { InlineGraphicProvider = provider } });
            WordInlineGraphicPaint[] graphics = Flatten(layout.Pages.SelectMany(page => page.Paint)).OfType<WordInlineGraphicPaint>().ToArray();
            Assert.Equal(2, graphics.Length);
            Assert.Equal(graphics[0].X, graphics[1].X);
            Assert.True(graphics[1].Top > graphics[0].Top || layout.PageCount > 1);
            Assert.Same(graphics[0].Graphic, graphics[1].Graphic);
            Assert.Throws<InvalidDataException>(() => WordRenderer.Layout(FluentEmojiTestInput.Read("<w:p><w:r><w:t>😀👍</w:t></w:r></w:p>"),
                new() { Text = new() { InlineGraphicProvider = provider, MaximumInlineGraphics = 1 } }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FluentEmojiProvider(new() { MaximumCachedGraphics = 0 }));
            Assert.Throws<OperationCanceledException>(() => provider.Resolve("😀", new(true)));
            provider.Dispose();
            Assert.Throws<ObjectDisposedException>(() => provider.Resolve("😀"));
        }

        [Fact]
        public void TextPresentationRemainsAnOutlineAndUnsupportedSequencesHaveDiagnostics()
        {
            using FluentEmojiProvider provider = new();
            WordLayout layout = WordRenderer.Layout(FluentEmojiTestInput.Read("<w:p><w:r><w:t>♥︎♥️</w:t></w:r></w:p>"),
                new() { Text = new() { InlineGraphicProvider = provider } });
            Assert.Single(Flatten(layout.Pages.SelectMany(page => page.Paint)).OfType<WordInlineGraphicPaint>());
            Assert.NotEmpty(Flatten(layout.Pages.SelectMany(page => page.Paint)).OfType<WordGlyphPaint>());
            Assert.Empty(layout.Warnings);
            WordLayout missing = WordRenderer.Layout(FluentEmojiTestInput.Read("<w:p><w:r><w:t>🇺🇸</w:t></w:r></w:p>"),
                new() { Text = new() { InlineGraphicProvider = provider, SubstituteMissingFonts = true } });
            Assert.Contains(missing.Warnings, warning => warning.Contains("missing-glyph", StringComparison.Ordinal));
        }

        [Fact]
        public void SvgCanDrawIntoACallerOwnedSurfaceWithPlacementAndStrictFeatureValidation()
        {
            SvgDocument document = SvgReader.Read("<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'><path d='M0 0H2V2H0Z' fill='red'/></svg>");
            using PixelBuffer pixels = new(4, 4);
            SvgRenderer.Draw(document, new RasterCanvas(pixels), Matrix3x2.CreateTranslation(1, 1));
            Assert.Equal(default, pixels[0, 0]);
            Assert.Equal(new Rgba32(255, 0, 0), pixels[1, 1]);
            Assert.Equal(default, pixels[3, 3]);
            Assert.Throws<ArgumentOutOfRangeException>(() => SvgRenderer.Draw(document, new RasterCanvas(pixels), Matrix3x2.CreateScale(float.NaN)));
            Assert.Throws<OperationCanceledException>(() => SvgRenderer.Draw(document, new RasterCanvas(pixels), Matrix3x2.Identity, cancellationToken: new(true)));
            SvgDocument unsupported = SvgReader.Read("<svg xmlns='http://www.w3.org/2000/svg'><unknown/></svg>");
            Assert.Throws<NotSupportedException>(() => SvgRenderer.Draw(unsupported, new RasterCanvas(pixels), Matrix3x2.Identity));
        }

        private static IEnumerable<WordPaint> Flatten(IEnumerable<WordPaint> paint)
        {
            foreach (WordPaint item in paint)
            {
                if (item is WordGroupPaint group)
                {
                    foreach (WordPaint child in Flatten(group.Children)) { yield return child; }
                }
                else { yield return item; }
            }
        }
    }
}
