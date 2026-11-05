// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeHeifTests
    {
        internal static async Task RunAsync()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Heif");
            await HeifReferenceTestInput.VerifyWavefrontAsync();
            await HeifReferenceTestInput.VerifyAllAsync(root);
            await HeifFrameOwnershipTestInput.VerifyAsync(root);
            await HeifMetadataTestInput.VerifyAsync(root);
            Console.WriteLine("PASS HEIC: 42 independent full RGBA references, transforms, grids, alpha and metadata independent of decode pixel limits.");
        }
    }
}
