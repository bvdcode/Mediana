// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264CabacResidualTests
    {
        [Theory]
        [InlineData(0, 16, -1)]
        [InlineData(1, 15, -1)]
        [InlineData(2, 16, -1)]
        [InlineData(3, 4, -1)]
        [InlineData(4, 15, -1)]
        [InlineData(0, 16, 0)]
        [InlineData(1, 15, 0)]
        [InlineData(2, 16, 0)]
        [InlineData(3, 4, 0)]
        [InlineData(4, 15, 0)]
        [InlineData(0, 16, 1)]
        [InlineData(1, 15, 1)]
        [InlineData(2, 16, 1)]
        [InlineData(3, 4, 1)]
        [InlineData(4, 15, 1)]
        [InlineData(0, 16, 2)]
        [InlineData(1, 15, 2)]
        [InlineData(2, 16, 2)]
        [InlineData(3, 4, 2)]
        [InlineData(4, 15, 2)]
        public async Task EveryPositionAndNeighbourContextMatchesIndependentIntervals(int categoryIndex, int count, int initialization)
        {
            H264ResidualCategory category = (H264ResidualCategory)categoryIndex;
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-aac.mp4"), 0);
            string referenceFile = initialization == -1 ? "cabac-intra-contexts.bin" : "cabac-predictive-contexts.bin";
            byte[] states = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264", referenceFile));
            H264BitReader headerBits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref headerBits, configuration, nal[0]);
            if (initialization != -1)
            {
                header = header with { Kind = H264SliceKind.P, CabacInitIdc = initialization };
            }
            foreach (int qp in new[] { 0, 23, 51 })
            {
                for (int neighbour = 0; neighbour < 4; neighbour++)
                {
                    H264CabacResidualWriter writer = new(qp, states, initialization);
                    List<int[]> expected = [];
                    expected.Add(new int[count]);
                    for (int position = 0; position < count; position++)
                    {
                        int[] coefficients = new int[count];
                        coefficients[position] = position % 2 == 0 ? 1 : -1;
                        expected.Add(coefficients);
                    }
                    expected.Add(Enumerable.Range(0, count).Select(index => index % 2 == 0 ? index + 1 : -index - 1).ToArray());
                    expected.Add(Enumerable.Range(0, count).Select(index => index == 0 ? -32768 : index == count - 1 ? 32767 : 0).ToArray());
                    foreach (int[] block in expected)
                    {
                        writer.Write((int)category, neighbour, block);
                    }
                    H264BitReader bits = new(writer.Finish());
                    H264CabacReader reader = new(ref bits, header with { Qp = qp });
                    foreach (int[] block in expected)
                    {
                        int[] actual = new int[count];
                        Array.Fill(actual, 123);
                        int total = H264CabacResidual.Read(ref bits, reader, category, neighbour, actual);
                        Assert.Equal(block.Count(value => value != 0), total);
                        Assert.Equal(block, actual);
                    }
                    Assert.True(reader.Termination(ref bits));
                }
            }
        }
    }
}
