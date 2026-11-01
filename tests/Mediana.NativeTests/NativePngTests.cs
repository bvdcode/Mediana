// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativePngTests
    {
        internal static async Task RunAsync()
        {
            await PngDecodeTestInput.VerifyCorpusAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Png"));
            await PngDecodeTestInput.VerifyIntegrityAsync();
            await PngDecodeTestInput.VerifyBudgetsAndColorAsync();
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
