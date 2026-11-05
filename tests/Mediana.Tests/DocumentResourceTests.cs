// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Drawing;
using Mediana.Drawing.Fonts;

namespace Mediana.Tests
{
    public class DocumentResourceTests
    {
        [Fact]
        public Task SharedResourcesReachWordNestedSvgAndPdf() => DocumentResourceTestInput.VerifyAsync();

        [Fact]
        public async Task ResourceSetsAreImmutableAndHonorCancellation()
        {
            TextOptions first = new();
            TextOptions second = new();
            MediaResourceSet original = MediaResourceSet.Empty.With(first);
            MediaResourceSet updated = original.With(second);
            Assert.Same(first, await original.GetAsync<TextOptions>());
            Assert.Same(second, await updated.GetAsync<TextOptions>());
            Assert.Null(await MediaResourceSet.Empty.GetAsync<TextOptions>());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original.GetAsync<TextOptions>(new(true)).AsTask());
        }

        [Fact]
        public async Task LibraryTextResourcesReuseParsedFacesAndConfiguredAliases()
        {
            string file = Path.Combine(Path.GetTempPath(), "mediana-resources-" + Guid.NewGuid().ToString("N") + ".ttf");
            await File.WriteAllBytesAsync(file, BundledFontPrograms.GetFont("Aileron").ToArray());
            try
            {
                TextOptions text = await TextResources.LoadAsync([file], new()
                {
                    FamilyAliases = new Dictionary<string, string> { ["DocumentFont"] = "Aileron" }
                });
                TrueTypeFont first = new TextRenderer(text).ResolveFont(new("DocumentFont", false, false))!;
                TrueTypeFont second = new TextRenderer(text).ResolveFont(new("Aileron", false, false))!;
                Assert.Same(first.SharedProgram, second.SharedProgram);
            }
            finally { File.Delete(file); }
        }
    }
}
