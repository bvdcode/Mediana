// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeAdtsTests
    {
        public static async Task RunAsync()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Aac");
            foreach (bool indexed in new[] { false, true })
            {
                await AdtsDecodeTestInput.VerifyAsync(root, "lc-mono-48", 48000, 1, indexed);
                await AdtsDecodeTestInput.VerifyAsync(root, "lc-stereo-44", 44100, 2, indexed);
            }
            Console.WriteLine("PASS ADTS: independent mono/stereo PCM, exact timing, sequential and indexed reading, repeated seeks and released storage.");
        }
    }
}
