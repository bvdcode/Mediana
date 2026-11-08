// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1TransformContextTests
    {
        [Fact]
        public void EveryTransformGeometryHasACompleteCoefficientScan()
        {
            ReadOnlySpan<int> widths = [4, 8, 16, 32, 64, 4, 8, 8, 16, 16, 32, 32, 64, 4, 16, 8, 32, 16, 64];
            ReadOnlySpan<int> heights = [4, 8, 16, 32, 64, 8, 4, 16, 8, 32, 16, 64, 32, 16, 4, 32, 8, 64, 16];
            Span<bool> visited = stackalloc bool[1024];
            for (int index = 0; index < widths.Length; index++)
            {
                Av1TransformSize size = new(widths[index], heights[index]);
                Assert.Equal(index, size.Index);
                ReadOnlySpan<ushort> scan = Av1CoefficientTables.Scan(size.CodedWidth, size.CodedHeight);
                Assert.Equal(size.CodedWidth * size.CodedHeight, scan.Length);
                Assert.Equal(0, scan[0]);
                visited.Clear();
                foreach (ushort position in scan)
                {
                    Assert.InRange(position, 0, scan.Length - 1);
                    Assert.False(visited[position]);
                    visited[position] = true;
                }
                if (index > 0)
                {
                    Assert.True(size.Split.Width * size.Split.Height < size.Width * size.Height);
                    Assert.InRange(size.Split.Index, 0, 18);
                }
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(21)]
        [InlineData(61)]
        [InlineData(121)]
        public void CompleteSizeContextsAreValidAndReferenceLoadResetsOnlyCounters(int quantizer)
        {
            ushort[] initialized = new ushort[Av1DefaultCdfs.Count];
            Av1TileProbabilities.Initialize(initialized, quantizer);
            int start = 0;
            foreach (int counter in Av1DefaultCdfs.CounterOffsets)
            {
                Av1Cdf.Validate(initialized.AsSpan(start, counter - start + 1));
                initialized[counter] = 17;
                start = counter + 1;
            }
            Assert.Equal(initialized.Length, start);
            ushort[] loaded = new ushort[initialized.Length];
            Av1TileProbabilities.Initialize(loaded, 255, initialized);
            foreach (int counter in Av1DefaultCdfs.CounterOffsets)
            {
                Assert.Equal(0, loaded[counter]);
                Assert.Equal(17, initialized[counter]);
                initialized[counter] = 0;
            }
            Assert.Equal(initialized, loaded);
        }
    }
}
