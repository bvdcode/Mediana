// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Drawing.Fonts;

namespace Mediana.Tests
{
    public class SharedFontProgramTests
    {
        [Fact]
        public async Task CollectionAndIndependentRenderersShareParsedStorage()
        {
            string file = Path.Combine(Path.GetTempPath(), "mediana-font-" + Guid.NewGuid().ToString("N") + ".ttf");
            await File.WriteAllBytesAsync(file, BundledFontPrograms.GetFont("Aileron").ToArray());
            try
            {
                FontCollection collection = await FontCollection.LoadAsync([file]);
                FontRequest request = new("Aileron", false, false);
                FontProgram program = Assert.IsType<FontProgram>(collection.ResolveProgram(request));
                TextOptions options = new() { FontProgramResolver = collection.ResolveProgram };
                TrueTypeFont first = new TextRenderer(options).ResolveFont(request)!;
                TrueTypeFont second = new TextRenderer(options).ResolveFont(request)!;
                Assert.NotSame(first, second);
                Assert.Same(program, first.SharedProgram);
                Assert.Same(program, second.SharedProgram);
                Assert.Equal(collection.Resolve(request), first.Program);
                await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
                {
                    TrueTypeFont font = new TextRenderer(options).ResolveFont(request)!;
                    int glyph = font.Glyph('A');
                    Assert.Equal(program.Advance(glyph), font.Advance(glyph));
                    Assert.True(font.Outline(glyph).CommandCount > 0);
                    font.Kerning(glyph, font.Glyph('V'));
                })));
            }
            finally { File.Delete(file); }
        }

        [Fact]
        public void BundledProgramsAreSharedAndFontLimitsStillApply()
        {
            FontProgram shared = BundledFontPrograms.GetProgram("Aileron");
            TextOptions options = new() { FontProgramResolver = _ => shared, MaximumFontBytes = shared.Program.Length - 1 };
            Assert.Throws<InvalidDataException>(() => new TextRenderer(options).ResolveFont(new("Aileron", false, false)));
            Assert.Same(shared, BundledFontPrograms.GetProgram("Aileron"));
            TrueTypeFont first = new TextRenderer().ResolveFont(new("Aileron", false, false))!;
            TrueTypeFont second = new TextRenderer().ResolveFont(new("Aileron", false, false))!;
            Assert.Same(shared, first.SharedProgram);
            Assert.Same(shared, second.SharedProgram);
        }
    }
}
