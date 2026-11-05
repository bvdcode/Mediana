// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeWebpTests
    {
        internal static async Task RunAsync()
        {
            foreach ((int width, int height) in new[] { (1, 1), (1, 65), (65, 1), (33, 35), (129, 101) })
            {
                for (int pattern = 0; pattern < 4; pattern++) { WebpEncodingTestInput.Verify(width, height, pattern, true); }
            }
            await WebpEncodingTestInput.VerifyStreamingAsync();
            await WebpEncodingTestInput.VerifyAnimationAsync();
            Console.WriteLine("PASS lossless WebP: exact RGBA, transparent RGB, strides, transforms, prefix codes, LZ77, async output and animation timing.");
        }
    }
}
