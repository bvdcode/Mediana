// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeIndexedMediaTests
    {
        public static async Task VerifyAsync(string root)
        {
            await FragmentHlsTestInput.VerifyAsync(root);
            await OnDemandHlsTestInput.VerifyAsync(root);
            await OggContainerTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Ogg"));
            if (OpusRangeTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data", "Opus")) != 65071)
            {
                throw new InvalidDataException("The Opus entropy reference operation count differs.");
            }
            Console.WriteLine("PASS Opus entropy: 65,071 independent symbols, final ranges and whole/fractional bit budgets match.");
            Console.WriteLine("PASS on-demand HLS encoding: independent reverse and repeated mid-GOP windows, exact reference pixels, clocks and allocation release.");
            Console.WriteLine("PASS Ogg: independent Opus/Vorbis packets, metadata, reverse requests, seek preroll and borrowed source ownership.");
            Console.WriteLine("PASS indexed fMP4 HLS: initialization, reverse segment requests, repeated hashes and allocation release match.");
        }
    }
}
