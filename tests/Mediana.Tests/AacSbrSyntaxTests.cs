// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Codecs.Aac;

namespace Mediana.Tests
{
    public class AacSbrSyntaxTests
    {
        [Theory]
        [InlineData(16000, 0, "21 23 25 27 29 31 33 35 37 39 41 43 45 47 49 51 53 55 57")]
        [InlineData(16000, 1, "21 22 23 24 25 27 29 31 33 35 37 39 42 45 49 53 57")]
        [InlineData(16000, 2, "21 22 24 26 28 30 32 34 36 39 42 45 49 53 57")]
        [InlineData(16000, 3, "21 23 25 27 29 32 35 38 42 49 57")]
        [InlineData(22050, 0, "17 18 20 22 24 26 28 30 32 34 36 38 40 42 44 46 48 50 52")]
        [InlineData(22050, 1, "17 18 19 20 21 22 23 24 26 28 30 32 34 36 39 42 45 48 52")]
        [InlineData(22050, 2, "17 18 19 20 22 24 26 28 30 32 34 38 42 47 52")]
        [InlineData(22050, 3, "17 18 20 22 24 26 28 31 34 38 42 47 52")]
        [InlineData(44100, 0, "14 16 18 20 22 24 26 28 30 32 34 36 38 40 42 44 47")]
        [InlineData(44100, 1, "14 15 16 17 18 19 20 21 22 23 24 26 28 30 33 36 39 43 47")]
        [InlineData(44100, 2, "14 15 16 17 18 19 20 22 24 26 28 30 33 36 39 43 47")]
        [InlineData(44100, 3, "14 15 16 18 20 22 24 26 28 32 36 41 47")]
        [InlineData(48000, 0, "13 15 17 19 21 23 25 27 29 31 33 35 37 39 41 43 45")]
        [InlineData(48000, 2, "13 14 15 16 17 18 19 20 22 24 26 28 31 34 37 41 45")]
        [InlineData(48000, 3, "13 14 15 16 18 20 22 24 26 30 34 39 45")]
        public void FrequencyTablesMatchReferenceMasterBands(int sampleRate, int scale, string expected)
        {
            AacSbrHeader header = new(true, 5, 9, 0, scale, true, 2, 2, 2, true, true);
            AacSbrFrequencyTables tables = new();
            tables.Update(header, sampleRate);
            int[] borders = expected.Split(' ').Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            Assert.Equal(borders, tables.Master.ToArray());
            Assert.Equal(borders[0], tables.Noise[0]);
            Assert.Equal(borders[^1], tables.Noise[^1]);
            Assert.All(tables.Noise.ToArray(), border => Assert.Contains(border, tables.Low.ToArray()));
        }

        [Fact]
        public void HeaderDefaultsAndExplicitValuesAreDistinct()
        {
            byte[] data = new AacTestBitWriter().Write(1, 1).Write(5, 4).Write(9, 4).Write(2, 3)
                .Write(0, 2).Write(0, 1).Write(0, 1).ToArray();
            AacBitReader bits = new(data);
            AacSbrHeader header = AacSbrHeader.Read(ref bits);
            Assert.Equal(new AacSbrHeader(true, 5, 9, 2, 2, true, 2, 2, 2, true, true), header);
            data = new AacTestBitWriter().Write(0, 1).Write(3, 4).Write(7, 4).Write(1, 3)
                .Write(0, 2).Write(1, 1).Write(1, 1).Write(1, 2).Write(0, 1).Write(3, 2)
                .Write(0, 2).Write(1, 2).Write(0, 1).Write(0, 1).ToArray();
            bits = new(data);
            Assert.Equal(new AacSbrHeader(false, 3, 7, 1, 1, false, 3, 0, 1, false, false), AacSbrHeader.Read(ref bits));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void VariableGridsPreserveEnvelopeOrderAndNoiseSplit(int frameClass)
        {
            AacTestBitWriter writer = new();
            writer.Write(frameClass, 2);
            switch (frameClass)
            {
                case 0:
                    writer.Write(1, 2).Write(1, 1);
                    break;
                case 1:
                    writer.Write(1, 2).Write(1, 2).Write(3, 2).Write(1, 2).Write(1, 1).Write(0, 1);
                    break;
                case 2:
                    writer.Write(1, 2).Write(1, 2).Write(3, 2).Write(2, 2).Write(0, 1).Write(1, 1);
                    break;
                case 3:
                    writer.Write(1, 2).Write(1, 2).Write(1, 2).Write(1, 2).Write(1, 2).Write(2, 2)
                        .Write(2, 2).Write(0, 1).Write(1, 1).Write(0, 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(frameClass));
            }
            AacBitReader bits = new(writer.ToArray());
            AacSbrGrid grid = new();
            grid.Read(ref bits);
            int[] expected = frameClass switch
            {
                0 => [0, 8, 16],
                1 => [0, 9, 17],
                2 => [1, 9, 16],
                3 => [1, 5, 11, 17],
                _ => throw new ArgumentOutOfRangeException(nameof(frameClass))
            };
            Assert.Equal(expected, grid.Borders.ToArray());
            Assert.Equal(2, grid.NoiseEnvelopeCount);
            Assert.Equal(expected[0], grid.NoiseBorders[0]);
            Assert.Equal(expected[^1], grid.NoiseBorders[2]);
            Assert.True(grid.NoiseBorders[1] > grid.NoiseBorders[0]);
            Assert.True(grid.NoiseBorders[1] < grid.NoiseBorders[2]);
            AacSbrGrid copied = new();
            copied.CopyFrom(grid);
            Assert.Equal(grid.Borders.ToArray(), copied.Borders.ToArray());
            Assert.Equal(grid.HighResolution.ToArray(), copied.HighResolution.ToArray());
            Assert.Equal(grid.AttackEnvelope, copied.AttackEnvelope);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void FineEnvelopeCodebooksDecodeSignedDeltas(int index)
        {
            AacSbrCodebook codebook = (AacSbrCodebook)index;
            AacBitReader bits = new(new byte[] { 0b0001_1000 });
            Assert.Equal(0, AacSbrHuffman.Read(ref bits, codebook));
            Assert.Equal(-1, AacSbrHuffman.Read(ref bits, codebook));
            Assert.Equal(1, AacSbrHuffman.Read(ref bits, codebook));
            Assert.Equal(7, bits.Position);
        }
    }
}
