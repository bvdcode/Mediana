// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1CompoundMaskTests
    {
        [Fact]
        public void EveryWedgeSizeIndexAndSignMatchesIndependentNativeMasks()
        {
            using FileStream source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "wedge-masks.gz"));
            using GZipStream expanded = new(source, CompressionMode.Decompress);
            using BinaryReader reader = new(expanded);
            int count = 0;
            while (expanded.ReadByte() is int width && width >= 0)
            {
                int height = reader.ReadByte();
                byte[] actual = new byte[width * height];
                for (int index = 0; index < 16; index++)
                {
                    for (int sign = 0; sign < 2; sign++)
                    {
                        Av1WedgeMask.Initialize(actual, width, height, index, sign);
                        Assert.Equal(reader.ReadBytes(actual.Length), actual);
                        count++;
                    }
                }
            }
            Assert.Equal(288, count);
        }

        [Theory]
        [InlineData(1000, 3000, 0, 45, 100)]
        [InlineData(1000, 3000, 1, 19, 150)]
        [InlineData(-1000, 5000, 0, 61, 0)]
        [InlineData(-1000, 5000, 1, 3, 255)]
        public void DifferenceMaskRetainsSignedPredictionUntilFinalBlend(short first, int second, int sign, byte weight, byte expected)
        {
            Span<short> prediction = stackalloc short[1];
            prediction[0] = first;
            Span<byte> mask = stackalloc byte[1];
            Av1CompoundBlend blend = new(prediction, new(default, Av1CompoundMaskType.Difference, Sign: sign), mask, 1);
            Assert.Equal(expected, blend.Apply(0, 0, 1, second));
            Assert.Equal(weight, mask[0]);
        }

        [Fact]
        public void ChromaUsesRoundedLumaMaskAverageWithoutOverwritingIt()
        {
            short[] prediction = [1600, 1600];
            byte[] mask = [0, 1, 63, 64, 2, 3, 61, 62];
            byte[] original = mask.ToArray();
            Av1CompoundBlend blend = new(prediction, new(default, Av1CompoundMaskType.Difference), mask, 4, 1);
            Assert.Equal(197, blend.Apply(0, 0, 2, 3200));
            Assert.Equal(102, blend.Apply(0, 1, 2, 3200));
            Assert.Equal(original, mask);
        }
    }
}
