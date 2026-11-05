// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Drawing;
using Mediana.Drawing.Emoji.Fluent;
using Mediana.Images.Svg;
using System.Numerics;
using System.Text;

namespace Mediana.Tests
{
    public class SvgEmojiTests
    {
        [Theory]
        [InlineData("text-anchor='middle' x='48'", 48 - 16, 0, 1)]
        [InlineData("textLength='64' lengthAdjust='spacingAndGlyphs'", 0, 0, 2)]
        [InlineData("rotate='90'", 0, 0, 1)]
        public void ArtworkUsesTextAnchoringLengthAndRotation(string attributes, int x, int y, int widthScale)
        {
            using FluentEmojiProvider provider = new();
            SvgDocument document = SvgReader.Read("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='64'><text y='32' font-size='32' " + attributes + ">😀</text></svg>", new() { Text = new() { InlineGraphicProvider = provider } });
            using PixelBuffer actual = SvgRenderer.Render(document, new() { Supersampling = 1 });
            using PixelBuffer expected = new(96, 64);
            Matrix3x2 placement = Matrix3x2.CreateTranslation(x, 32 + y);
            if (attributes.StartsWith("rotate", StringComparison.Ordinal)) { placement = Matrix3x2.CreateRotation(MathF.PI / 2) * placement; }
            provider.Resolve("😀")!.Draw(new(expected), Matrix3x2.CreateScale(32 * widthScale, 32) * Matrix3x2.CreateTranslation(0, -32 * .85f) * placement);
            Assert.Equal(expected.Pixels.ToArray(), actual.Pixels.ToArray());
        }

        [Fact]
        public void OpacityClippingAndWorkspaceUseTheDestinationAllocator()
        {
            using FluentEmojiProvider provider = new();
            string xml = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><clipPath id='c'><rect width='24' height='64'/></clipPath></defs><g opacity='.5' clip-path='url(#c)'><text x='0' y='48' font-size='48'>🎅</text></g></svg>";
            SvgDocument document = SvgReader.Read(xml, new() { Text = new() { InlineGraphicProvider = provider } });
            MediaBufferAllocator allocator = new(64 * 64 * 4 * 8);
            using (PixelBuffer image = SvgRenderer.Render(document, new() { Supersampling = 1 }, allocator: allocator))
            {
                Assert.True(image.Pixels.Span.ContainsAnyExcept((byte)0));
                Assert.All(image.Pixels.ToArray().Chunk(4), pixel => Assert.InRange(pixel[3], 0, 128));
                for (int y = 0; y < 64; y++) { Assert.Equal(default, image[24, y]); }
                Assert.Equal(64 * 64 * 4, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            MediaBufferAllocator small = new(64 * 64 * 4);
            Assert.Throws<MediaMemoryLimitException>(() => SvgRenderer.Render(document, new() { Supersampling = 1 }, allocator: small));
            Assert.Equal(0, small.AllocatedBytes);
        }

        [Fact]
        public async Task MetadataDoesNotTouchSharedFontOrGraphicResources()
        {
            SvgImageDecoder decoder = new(new() { Text = new() { FontResolver = _ => throw new InvalidOperationException("font") } });
            byte[] xml = Encoding.UTF8.GetBytes("<svg width='64' height='64'><text font-family='Custom'>A</text></svg>");
            Assert.Equal(64, (await decoder.ReadInfoAsync(xml)).Width);
            MediaBufferAllocator allocator = new(64 * 64 * 4);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await decoder.DecodeAsync(xml, new ImageDecodeRequest(), allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
