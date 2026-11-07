// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class CeltSpectralFrameTests
    {
        [Fact]
        public void SequentialSpectralFramesMatchTheIndependentDecoder() =>
            Assert.Equal(1963, OpusSpectrumTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void EncodedSignalsAcrossBandwidthsAndSuccessiveFramesMatchTheIndependentDecoder() =>
            Assert.Equal(1152, OpusSpectrumTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data"), "encoded-spectra.bin"));

        [Fact]
        public void RepeatedFramesReuseAllSpectralStorage()
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            byte[] bytes = new byte[256];
            Span<float> energies = stackalloc float[42], coefficients = stackalloc float[1920], folding = stackalloc float[1248];
            Span<int> tf = stackalloc int[21], boosts = stackalloc int[21], shape = stackalloc int[21], fine = stackalloc int[21];
            Span<byte> priority = stackalloc byte[21], masks = stackalloc byte[42];
            CeltBandMode mode = new(2, 3, 0, 21);
            energies.Clear();
            OpusRangeDecoder warm = new(bytes);
            CeltFrameSyntax syntax = CeltFrameSyntaxReader.Read(ref warm, mode, energies, tf, boosts, new(shape, fine, priority));
            uint seed = 17;
            CeltSpectralFrameReader.Read(ref warm, codebook, mode, syntax, tf, shape, new(coefficients, folding, masks), ref seed);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int at = 0; at < 1000; at++)
            {
                energies.Clear();
                OpusRangeDecoder decoder = new(bytes);
                syntax = CeltFrameSyntaxReader.Read(ref decoder, mode, energies, tf, boosts, new(shape, fine, priority));
                seed = 17;
                CeltSpectralFrameReader.Read(ref decoder, codebook, mode, syntax, tf, shape, new(coefficients, folding, masks), ref seed);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
        }
    }
}
