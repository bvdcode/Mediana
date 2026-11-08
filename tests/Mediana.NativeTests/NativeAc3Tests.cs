// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeAc3Tests
    {
        public static async Task RunAsync()
        {
            await Ac3ContainerTestInput.VerifyAsync("mono.mp4", "mono.ac3", false, 1);
            await Ac3ContainerTestInput.VerifyAsync("stereo-eac3.mp4", "stereo.eac3", true, 2);
            await Ac3ContainerTestInput.VerifyBundledAndSeekAsync();
            await Console.Out.WriteLineAsync("PASS AC-3/E-AC-3: raw and MP4 packets, metadata, complete PCM, bundled syncframes, seek overlap and owned storage.");
        }
    }
}
