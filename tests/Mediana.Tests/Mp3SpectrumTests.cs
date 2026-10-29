// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text;
using Mediana.Codecs.Mp3;

namespace Mediana.Tests
{
    public class Mp3SpectrumTests
    {
        [Fact]
        public void AllFrequencyBandsMatchThePublishedWidths()
        {
            foreach ((int rate, int[] longWidths, int[] shortWidths) in Bands())
            {
                CheckBands(Mp3ScaleFactorBands.Long(rate), longWidths, 576);
                CheckBands(Mp3ScaleFactorBands.Short(rate), shortWidths, 192);
            }
        }

        [Fact]
        public void EveryLongRegionBoundarySelectsItsOwnHuffmanTable()
        {
            foreach ((int rate, int[] widths, _) in Bands())
            {
                for (int region0 = 0; region0 <= 15; region0++)
                {
                    for (int region1 = 0; region1 <= 7 && region0 + region1 + 2 <= widths.Length; region1++)
                    {
                        int first = widths.Take(region0 + 1).Sum();
                        int second = widths.Take(region0 + region1 + 2).Sum();
                        CheckRegions(rate, Info(288) with { Region0Count = region0, Region1Count = region1 }, first, second);
                    }
                }
            }
        }

        [Fact]
        public void SwitchedWindowsUseFrequencyDependentRegionsAndOnlyTwoTables()
        {
            foreach ((int rate, int[] longWidths, int[] shortWidths) in Bands())
            {
                foreach (int block in new[] { 1, 2, 3 })
                {
                    foreach (bool mixed in new[] { false, true })
                    {
                        int first = block == 2 ? 3 * shortWidths.Take(3).Sum() : longWidths.Take(8).Sum();
                        Mp3GranuleInfo info = Info(288) with
                        {
                            BlockType = block,
                            MixedBlock = mixed,
                            Region0Count = -1,
                            Region1Count = -1,
                            Table2 = 4
                        };
                        CheckRegions(rate, info, first, 576);
                    }
                }
            }
        }

        [Theory]
        [InlineData(0, "000000")]
        [InlineData(1, "000")]
        public void IncompleteCountOneTupleIsDiscardedAndTheRemainingSpectrumIsZero(int table, string stuffing)
        {
            string pair = Word(1, 17) + "01";
            string quad = Word(32 + table, 11) + "100";
            int[] expected = new int[576];
            new[] { 1, -1, -1, 0, 1, 1 }.CopyTo(expected, 0);
            Check(pair + quad + stuffing, 44100, Info(1) with { Count1Table = table }, expected, 6);
        }

        [Fact]
        public void ACountOneTupleCannotOverrunTheLastTwoSpectrumPositions()
        {
            string pair = Word(1, 17) + "01";
            string quad = Word(33, 15) + "0000";
            string data = pair + string.Concat(Enumerable.Repeat(quad, 144));
            int[] expected = Enumerable.Repeat(1, 576).ToArray();
            expected[1] = -1;
            expected[574] = 0;
            expected[575] = 0;
            Check(data, 44100, Info(1) with { Count1Table = 1 }, expected, 574);
        }

        [Fact]
        public void ZeroTableAndAnEmptyGranuleConsumeNoHuffmanBits()
        {
            Check("", 44100, Info(0), new int[576], 0);
            Check("", 44100, Info(288) with { Table0 = 0, Table1 = 0, Table2 = 0 }, new int[576], 576);
        }

        [Fact]
        public void TruncatedPairCannotBorrowFromTheFollowingGranule()
        {
            byte[] data = Mp3HuffmanTestInput.Pack("00001" + "111111");
            Assert.Throws<InvalidDataException>(() => ReadInvalid(data, 44100, Info(1), 4));
        }

        [Fact]
        public void InvalidCountsWindowsAndFrequenciesAreRejected()
        {
            foreach (Mp3GranuleInfo info in new[]
            {
                Info(-1), Info(289), Info(0) with { BlockType = 4 },
                Info(0) with { Region0Count = -1 }, Info(0) with { Region1Count = -1 },
                Info(0) with { Region0Count = 16 }, Info(0) with { Region1Count = 8 },
                Info(0) with { Region0Count = 15, Region1Count = 6 }
            })
            {
                Assert.Throws<InvalidDataException>(() => ReadInvalid([], 44100, info, 0));
            }
            Assert.Throws<InvalidDataException>(() => ReadInvalid([], 12345, Info(0), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ReadInvalid([], 44100, Info(0), 0, 575));
        }

        private static void CheckRegions(int rate, Mp3GranuleInfo info, int first, int second)
        {
            StringBuilder bits = new();
            int[] expected = new int[576];
            for (int index = 0; index < expected.Length; index += 2)
            {
                if (index < first)
                {
                    bits.Append(Word(1, 17)).Append("01");
                    expected[index] = 1;
                    expected[index + 1] = -1;
                }
                else if (index < second)
                {
                    bits.Append(Word(2, 32)).Append('0');
                    expected[index] = 2;
                }
                else
                {
                    bits.Append(Word(3, 2)).Append('1');
                    expected[index + 1] = -2;
                }
            }
            Check(bits.Append("10110101").ToString(), rate, info, expected, 576);
        }

        private static void Check(string payload, int rate, Mp3GranuleInfo info, int[] expected, int coded)
        {
            byte[] data = Mp3HuffmanTestInput.Pack("101" + payload + "110101");
            Mp3BitReader bits = new(data, 3, payload.Length);
            int[] output = Enumerable.Repeat(1234, 577).ToArray();
            Assert.Equal(coded, Mp3Spectrum.Read(ref bits, rate, info, output));
            Assert.Equal(expected, output[..576]);
            Assert.Equal(1234, output[576]);
            Assert.Equal(payload.Length + 3, bits.Position);
            Assert.Equal(0, bits.Remaining);
            Mp3BitReader following = new(data, bits.Position, 6);
            Assert.Equal(53, following.Read(6));
        }

        private static void ReadInvalid(byte[] data, int rate, Mp3GranuleInfo info, int bitCount, int capacity = 576)
        {
            Mp3BitReader bits = new(data, 0, bitCount);
            Mp3Spectrum.Read(ref bits, rate, info, new int[capacity]);
        }

        private static Mp3GranuleInfo Info(int pairs) => new(0, pairs, 210, 0, 0, false,
            1, 2, 3, 0, 0, 0, 0, 0, false, false, 0);

        private static string Word(int table, int value) => (table, value) switch
        {
            (1, 17) => "000",
            (2, 32) => "00011",
            (3, 2) => "000001",
            (32, 11) => "000000",
            (33, 11) => "0100",
            (33, 15) => "0000",
            _ => throw new InvalidOperationException()
        };

        private static void CheckBands(ReadOnlySpan<int> boundaries, int[] widths, int total)
        {
            Assert.Equal(widths.Length + 1, boundaries.Length);
            int position = 0;
            for (int index = 0; index < widths.Length; index++)
            {
                Assert.Equal(position, boundaries[index]);
                Assert.True(widths[index] > 0);
                position += widths[index];
            }
            Assert.Equal(total, position);
            Assert.Equal(total, boundaries[^1]);
        }

        private static IEnumerable<(int Rate, int[] Long, int[] Short)> Bands()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Mp3", "scalefactor-bands.tsv");
            foreach (string line in File.ReadLines(path).Skip(1))
            {
                string[] fields = line.Split('\t');
                yield return (int.Parse(fields[0], CultureInfo.InvariantCulture),
                    fields[1].Split(',').Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray(),
                    fields[2].Split(',').Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray());
            }
        }
    }
}
