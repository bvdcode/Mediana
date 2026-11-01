// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeJpegTests
    {
        internal static async Task RunAsync()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            await JpegDecodeTestInput.VerifyCorpusAsync(Path.Combine(root, "Jpeg"));
            await JpegDecodeTestInput.VerifyIntegrityAsync(Path.Combine(root, "Jpeg"));
            await JpegDecodeTestInput.VerifyResourcesAsync();
            await JpegDecodeTestInput.VerifyInterpretationsAsync(Path.Combine(root, "Jpeg"));
            await JpegCoverTestInput.VerifyAsync(root);
            await JpegProgressiveTestInput.VerifyOrderingAsync();
            await JpegProgressiveTestInput.VerifyEntropyAsync(Path.Combine(root, "Jpeg"));
            await JpegProgressiveTestInput.VerifyResourcesAsync();
            Console.WriteLine("PASS sequential and progressive JPEG: complete independent pixels, refinement, restart scans, exact budgets and real borrowed covers.");
        }
    }
}
