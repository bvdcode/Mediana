// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeContainerImageTests
    {
        internal static async Task RunAsync()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            await ContainerImageTestInput.VerifyRealFilesAsync(root);
            await ContainerImageTestInput.VerifyPriorityAsync();
            await ContainerImageTestInput.VerifyLimitsAsync();
            await ContainerImageTestInput.VerifyEmptyAsync();
            await ContainerImageTestInput.VerifyRecognizedFailureAsync(root);
            await ContainerImageTestInput.VerifyCancellationAsync(false);
            await ContainerImageTestInput.VerifyCancellationAsync(true);
            Console.WriteLine("PASS container images: explicit priority, actual content, borrowed covers, bounded prefixes, limits, cancellation and owned payloads.");
        }
    }
}
