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
                    foreach (bool singleRate in new[] { false, true })
                    {
                        await AacSbrDecodeTestInput.VerifySequenceAsync(root, channels, explicitSbr, singleRate);
                        foreach (bool mp4 in new[] { false, true })
                        {
                            await AacSbrSeekTestInput.VerifyAsync(root, channels, explicitSbr, mp4, singleRate);
                        }
                    }
                }
            }
            await AacSbrHlsTestInput.VerifyAsync(root);
            Console.WriteLine("PASS HE-AAC: mono/stereo, explicit/implicit SBR, every independent PCM sample, exact timing and persistent state ownership.");
            Console.WriteLine("PASS HE-AAC: indexed MP4/ADTS recovery, fresh HLS requests, repeated segments and bounded source reads.");
        }
    }
}
