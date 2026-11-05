// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing.Fonts;
using Mediana.Documents.Word;

namespace Mediana.Tests
{
    public class FontFaceInfoTests
    {
        [Theory]
        [InlineData("Aileron", false, false, false)]
        [InlineData("Aileron", true, false, false)]
        [InlineData("Canada1500", false, true, false)]
        [InlineData("Canada1500", true, true, false)]
        public void ReadsFamilyWeightAndItalicFromTheProgram(string family, bool bold, bool italic, bool declaredItalic)
        {
            TrueTypeFont font = new(BundledFontPrograms.GetFont(family, bold, italic));
            FontFaceInfo info = Assert.IsType<FontFaceInfo>(font.ReadFaceInfo());
            Assert.Equal(family, info.Family.Replace(" ", "", StringComparison.Ordinal));
            Assert.Equal(bold, info.Weight >= 600);
            Assert.Equal(declaredItalic, info.Italic);
        }

        [Fact]
        public async Task FontCollectionResolvesMetadataInsteadOfFilenamesAndHonorsBounds()
        {
            string directory = Path.Combine(Path.GetTempPath(), "mediana-fonts-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string regular = Path.Combine(directory, "unrelated-a.ttf"), bold = Path.Combine(directory, "unrelated-b.ttf");
                byte[] normal = BundledFontPrograms.GetFont("Aileron").ToArray();
                await File.WriteAllBytesAsync(regular, normal);
                await File.WriteAllBytesAsync(bold, BundledFontPrograms.GetFont("Aileron", true).ToArray());
                WordFontCollection fonts = await WordFontCollection.LoadAsync([regular, bold]);
                Assert.True(fonts.Resolve(new("aileron", false, false)).Span.SequenceEqual(normal));
                TrueTypeFont face = new(fonts.Resolve(new("Aileron", true, false)));
                Assert.True(face.ReadFaceInfo()!.Weight >= 600);
                Assert.False(fonts.ResolveCharacter(new("Unknown", false, false), 'A').IsEmpty);
                Assert.True(fonts.Resolve(new("Unknown", false, false)).IsEmpty);
                WordFontCollection bounded = await WordFontCollection.LoadAsync([regular], maximumBytes: normal.Length - 1);
                Assert.True(bounded.Resolve(new("Aileron", false, false)).IsEmpty);
                await Assert.ThrowsAsync<OperationCanceledException>(() => WordFontCollection.LoadAsync([regular], cancellationToken: new(true)));
            }
            finally { Directory.Delete(directory, recursive: true); }
        }
    }
}
