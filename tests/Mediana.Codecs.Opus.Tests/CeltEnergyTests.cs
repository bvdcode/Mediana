// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class CeltEnergyTests
    {
        [Fact]
        public void EveryEnergyAndEntropyStateMatchesIndependentReference() =>
            Assert.Equal(768, OpusEnergyTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void RawRefinementAndFinalPriorityHaveExactBinaryOffsets()
        {
            OpusRangeDecoder decoder = new(new byte[] { 0, 0, 0, 255, 255, 255 });
            float[] energies = new float[42];
            int[] fine = new int[21];
            byte[] priorities = new byte[21];
            fine[0] = 2;
            fine[1] = 8;
            CeltEnergyReader.ReadFine(ref decoder, energies, fine, 2, 0, 2);
            Assert.Equal(.375f, energies[0]);
            Assert.Equal(.375f, energies[21]);
            Assert.Equal(.498046875f, energies[1]);
            Assert.Equal(.498046875f, energies[22]);
            int bits = decoder.BitsUsed;
            CeltEnergyReader.ReadFinal(ref decoder, energies, fine, priorities, 2, 0, 2);
            Assert.Equal(.4375f, energies[0]);
            Assert.Equal(.4375f, energies[21]);
            Assert.Equal(.498046875f, energies[1]);
            Assert.Equal(bits + 2, decoder.BitsUsed);
        }

        [Fact]
        public void PacketExhaustionDoesNotConsumeNonexistentFineBits()
        {
            OpusRangeDecoder decoder = new(ReadOnlySpan<byte>.Empty);
            float[] energies = new float[21];
            int[] fine = Enumerable.Repeat(8, 21).ToArray();
            CeltEnergyReader.ReadFine(ref decoder, energies, fine, 1, 0, 21);
            CeltEnergyReader.ReadFinal(ref decoder, energies, fine, new byte[21], 1, 0, 21);
            Assert.Equal(1, decoder.BitsUsed);
            Assert.All(energies, value => Assert.Equal(0, value));
            CeltEnergyReader.ReadCoarse(ref decoder, energies, 1, 0, 21, 0, true);
            Assert.Equal(1, decoder.BitsUsed);
            Assert.Equal(-1, energies[0]);
            Assert.InRange(energies[20], -18.01f, -17.99f);
        }

        [Fact]
        public void RepeatedCoarseAndFineDecodingAllocatesNoHeapStorage()
        {
            Span<float> energies = stackalloc float[42];
            Span<int> fine = stackalloc int[21];
            Span<byte> priorities = stackalloc byte[21];
            energies.Clear(); fine.Clear(); priorities.Clear();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 1000; index++)
            {
                OpusRangeDecoder decoder = new(ReadOnlySpan<byte>.Empty);
                CeltEnergyReader.ReadCoarse(ref decoder, energies, 2, 0, 21, index % 4, false);
                CeltEnergyReader.ReadFine(ref decoder, energies, fine, 2, 0, 21);
                CeltEnergyReader.ReadFinal(ref decoder, energies, fine, priorities, 2, 0, 21);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
            foreach (float energy in energies) { Assert.True(float.IsFinite(energy)); }
        }
    }
}
