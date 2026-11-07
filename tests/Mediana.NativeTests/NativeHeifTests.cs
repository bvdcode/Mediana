// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;
using Mediana.Core.Video;

namespace Mediana.NativeTests
{
    internal static class NativeHeifTests
    {
        internal static async Task RunAsync()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Heif");
            await HeifReferenceTestInput.VerifyWavefrontAsync();
            await HeifReferenceTestInput.VerifyAllAsync(root);
            await HeifReferenceTestInput.VerifyChromaAsync(root);
            await HeifReferenceTestInput.VerifyFiltersAsync(root);
            await HeifFrameOwnershipTestInput.VerifyAsync(root);
            foreach (ChromaUpsamplingFilter filter in new[] { ChromaUpsamplingFilter.NearestNeighbor,
                ChromaUpsamplingFilter.Bilinear, ChromaUpsamplingFilter.Bicubic, ChromaUpsamplingFilter.Lanczos3 })
            {
                await HeifFrameOwnershipTestInput.VerifyIccAsync(root, Path.Combine(AppContext.BaseDirectory, "Data", "Icc"), filter);
            }
            await HeifMetadataTestInput.VerifyAsync(root);
            ExifMetadataTestInput.Verify();
            await HeifExifTestInput.VerifyAsync(root);
            Console.WriteLine("PASS HEIC: 42 independent full RGBA references, transforms, grids, alpha and metadata independent of decode pixel limits.");
        }
    }
}
