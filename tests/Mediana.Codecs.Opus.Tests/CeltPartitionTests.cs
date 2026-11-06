// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class CeltPartitionTests
    {
        [Fact]
        public void RecursivePartitionsMatchTheIndependentDecoder() =>
            Assert.Equal(7682, OpusPartitionTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void RecursiveDecodingReusesTheCodebookAndOutputStorage()
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            byte[] bytes = new byte[256];
            Span<float> vector = stackalloc float[176], low = stackalloc float[176];
            low.Fill(.125f);
            OpusRangeDecoder warm = new(bytes);
            CeltBandDecoderState state = new(codebook, 20, CeltSpreading.Normal, 4096, 17);
            CeltPartitionReader.Read(ref warm, ref state, vector, 1200, 8, low, 3, 1, 255);
            long before = GC.GetAllocatedBytesForCurrentThread();
            long budget = allocator.AllocatedBytes;
            uint masks = 0;
            for (int at = 0; at < 1000; at++)
            {
                OpusRangeDecoder decoder = new(bytes);
                state = new(codebook, 20, CeltSpreading.Normal, 4096, 17);
                masks |= CeltPartitionReader.Read(ref decoder, ref state, vector, 1200, 8, low, 3, 1, 255);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
            Assert.Equal(budget, allocator.AllocatedBytes);
            Assert.NotEqual(0u, masks);
            foreach (float coefficient in low) { Assert.Equal(.125f, coefficient); }
        }

        [Fact]
        public void EmptyPartitionsClearTheOutputAndPreserveTheNoiseSeed()
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            Span<float> vector = stackalloc float[176];
            vector.Fill(9);
            OpusRangeDecoder decoder = new(ReadOnlySpan<byte>.Empty);
            CeltBandDecoderState state = new(codebook, 20, CeltSpreading.None, 0, 37);
            uint mask = CeltPartitionReader.Read(ref decoder, ref state, vector, 0, 16, default, 3, 1, 0);
            Assert.Equal(0u, mask);
            Assert.Equal(37u, state.Seed);
            Assert.Equal(0, state.RemainingEighths);
            foreach (float coefficient in vector) { Assert.Equal(0, coefficient); }
        }
    }
}
