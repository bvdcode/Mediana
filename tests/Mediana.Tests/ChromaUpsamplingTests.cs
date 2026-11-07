// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class ChromaUpsamplingTests
    {
        [Fact]
        public Task FourFiltersMatchIndependentEightAndTenBitReferences() => HeifReferenceTestInput.VerifyChromaAsync(
            Path.Combine(AppContext.BaseDirectory, "Data", "Heif"));

        [Theory]
        [InlineData(ChromaLocation.Left, 93)]
        [InlineData(ChromaLocation.Center, 71)]
        [InlineData(ChromaLocation.TopLeft, 135)]
        [InlineData(ChromaLocation.Top, 118)]
        [InlineData(ChromaLocation.BottomLeft, 50)]
        [InlineData(ChromaLocation.Bottom, 25)]
        public void BilinearHonorsSampleLocationAndPaddedStride(ChromaLocation location, int expected)
        {
            ChromaUpsampler plan = new(2, 2, 2, 2, location, ChromaUpsamplingFilter.Bilinear);
            byte[] source = [0, 100, 255, 200, 240];
            byte[] output = new byte[4];
            plan.CopyRow<byte>(source, 3, 1, output, new float[2], 255);
            Assert.Equal(expected, output[1]);
        }

        [Theory]
        [InlineData(ChromaUpsamplingFilter.NearestNeighbor)]
        [InlineData(ChromaUpsamplingFilter.Bilinear)]
        [InlineData(ChromaUpsamplingFilter.Bicubic)]
        [InlineData(ChromaUpsamplingFilter.Lanczos3)]
        public void ConstantPlanesAndUnsubsampledAxesArePreserved(ChromaUpsamplingFilter filter)
        {
            foreach ((int x, int y) in new[] { (1, 1), (1, 2), (2, 1), (2, 2) })
            {
                ChromaUpsampler plan = new(1, 1, x, y, ChromaLocation.Center, filter);
                ushort[] row = new ushort[x];
                for (int outputY = 0; outputY < y; outputY++)
                {
                    plan.CopyRow<ushort>([777], 1, outputY, row, new float[1], 1023);
                    Assert.All(row, value => Assert.Equal(777, value));
                }
            }
            ChromaUpsampler edge = new(2, 1, 2, 1, ChromaLocation.Center, filter);
            ushort[] edgeRow = new ushort[4];
            edge.CopyRow<ushort>([0, 1023], 2, 0, edgeRow, new float[2], 1023);
            Assert.Equal(0, edgeRow[0]);
            Assert.Equal(1023, edgeRow[^1]);
        }

        [Fact]
        public void ValidationAndCancellationDoNotWriteOutputAndRowsAllocateNothing()
        {
            ChromaUpsampler plan = new(2, 2, 2, 2, ChromaLocation.Center, ChromaUpsamplingFilter.Lanczos3);
            byte[] source = [0, 255, 255, 0];
            byte[] output = [17, 17, 17, 17];
            float[] workspace = new float[2];
            Assert.Throws<OperationCanceledException>(() => plan.CopyRow<byte>(source, 2, 0, output, workspace, 255, new(true)));
            Assert.Throws<ArgumentException>(() => plan.CopyRow<byte>(source, 1, 0, output, workspace, 255));
            Assert.Throws<ArgumentException>(() => plan.CopyRow<byte>(source, 2, 0, source, workspace, 255));
            Assert.Throws<ArgumentOutOfRangeException>(() => plan.CopyRow<byte>(source, 2, 4, output, workspace, 255));
            Assert.All(output, value => Assert.Equal(17, value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChromaUpsampler(2, 2, 2, 2, ChromaLocation.Center, (ChromaUpsamplingFilter)99));
            Assert.Throws<NotSupportedException>(() => new ChromaUpsampler(2, 2, 2, 2, ChromaLocation.Unspecified, ChromaUpsamplingFilter.Bilinear));
            for (int i = 0; i < 100; i++)
            {
                plan.CopyRow<byte>(source, 2, i % 4, output, workspace, 255);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                plan.CopyRow<byte>(source, 2, i % 4, output, workspace, 255);
            }
            Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        }
    }
}
