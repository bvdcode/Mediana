// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class CeltBandTests
    {
        [Fact]
        public void BandsWithTimeFrequencyReconstructionMatchTheIndependentDecoder() =>
            Assert.Equal(7782, OpusSpectralTestInput.VerifyBands(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void TimeFrequencyReconstructionDoesNotAllocateOrModifyTheBorrowedFoldingSpectrum()
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            byte[] bytes = new byte[256];
            Span<float> vector = stackalloc float[176], low = stackalloc float[176], output = stackalloc float[176];
            low.Fill(.125f);
            OpusRangeDecoder warm = new(bytes);
            CeltBandDecoderState state = new(codebook, 20, CeltSpreading.Normal, 4096, 17);
            CeltBandReader.Read(ref warm, ref state, vector, 1200, new(3, 8, -1), low, output, 1, 255);
            long before = GC.GetAllocatedBytesForCurrentThread();
            uint masks = 0;
            for (int at = 0; at < 1000; at++)
            {
                OpusRangeDecoder decoder = new(bytes);
                state = new(codebook, 20, CeltSpreading.Normal, 4096, 17);
                masks |= CeltBandReader.Read(ref decoder, ref state, vector, 1200, new(3, 8, -1), low, output, 1, 255);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
            Assert.NotEqual(0u, masks);
            foreach (float coefficient in low) { Assert.Equal(.125f, coefficient); }
        }

        [Fact]
        public void OneDimensionalBandsConsumeOnlyAnAvailableSignBit()
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            Span<float> vector = stackalloc float[1], output = stackalloc float[1];
            OpusRangeDecoder decoder = new(new byte[] { 255 });
            CeltBandDecoderState state = new(codebook, 0, CeltSpreading.None, 8, 9);
            Assert.Equal(1u, CeltBandReader.Read(ref decoder, ref state, vector, 0, new(0, 1, 0), default, output, 1, 0));
            Assert.Equal(-1, vector[0]);
            Assert.Equal(-1, output[0]);
            Assert.Equal(0, state.RemainingEighths);
            CeltBandReader.Read(ref decoder, ref state, vector, 0, new(0, 1, 0), default, output, 1, 0);
            Assert.Equal(1, vector[0]);
            Assert.Equal(0, state.RemainingEighths);
        }
    }
}
