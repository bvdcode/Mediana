// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc.Tests
{
    public class HevcInterBlockTests
    {
        [Theory]
        [InlineData(16)]
        [InlineData(32)]
        [InlineData(64)]
        public void EveryPartitionCoversItsCodingUnitExactlyOnce(int size)
        {
            foreach (HevcPartitionMode mode in new[] { HevcPartitionMode.Whole, HevcPartitionMode.Horizontal,
                HevcPartitionMode.Vertical, HevcPartitionMode.Quarters, HevcPartitionMode.NarrowTop,
                HevcPartitionMode.NarrowBottom, HevcPartitionMode.NarrowLeft, HevcPartitionMode.NarrowRight })
            {
                HevcPredictionBlock[] blocks = new HevcPredictionBlock[4];
                int[] coverage = new int[size * size];
                int count = HevcPartitionLayout.Write(mode, 8, 12, size, blocks);
                for (int i = 0; i < count; i++)
                {
                    HevcPredictionBlock block = blocks[i];
                    Assert.Equal(i, block.Index); Assert.Equal(mode, block.Mode);
                    Assert.Equal(0, (block.X | block.Y | block.Width | block.Height) & 3);
                    for (int y = block.Y - 12; y < block.Y - 12 + block.Height; y++)
                    {
                        for (int x = block.X - 8; x < block.X - 8 + block.Width; x++) { coverage[y * size + x]++; }
                    }
                }
                Assert.All(coverage, value => Assert.Equal(1, value));
            }
        }

        [Fact]
        public void DeblockingComparesReferenceIdentityAndQuarterSampleVectorThresholds()
        {
            HevcMotionSample first = new(2, 0, new(0, 0), default, 0, -1, HevcMotionSample.List0);
            HevcMotionSample alternateList = new(0, 2, default, new(3, -3), -1, 1, HevcMotionSample.List1);
            Assert.Equal(0, HevcBoundaryStrength.Compare(first, alternateList));
            Assert.Equal(1, HevcBoundaryStrength.Compare(first, alternateList with { Vector1 = new(4, 0) }));
            Assert.Equal(1, HevcBoundaryStrength.Compare(first, alternateList with { PictureOrder1 = 3 }));
            HevcMotionSample bi = new(2, 6, new(1, 2), new(-3, 4), 0, 0, HevcMotionSample.List0 | HevcMotionSample.List1);
            HevcMotionSample swapped = new(6, 2, new(-3, 4), new(1, 2), 0, 0, HevcMotionSample.List0 | HevcMotionSample.List1);
            Assert.Equal(0, HevcBoundaryStrength.Compare(bi, swapped));
            Assert.Equal(1, HevcBoundaryStrength.Compare(bi, swapped with { Vector0 = new(1, 4) }));
            Assert.Equal(1, HevcBoundaryStrength.Compare(first, bi));
            HevcMotionSample sameReferences = bi with { PictureOrder1 = 2 };
            Assert.Equal(0, HevcBoundaryStrength.Compare(sameReferences, swapped with { PictureOrder0 = 2 }));
            Assert.Throws<InvalidOperationException>(() => HevcBoundaryStrength.Compare(default, bi));
        }
    }
}
