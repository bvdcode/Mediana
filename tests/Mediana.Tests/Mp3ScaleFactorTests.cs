using Mediana.Codecs.Mp3;

namespace Mediana.Tests
{
    public class Mp3ScaleFactorTests
    {
        [Fact]
        public void AllMpeg1CompressionValuesAndWindowTypesMatchTheirWireOrder()
        {
            (int First, int Second)[] lengths = [(0, 0), (0, 1), (0, 2), (0, 3), (3, 0), (1, 1), (1, 2), (1, 3),
                (2, 1), (2, 2), (2, 3), (3, 1), (3, 2), (3, 3), (4, 2), (4, 3)];
            for (int compression = 0; compression < lengths.Length; compression++)
            {
                foreach ((int block, bool mixed) in new[] { (0, false), (1, false), (2, false), (2, true), (3, false) })
                {
                    int firstCount = block == 2 ? (mixed ? 17 : 18) : 11;
                    int secondCount = block == 2 ? 18 : 10;
                    int[] widths = [.. Enumerable.Repeat(lengths[compression].First, firstCount),
                        .. Enumerable.Repeat(lengths[compression].Second, secondCount)];
                    byte[] expected = Values(widths);
                    byte[] data = Encode(widths, expected, "10110");
                    Mp3BitReader bits = new(data, 0, widths.Sum() + 5);
                    byte[] output = Enumerable.Repeat((byte)0xed, 40).ToArray();
                    Mp3ScaleFactors.ReadMpeg1(ref bits, Info(compression, block, mixed), 0, output);
                    Assert.Equal(expected, output.AsSpan(0, widths.Length).ToArray());
                    Assert.All(output.AsSpan(widths.Length, 39 - widths.Length).ToArray(), value => Assert.Equal(0, value));
                    Assert.Equal(0xed, output[39]);
                    Assert.Equal(widths.Sum(), bits.Position);
                    Assert.Equal(22, bits.Read(5));
                }
            }
        }

        [Fact]
        public void EveryReuseMaskRetainsOnlyItsSelectedLongBandGroups()
        {
            int[] ends = [0, 6, 11, 16, 21];
            for (int mask = 0; mask < 16; mask++)
            {
                List<int> widths = [];
                List<byte> encoded = [];
                byte[] expected = new byte[39];
                byte[] output = Enumerable.Repeat((byte)0xed, 40).ToArray();
                for (int group = 0; group < 4; group++)
                {
                    for (int index = ends[group]; index < ends[group + 1]; index++)
                    {
                        output[index] = (byte)(index % 6);
                        if ((mask & (8 >> group)) != 0)
                        {
                            expected[index] = output[index];
                        }
                        else
                        {
                            widths.Add(group < 2 ? 4 : 3);
                            encoded.Add((byte)(index % 7));
                            expected[index] = encoded[^1];
                        }
                    }
                }
                byte[] data = Encode(widths, encoded, "101");
                Mp3BitReader bits = new(data, 0, widths.Sum() + 3);
                Mp3ScaleFactors.ReadMpeg1(ref bits, Info(15, 0, false), mask, output);
                Assert.Equal(expected, output.AsSpan(0, 39).ToArray());
                Assert.Equal(0xed, output[39]);
                Assert.Equal(widths.Sum(), bits.Position);
                Assert.Equal(5, bits.Read(3));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AllLowRateCompressionCodesMatchReverseConstructedPartitions(bool intensity)
        {
            HashSet<int> covered = [];
            foreach ((int compression, int[] lengths, int family) in LowRateCodes(intensity))
            {
                Assert.True(covered.Add(compression));
                foreach ((int block, bool mixed) in new[] { (0, false), (1, false), (2, false), (2, true), (3, false) })
                {
                    int[] counts = Counts(family, block, mixed);
                    int[] widths = counts.SelectMany((count, index) => Enumerable.Repeat(lengths[index], count)).ToArray();
                    byte[] expected = Values(widths);
                    byte[] data = Encode(widths, expected, "11001");
                    Mp3BitReader bits = new(data, 0, widths.Sum() + 5);
                    byte[] output = Enumerable.Repeat((byte)0xed, 40).ToArray();
                    byte[] limits = Enumerable.Repeat((byte)0xed, 40).ToArray();
                    Mp3ScaleFactors.ReadLowRate(ref bits, Info(compression, block, mixed), intensity, output, limits);
                    Assert.Equal(expected, output.AsSpan(0, widths.Length).ToArray());
                    for (int index = 0; index < 39; index++)
                    {
                        byte limit = intensity && index < widths.Length && widths[index] != 0
                            ? (byte)((1 << widths[index]) - 1) : byte.MaxValue;
                        Assert.Equal(limit, limits[index]);
                        if (index >= widths.Length)
                        {
                            Assert.Equal(0, output[index]);
                        }
                    }
                    Assert.Equal(0xed, output[39]);
                    Assert.Equal(0xed, limits[39]);
                    Assert.Equal(widths.Sum(), bits.Position);
                    Assert.Equal(25, bits.Read(5));
                }
            }
            Assert.Equal(512, covered.Count);
        }

        [Theory]
        [InlineData(false, 0, false, 74)]
        [InlineData(false, 2, false, 126)]
        [InlineData(false, 2, true, 122)]
        [InlineData(true, 0, false, 74)]
        [InlineData(true, 2, false, 126)]
        [InlineData(true, 2, true, 114)]
        public void ScaleFactorsCannotReadPastTheDeclaredGranule(bool lowRate, int block, bool mixed, int required)
        {
            int compression = lowRate ? 399 : 15;
            Assert.Throws<InvalidDataException>(() => ReadTruncated(lowRate, compression, block, mixed, required - 1));
            ReadTruncated(lowRate, compression, block, mixed, required);
        }

        private static void ReadTruncated(bool lowRate, int compression, int block, bool mixed, int bitsAvailable)
        {
            Mp3BitReader bits = new(new byte[32], 0, bitsAvailable);
            if (lowRate)
            {
                Mp3ScaleFactors.ReadLowRate(ref bits, Info(compression, block, mixed), false, new byte[39], new byte[39]);
            }
            else
            {
                Mp3ScaleFactors.ReadMpeg1(ref bits, Info(compression, block, mixed), 0, new byte[39]);
            }
            Assert.Equal(bitsAvailable, bits.Position);
        }

        private static Mp3GranuleInfo Info(int compression, int block, bool mixed) => new(
            0, 0, 210, compression, block, mixed, 0, 0, 0, 0, 0, 0, 0, 0, false, false, 0);

        private static byte[] Values(IReadOnlyList<int> widths) => widths.Select((width, index) =>
            (byte)((index * 11 + 7) & ((1 << width) - 1))).ToArray();

        private static byte[] Encode(IReadOnlyList<int> widths, IReadOnlyList<byte> values, string suffix)
        {
            string text = string.Concat(widths.Select((width, index) => width == 0
                ? string.Empty : Convert.ToString(values[index], 2).PadLeft(width, '0'))) + suffix;
            text = text.PadRight((text.Length + 7) / 8 * 8, '0');
            return Enumerable.Range(0, text.Length / 8).Select(index => Convert.ToByte(text.Substring(index * 8, 8), 2)).ToArray();
        }

        private static IEnumerable<(int Compression, int[] Lengths, int Family)> LowRateCodes(bool intensity)
        {
            int copies = intensity ? 2 : 1;
            for (int family = 0; family < 3; family++)
            {
                int aLimit = family == 2 ? 4 : 5;
                int bLimit = family switch { 0 => intensity ? 6 : 5, 1 => intensity ? 4 : 5, 2 => 3, _ => throw new InvalidOperationException() };
                int cLimit = family switch { 0 => intensity ? 6 : 4, 1 => 4, 2 => 1, _ => throw new InvalidOperationException() };
                int dLimit = !intensity && family == 0 ? 4 : 1;
                if (intensity && family == 1)
                {
                    aLimit = 4;
                }
                for (int a = 0; a < aLimit; a++)
                {
                    for (int b = 0; b < bLimit; b++)
                    {
                        for (int c = 0; c < cLimit; c++)
                        {
                            for (int d = 0; d < dLimit; d++)
                            {
                                int code = (family, intensity) switch
                                {
                                    (0, false) => (a * 5 + b) * 16 + c * 4 + d,
                                    (1, false) => 400 + (a * 5 + b) * 4 + c,
                                    (2, false) => 500 + a * 3 + b,
                                    (0, true) => a * 36 + b * 6 + c,
                                    (1, true) => 180 + a * 16 + b * 4 + c,
                                    (2, true) => 244 + a * 3 + b,
                                    _ => throw new InvalidOperationException()
                                };
                                for (int scale = 0; scale < copies; scale++)
                                {
                                    yield return (code * copies + scale, [a, b, c, d], family + (intensity ? 3 : 0));
                                }
                            }
                        }
                    }
                }
            }
        }

        private static int[] Counts(int family, int block, bool mixed)
        {
            int[][] longCounts = [[6, 5, 5, 5], [6, 5, 7, 3], [11, 10, 0, 0], [7, 7, 7, 0], [6, 6, 6, 3], [8, 8, 5, 0]];
            int[][] shortCounts = [[9, 9, 9, 9], [9, 9, 12, 6], [18, 18, 0, 0], [12, 12, 12, 0], [12, 9, 9, 6], [15, 12, 9, 0]];
            int[][] mixedCounts = [[6, 9, 9, 9], [6, 9, 12, 6], [15, 18, 0, 0], [6, 15, 12, 0], [6, 12, 9, 6], [6, 18, 9, 0]];
            if (block != 2)
            {
                return longCounts[family];
            }
            return mixed ? mixedCounts[family] : shortCounts[family];
        }
    }
}
