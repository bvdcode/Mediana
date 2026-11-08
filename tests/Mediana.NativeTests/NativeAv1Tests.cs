// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeAv1Tests
    {
        public static async Task RunAsync()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Av1");
            await Av1ContainerTestInput.VerifyAsync(root, "mp4");
            await Av1ContainerTestInput.VerifyAsync(root, "mkv");
            await Av1ContainerTestInput.VerifyImplicitFinalSizesAsync(root);
            Console.WriteLine("PASS AV1 public containers: MP4, Matroska, implicit final OBU sizes, exact YUV, repeated seeking and released leases.");
        }
    }
}
