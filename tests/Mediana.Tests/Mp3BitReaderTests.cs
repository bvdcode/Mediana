// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Mp3;

namespace Mediana.Tests
{
    public class Mp3BitReaderTests
    {
        [Fact]
        public void AllWidthsAndAlignmentsRespectTheDeclaredBitRange()
        {
            byte[] bytes = [0xa7, 0x16, 0xd9, 0xe3, 0x5f];
            for (int start = 0; start < 8; start++)
            {
                for (int width = 0; width <= 24; width++)
                {
                    int expected = 0;
                    for (int bit = start; bit < start + width; bit++)
                    {
                        expected = (expected << 1) | ((bytes[bit / 8] >> (7 - bit % 8)) & 1);
                    }
                    Mp3BitReader reader = new(bytes, start, width);
                    Assert.Equal(expected, reader.Read(width));
                    Assert.Equal(start + width, reader.Position);
                    Assert.Equal(0, reader.Remaining);
                    Assert.Equal(0, reader.Read(0));
                }
            }
        }

        [Fact]
        public void ReadsAndSkipsCannotReachAdjacentGranuleBits()
        {
            Assert.Throws<InvalidDataException>(() => Read(3, 8, 9));
            Assert.Throws<InvalidDataException>(() => Skip(3, 8, 9));
            Assert.Throws<ArgumentOutOfRangeException>(() => Read(0, 32, 25));
            Assert.Throws<ArgumentOutOfRangeException>(() => Read(0, 32, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Skip(0, 32, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Read(-1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Read(0, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Read(31, 2, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Read(int.MaxValue, 1, 0));
        }

        [Fact]
        public void SequentialReadsPreserveNonByteAlignedFieldBoundaries()
        {
            Mp3BitReader reader = new([0xb5, 0x6a, 0x80], 1, 16);
            Assert.Equal(3, reader.Read(3));
            reader.Skip(2);
            Assert.Equal(1, reader.Read(2));
            Assert.Equal(0x6a, reader.Read(8));
            Assert.Equal(1, reader.Read(1));
            Assert.Equal(0, reader.Remaining);
        }

        private static int Read(int start, int length, int width)
        {
            Mp3BitReader reader = new([0xff, 0xff, 0xff, 0xff], start, length);
            return reader.Read(width);
        }

        private static void Skip(int start, int length, int width)
        {
            Mp3BitReader reader = new([0xff, 0xff, 0xff, 0xff], start, length);
            reader.Skip(width);
        }
    }
}
