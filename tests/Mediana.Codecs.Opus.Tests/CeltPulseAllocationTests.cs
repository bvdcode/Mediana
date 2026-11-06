// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus.Tests
{
    public class CeltPulseAllocationTests
    {
        [Fact]
        public void CanonicalCostsPulseCountsAndNearestSelectionsMatchIndependentReference()
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "pulse-costs.bin")));
            Assert.Equal("OPUSPUL1"u8.ToArray(), reader.ReadBytes(8));
            int cases = reader.ReadInt32(), selections = 0;
            Assert.Equal(105, cases);
            for (int index = 0; index < cases; index++)
            {
                int band = reader.ReadInt32(), shift = reader.ReadInt32(), count = reader.ReadInt32();
                if (count < 0)
                {
                    Assert.Throws<ArgumentException>(() => CeltPulseAllocation.Select(band, shift, 0));
                    continue;
                }
                int cost = 0;
                for (int pulse = 0; pulse <= count; pulse++)
                {
                    Assert.Equal(reader.ReadInt32(), CeltPulseAllocation.PulseCount(pulse));
                    cost = reader.ReadInt32();
                    Assert.Equal(cost, CeltPulseAllocation.Cost(band, shift, pulse));
                }
                Assert.Equal(cost, CeltPulseAllocation.MaximumCost(band, shift));
                for (int bits = -2; bits <= 512; bits++)
                {
                    Assert.Equal(reader.ReadInt32(), CeltPulseAllocation.Select(band, shift, bits));
                    selections++;
                }
                Assert.Throws<ArgumentOutOfRangeException>(() => CeltPulseAllocation.Cost(band, shift, count + 1));
            }
            Assert.Equal(49955, selections);
            Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
        }

        [Fact]
        public void CanonicalLookupDoesNotAllocate()
        {
            CeltPulseAllocation.Select(20, 3, 100);
            long before = GC.GetAllocatedBytesForCurrentThread();
            int total = 0;
            for (int bits = 0; bits < 10000; bits++)
            {
                int pseudo = CeltPulseAllocation.Select(20, 3, bits % 257);
                total += CeltPulseAllocation.PulseCount(pseudo) + CeltPulseAllocation.Cost(20, 3, pseudo);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(total > 0);
            Assert.Equal(0, allocated);
        }
    }
}
