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
            await JpegComponentTestInput.VerifyCorpusAsync(Path.Combine(root, "Jpeg"));
            JpegComponentTestInput.VerifyOwnership();
            JpegComponentTestInput.VerifyMetadata();
            JpegComponentTestInput.VerifyFailures();
            await JpegReducedTestInput.VerifyReferencesAsync(Path.Combine(root, "Jpeg"));
            JpegReducedTestInput.VerifyBudgets();
            await JpegReducedTestInput.VerifySamplingAsync(Path.Combine(root, "Jpeg"));
            await JpegReducedTestInput.VerifyFailuresAsync(Path.Combine(root, "Jpeg"));
            string exif = Path.Combine(root, "JpegExif");
            await JpegExifTestInput.VerifyCorpusAsync(exif);
            await JpegExifTestInput.VerifyFailuresAsync(exif);
            await JpegExifTestInput.VerifyColorAsync(exif);
            await JpegExifTestInput.VerifyOwnershipAsync(exif);
            Console.WriteLine("PASS JPEG Exif: all orientations, byte orders, explicit colors, chroma position and direct owned pixels.");
            Console.WriteLine("PASS sequential and progressive JPEG: complete independent pixels, refinement, restart scans, exact budgets and real borrowed covers.");
        }
    }
}
