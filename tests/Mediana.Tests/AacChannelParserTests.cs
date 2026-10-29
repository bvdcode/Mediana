// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class AacChannelParserTests
    {
        [Fact]
        public void LongChannelKeepsScaleAndSpectrumSeparateFromOverlap()
        {
            byte[] data = Header(1).Write(1, 4).Write(1, 5).Write(0, 1).Write(0, 3)
                .Write(2036, 11).Write(21, 5).ToArray();
            MediaBufferAllocator allocator = new(32768);
            using (AacChannelState channel = new(allocator))
            {
                channel.Overlap[3] = 123;
                AacBitReader bits = new(data);
                AacChannelParser.Read(ref bits, channel, 3, null, false, CancellationToken.None);
                Assert.Equal(new[] { 1, 1, 1, 1 }, channel.Quantized[..4].ToArray());
                Assert.Equal(100, channel.Scales[0]);
                Assert.Equal(123, channel.Overlap[3]);
                Assert.Equal(21, bits.Read(5));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ShortSpectraAreDeinterleavedByGroupBandAndWindow()
        {
            int[] groups = [2, 1, 3, 1, 1];
            AacTestBitWriter writer = new();
            writer.Write(100, 8).Write(0, 1).Write(2, 2).Write(1, 1).Write(2, 4).Write(0b1001100, 7);
            foreach (int group in groups)
            {
                writer.Write(1, 4).Write(2, 3);
            }
            writer.Write(0, groups.Length * 2).Write(0, 3);
            int[] expected = new int[1024];
            int firstWindow = 0;
            for (int group = 0; group < groups.Length; group++)
            {
                for (int band = 0; band < 2; band++)
                {
                    for (int window = 0; window < groups[group]; window++)
                    {
                        int value = (group + band + window) % 2;
                        if (value == 0)
                        {
                            writer.Write(0, 1);
                        }
                        else
                        {
                            writer.Write(2036, 11);
                        }
                        Array.Fill(expected, value, (firstWindow + window) * 128 + band * 4, 4);
                    }
                }
                firstWindow += groups[group];
            }
            MediaBufferAllocator allocator = new(32768);
            using AacChannelState channel = new(allocator);
            AacBitReader bits = new(writer.ToArray());
            AacChannelParser.Read(ref bits, channel, 4, null, false, CancellationToken.None);
            Assert.Equal(groups, channel.Info.Groups);
            Assert.Equal(expected, channel.Quantized.ToArray());
        }

        [Fact]
        public void PulseOnZeroIsNegativeAndTnsUsesTheBoundedReverseRegion()
        {
            AacTestBitWriter writer = Header(20).Write(0, 4).Write(20, 5)
                .Write(1, 1).Write(0, 2).Write(0, 6).Write(0, 5).Write(3, 4)
                .Write(1, 1).Write(1, 2).Write(0, 1).Write(30, 6).Write(1, 5)
                .Write(1, 1).Write(0, 1).Write(1, 3).Write(0, 1);
            MediaBufferAllocator allocator = new(32768);
            using AacChannelState channel = new(allocator);
            AacBitReader bits = new(writer.ToArray());
            AacChannelParser.Read(ref bits, channel, 3, null, false, CancellationToken.None);
            Assert.Equal(-3, channel.Quantized[0]);
            AacTnsFilter filter = Assert.Single(channel.TnsFilters);
            Assert.Equal(120, filter.Start);
            Assert.Equal(132, filter.End);
            channel.Spectrum[131] = 1;
            AacTemporalNoiseShaping.Apply(channel);
            double coefficient = Math.Sin(Math.PI / 7);
            for (int index = 0; index < 12; index++)
            {
                Assert.InRange(Math.Abs(channel.Spectrum[131 - index] - Math.Pow(-coefficient, index)), 0, 1e-15);
            }
            Assert.Equal(0, channel.Spectrum[119]);
            Assert.Equal(0, channel.Spectrum[132]);
        }

        [Theory]
        [InlineData(12, 1)]
        [InlineData(14, 1)]
        [InlineData(15, 1)]
        [InlineData(1, 0)]
        [InlineData(1, 2)]
        public void InvalidSectionsAreRejected(int book, int length)
        {
            byte[] data = Header(1).Write(book, 4).Write(length, 5).ToArray();
            Assert.Throws<InvalidDataException>(() => Parse(data));
        }

        [Fact]
        public void ReservedIcsBitsPredictionAndExcessBandsAreRejected()
        {
            Assert.Throws<InvalidDataException>(() => Parse(new AacTestBitWriter().Write(100, 8).Write(1, 1).ToArray()));
            Assert.Throws<InvalidDataException>(() => Parse(Header(50).ToArray()));
            Assert.Throws<InvalidDataException>(() => Parse(new AacTestBitWriter().Write(100, 8).Write(0, 4).Write(1, 6).Write(1, 1).ToArray()));
            Assert.Throws<InvalidDataException>(() => Parse(Header(1).Write(1, 4).Write(1, 5).ToArray()));
        }

        private static AacTestBitWriter Header(int maxBands) => new AacTestBitWriter()
            .Write(100, 8).Write(0, 4).Write(maxBands, 6).Write(0, 1);

        private static void Parse(byte[] bytes)
        {
            MediaBufferAllocator allocator = new(32768);
            using AacChannelState channel = new(allocator);
            AacBitReader bits = new(bytes);
            AacChannelParser.Read(ref bits, channel, 3, null, false, CancellationToken.None);
        }
    }
}
