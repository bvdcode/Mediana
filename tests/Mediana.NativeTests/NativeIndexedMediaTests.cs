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
            string opusRoot = Path.Combine(AppContext.BaseDirectory, "Data", "Opus");
            if (OpusPulseTestInput.Verify(opusRoot) != 13574 || OpusLaplaceTestInput.Verify(opusRoot) != 5257 ||
                OpusEnergyTestInput.Verify(opusRoot) != 768 || OpusAllocationTestInput.Verify(opusRoot) != 4096 ||
                OpusFrameSyntaxTestInput.Verify(opusRoot) != 1024 || OpusVectorTestInput.Verify(opusRoot) != 3793 ||
                OpusVectorTestInput.Verify(opusRoot, "normalized-vectors-16.bin") != 1864)
            {
                throw new InvalidDataException("The CELT reference case count differs.");
            }
            Console.WriteLine("PASS CELT: pulse codewords, normalized spectral vectors, energies, allocation and sequential frame syntax match independent references.");
            Console.WriteLine("PASS on-demand HLS encoding: independent reverse and repeated mid-GOP windows, exact reference pixels, clocks and allocation release.");
            Console.WriteLine("PASS Ogg: independent Opus/Vorbis packets, metadata, reverse requests, seek preroll and borrowed source ownership.");
            Console.WriteLine("PASS indexed fMP4 HLS: initialization, reverse segment requests, repeated hashes and allocation release match.");
        }
    }
}
