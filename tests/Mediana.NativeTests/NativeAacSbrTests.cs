// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeAacSbrTests
    {
        public static async Task RunAsync()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "AacSbr");
            foreach (int channels in new[] { 1, 2 })
            {
                foreach (bool explicitSbr in new[] { false, true })
                {
                    await AacSbrDecodeTestInput.VerifySequenceAsync(root, channels, explicitSbr);
                }
            }
            Console.WriteLine("PASS HE-AAC: mono/stereo, explicit/implicit SBR, every independent PCM sample, exact timing and persistent state ownership.");
        }
    }
}
