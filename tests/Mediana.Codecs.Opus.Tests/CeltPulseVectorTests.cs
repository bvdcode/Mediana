// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class CeltPulseVectorTests
    {
        [Fact]
        public void NormalizedSpreadVectorsCollapseMasksAndEntropyMatchIndependentReference() =>
            Assert.Equal(3793, OpusVectorTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void ZeroGainRetainsTheEncodedCollapseMask()
        {
            MediaBufferAllocator allocator = new(4096);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(8, 1, allocator);
            Span<float> vector = stackalloc float[8];
            OpusRangeDecoder decoder = new(new byte[32]);
            uint mask = CeltPulseVector.Read(codebook, ref decoder, vector, 1, 4, CeltSpreading.Normal, 0);
            Assert.Equal(1u, mask);
            foreach (float coefficient in vector) { Assert.Equal(0, coefficient); }
        }

        [Fact]
        public void CorruptRanksLeaveTheFloatWorkspaceUnchanged()
        {
            MediaBufferAllocator allocator = new(4096);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(8, 7, allocator);
            float[] vector = Enumerable.Repeat(17.5f, 8).ToArray();
            Assert.Throws<InvalidDataException>(() => ReadCorrupt(codebook, vector));
            Assert.All(vector, coefficient => Assert.Equal(17.5f, coefficient));
        }

        [Fact]
        public void ReusedNormalizationAndRotationUseTheOutputWorkspaceWithoutAllocating()
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            Span<float> vector = stackalloc float[176];
            byte[] data = new byte[32];
            OpusRangeDecoder warm = new(data);
            CeltPulseVector.Read(codebook, ref warm, vector, 3, 8, CeltSpreading.Aggressive, 1);
            long before = GC.GetAllocatedBytesForCurrentThread();
            long budget = allocator.AllocatedBytes;
            uint masks = 0;
            for (int index = 0; index < 10000; index++)
            {
                OpusRangeDecoder decoder = new(data);
                masks |= CeltPulseVector.Read(codebook, ref decoder, vector, 3, 8, CeltSpreading.Aggressive, 1);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
            Assert.Equal(budget, allocator.AllocatedBytes);
            Assert.Equal(1u, masks);
            float energy = 0;
            foreach (float coefficient in vector) { energy += coefficient * coefficient; }
            Assert.InRange(energy, .99999f, 1.00001f);
        }

        private static void ReadCorrupt(CeltPulseCodebook codebook, Span<float> vector)
        {
            OpusRangeDecoder decoder = new(Enumerable.Repeat((byte)255, 32).ToArray());
            CeltPulseVector.Read(codebook, ref decoder, vector, 7, 1, CeltSpreading.None, 1);
        }
    }
}
