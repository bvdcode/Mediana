// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Video;

namespace Mediana.Codecs.Hevc.Tests
{
    public class HevcConfigurationTests
    {
        private static byte[] Record => [1, 0x62, 0x60, 0, 0, 0, 0xb0, 0, 0, 0, 0, 1, 93,
            0xf0, 7, 0xfd, 0xfd, 0xfa, 0xfa, 0, 24, 0x8f, 1, 0xa1, 0, 1, 0, 3, 0x42, 1, 0x80];

        [Fact]
        public void RetainsMain10ProfileConstraintsTimingAndEncodedParameterSets()
        {
            byte[] bytes = Record;
            HevcDecoderConfiguration config = HevcDecoderConfiguration.Read(bytes);
            Assert.Equal(1, config.ProfileSpace);
            Assert.True(config.HighTier);
            Assert.Equal(2, config.Profile);
            Assert.Equal(0x60000000u, config.ProfileCompatibilityFlags);
            Assert.Equal(0xb00000000001ul, config.ConstraintIndicatorFlags);
            Assert.Equal(93, config.Level);
            Assert.Equal(7, config.MinimumSpatialSegmentation);
            Assert.Equal(1, config.ParallelismType);
            Assert.Equal(1, config.ChromaFormat);
            Assert.Equal(10, config.LumaBitDepth);
            Assert.Equal(10, config.ChromaBitDepth);
            Assert.Equal(24, config.AverageFrameRate);
            Assert.Equal(2, config.ConstantFrameRate);
            Assert.Equal(1, config.TemporalLayers);
            Assert.True(config.TemporalIdNested);
            Assert.Equal(4, config.LengthSize);
            HevcNalArray array = Assert.Single(config.Arrays);
            Assert.Equal(33, array.Type);
            Assert.True(array.IsComplete);
            ReadOnlyMemory<byte> unit = Assert.Single(array.Units);
            Assert.Equal(bytes.AsMemory(bytes.Length - 3), unit);
        }

        [Fact]
        public void RejectsEveryTruncationBeforePublishingConfiguration()
        {
            byte[] bytes = Record;
            for (int length = 0; length < bytes.Length; length++)
            {
                Assert.Throws<InvalidDataException>(() => HevcDecoderConfiguration.Read(bytes.AsMemory(0, length)));
            }
            byte[] trailing = [.. bytes, 0];
            Assert.Throws<InvalidDataException>(() => HevcDecoderConfiguration.Read(trailing));
        }

        [Theory]
        [InlineData(21, 0x8e)]
        [InlineData(23, 0xe1)]
        [InlineData(25, 2)]
        [InlineData(27, 1)]
        [InlineData(28, 0xc2)]
        [InlineData(28, 0x40)]
        [InlineData(29, 0)]
        public void RejectsReservedFramingMalformedHeaderAndMismatchedArray(int offset, byte value)
        {
            byte[] bytes = Record;
            bytes[offset] = value;
            Assert.Throws<InvalidDataException>(() => HevcDecoderConfiguration.Read(bytes));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 2)]
        [InlineData(3, 4)]
        public void AcceptsAllDefinedNalLengthFieldSizes(byte field, int length)
        {
            byte[] bytes = Record;
            bytes[21] = (byte)((bytes[21] & ~3) | field);
            Assert.Equal(length, HevcDecoderConfiguration.Read(bytes).LengthSize);
        }
    }
}
