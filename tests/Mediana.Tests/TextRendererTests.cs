// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Drawing.Fonts;
using System.Numerics;

namespace Mediana.Tests
{
    public class TextRendererTests
    {
        [Fact]
        public void SharedFontsAreResolvedOnceOwnedAndOutlinesCannotBeMutatedByCallers()
        {
            byte[] source = BundledFontPrograms.GetFont("Aileron").ToArray();
            int calls = 0;
            TextRenderer text = new(new() { FontResolver = _ => { calls++; return source; } });
            (TrueTypeFont font, int glyph) = text.ResolveGlyph(new("Custom"), 'A');
            Array.Clear(source);
            Assert.Same(font, text.ResolveGlyph(new("Custom"), 'B').Font);
            Assert.Equal(1, calls);
            VectorPath first = text.Outline(font, glyph);
            int commands = first.CommandCount;
            first.Rectangle(0, 0, 100, 100);
            Assert.Equal(commands, text.Outline(font, glyph).CommandCount);
            Assert.Same(font.BorrowOutline(glyph), text.BorrowOutline(font, glyph));
            using PixelBuffer pixels = new(40, 40);
            text.DrawGlyph(new(pixels), font, glyph, new(255, 0, 0), Matrix3x2.CreateScale(32 / font.Units, -32 / font.Units) * Matrix3x2.CreateTranslation(2, 34));
            Assert.Contains(pixels.Pixels.ToArray().Chunk(4), pixel => pixel[0] != 0 && pixel[3] != 0);
        }

        [Fact]
        public void FontsProgramsOutlinesAndCancellationHaveExplicitBounds()
        {
            FontRequest request = new("Aileron");
            Assert.Throws<InvalidDataException>(() => new TextRenderer(new() { MaximumFontBytes = 1 }).ResolveFont(request));
            TextRenderer faces = new(new() { MaximumFontFaces = 1 });
            Assert.NotNull(faces.ResolveFont(request));
            Assert.Throws<InvalidDataException>(() => faces.ResolveFont(request with { Bold = true }));
            TextRenderer paths = new(new() { MaximumPathCommands = 1 });
            (TrueTypeFont font, int glyph) = paths.ResolveGlyph(request, 'A');
            Assert.Throws<InvalidDataException>(() => paths.Outline(font, glyph));
            TextRenderer strict = new();
            Assert.Null(strict.ResolveFont(new("Unavailable")));
            Assert.Throws<NotSupportedException>(() => strict.ResolveGlyph(new("Unavailable"), 'A'));
            Assert.Throws<ArgumentOutOfRangeException>(() => strict.ResolveGlyph(request, 0xD800));
            Assert.Throws<OperationCanceledException>(() => strict.ResolveFont(request, new(true)));
            Assert.Throws<OperationCanceledException>(() => strict.ResolveGraphic("A", new(true)));
        }
    }
}
