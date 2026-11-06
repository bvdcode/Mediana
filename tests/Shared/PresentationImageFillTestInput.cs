// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Documents.Presentation;
using Mediana.Drawing;

namespace Mediana.Tests
{
    internal static class PresentationImageFillTestInput
    {
        internal static async Task VerifyAsync()
        {
            foreach (PresentationFillLocation location in Enum.GetValues<PresentationFillLocation>())
            {
                foreach (bool strict in new[] { false, true })
                {
                    using PresentationDocument document = PresentationReader.Open(PresentationImageFillTestData.Create(location, strict: strict));
                    using PixelBuffer pixels = await RenderAsync(document);
                    Check(pixels[location == PresentationFillLocation.Background ? 4 : 19, location == PresentationFillLocation.Background ? 4 : 15] == new Rgba32(255, 0, 0), "owning part " + location);
                }
            }
            using (PresentationDocument document = PresentationReader.Open(PresentationImageFillTestData.Create()))
            using (PixelBuffer pixels = await RenderAsync(document))
            { Check(pixels[19, 15] == new Rgba32(255, 0, 0) && pixels[25, 15] == new Rgba32(0, 255, 0) && pixels[31, 15] == new Rgba32(0, 0, 255) && pixels[19, 23] == new Rgba32(0, 255, 255), "stretched quadrants"); }
            using (PresentationDocument document = PresentationReader.Open(PresentationImageFillTestData.Create(crop: "<a:srcRect l='50%'/>", mode: "<a:stretch/>")))
            using (PixelBuffer pixels = await RenderAsync(document))
            { Check(pixels[19, 15] == new Rgba32(0, 0, 255) && pixels[33, 15] == new Rgba32(255, 255, 0), "source crop"); }
            using (PresentationDocument document = PresentationReader.Open(PresentationImageFillTestData.Create(mode: "<a:stretch><a:fillRect l='25000' r='25000'/></a:stretch>")))
            using (PixelBuffer pixels = await RenderAsync(document))
            { Check(pixels[19, 15] == new Rgba32(255, 255, 255) && pixels[23, 15] == new Rgba32(255, 0, 0) && pixels[37, 15] == new Rgba32(255, 255, 255), "target insets"); }
            using (PresentationDocument document = PresentationReader.Open(PresentationImageFillTestData.Create(geometry: "ellipse")))
            using (PixelBuffer pixels = await RenderAsync(document))
            { Check(pixels[16, 12] == new Rgba32(255, 255, 255) && pixels[25, 15] == new Rgba32(0, 255, 0), "geometry clipping"); }
            using (PresentationDocument document = PresentationReader.Open(PresentationImageFillTestData.Create(rotation: 5400000)))
            using (PixelBuffer pixels = await RenderAsync(document))
            { Check(pixels[33, 11] == new Rgba32(255, 0, 0) && pixels[17, 13] == new Rgba32(255, 255, 255), "rotated fill"); }
            using (PresentationDocument document = PresentationReader.Open(PresentationImageFillTestData.Create(effects: "<a:alphaModFix amt='50000'/>")))
            using (PixelBuffer pixels = await RenderAsync(document))
            { Check(pixels[19, 15] == new Rgba32(255, 127, 127), "image opacity"); }
            using (PresentationDocument document = PresentationReader.Open(PresentationImageFillTestData.Create(mode: "<a:tile sx='300000' sy='400000' flip='xy'/>")))
            using (PixelBuffer pixels = await RenderAsync(document))
            { Check(pixels[17, 13] == new Rgba32(255, 0, 0) && pixels[29, 13] == new Rgba32(255, 255, 0) && pixels[17, 21] == new Rgba32(0, 255, 255), "scaled mirrored tiles"); }
            using (PresentationDocument document = PresentationReader.Open(PresentationImageFillTestData.Create(mode: null)))
            using (PixelBuffer pixels = await RenderAsync(document))
            { Check(pixels[16, 12] == new Rgba32(255, 0, 0) && pixels[17, 12] == new Rgba32(0, 255, 0) && pixels[20, 12] == new Rgba32(255, 0, 0), "implicit tiling"); }
            using (PresentationDocument document = PresentationReader.Open(PresentationImageFillTestData.Create(mode: "<a:tile sx='250000' sy='300000' algn='br' tx='12700' ty='-12700'/>")))
            using (PixelBuffer pixels = await RenderAsync(document))
            { Check(pixels[17, 13] == new Rgba32(0, 0, 0) && pixels[17, 16] == new Rgba32(0, 0, 255) && pixels[31, 21] == new Rgba32(255, 0, 0) && pixels[34, 21] == new Rgba32(0, 255, 0), "tile alignment and signed offsets"); }
            using (PresentationDocument document = PresentationReader.Open(PresentationImageFillTestData.Create(mode: null, crop: "<a:srcRect l='25000'/>")))
            using (PixelBuffer pixels = await RenderAsync(document))
            { Check(pixels[16, 12] == new Rgba32(0, 255, 0) && pixels[17, 12] == new Rgba32(0, 0, 255) && pixels[19, 12] == new Rgba32(0, 255, 0), "cropped tile period"); }
            int decoded = 0;
            MediaBufferAllocator allocator = new(64 * 48 * 4 + 64);
            using (PresentationDocument document = PresentationReader.Open(PresentationImageFillTestData.Create(duplicate: true, image: "PIX"u8.ToArray())))
            using (PixelBuffer pixels = await PresentationRenderer.RenderAsync(document, 0, new()
            { Dpi = 72, Supersampling = 1, Images = new([new EmbeddedTestImageDecoder(decoded: () => decoded++)]) }, allocator: allocator))
            { Check(decoded == 1 && allocator.AllocatedBytes == 64 * 48 * 4 && pixels[19, 15] == new Rgba32(21, 43, 65), "shared registry and one cached immutable frame"); }
            Check(allocator.AllocatedBytes == 0, "buffer release");
        }

        private static ValueTask<PixelBuffer> RenderAsync(PresentationDocument document) => PresentationRenderer.RenderAsync(document, 0, new() { Dpi = 72, Supersampling = 1 });
        private static void Check(bool condition, string feature) { if (!condition) { throw new InvalidDataException("Presentation image fill mismatch: " + feature); } }
    }
}
