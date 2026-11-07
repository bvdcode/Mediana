// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeImageCodecTests
    {
        internal static async Task RunAsync()
        {
            await NativeWebpTests.RunAsync();
            await ImageExifOrientationTestInput.VerifyPngAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Png"));
            await ImageExifOrientationTestInput.VerifyWebpAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Raster"));
            ImageExifOrientationTestInput.VerifyInvalidMetadata();
            await PngDecodeTestInput.VerifyCorpusAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Png"));
            await PngDecodeTestInput.VerifyReducedCorpusAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Png"));
            await PngDecodeTestInput.VerifyIntegrityAsync();
            await PngDecodeTestInput.VerifyBudgetsAndColorAsync();
            await PngCoverTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));
            foreach ((int width, int height, bool noisy) in new[]
            {
                (1, 1, false), (1, 20000, true), (17000, 2, true), (1024, 64, true),
                (512, 512, true), (2560, 2560, false)
            })
            {
                await PngStreamingTestInput.VerifyRoundtripAsync(width, height, noisy);
            }
            await PngStreamingTestInput.VerifyPendingLifetimeAsync();
            await PngStreamingTestInput.VerifyFailuresAsync(false);
            await PngStreamingTestInput.VerifyFailuresAsync(true);
            await PngStreamingTestInput.VerifyEarlyRejectionAsync();
            await PngStreamingTestInput.VerifyAllocationAsync();
            Console.WriteLine("PASS streaming PNG: bounded IDAT, exact pixels, pending owners, cancellation and original write errors.");
        }
    }
}
