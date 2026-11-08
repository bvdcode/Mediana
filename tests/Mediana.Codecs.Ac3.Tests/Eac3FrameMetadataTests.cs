// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3.Tests
{
    public class Eac3FrameMetadataTests
    {
        [Theory]
        [InlineData(0, "front-left")]
        [InlineData(1, "front-center")]
        [InlineData(2, "front-right")]
        [InlineData(3, "side-left")]
        [InlineData(4, "side-right")]
        [InlineData(5, "front-left-of-center", "front-right-of-center")]
        [InlineData(6, "back-left", "back-right")]
        [InlineData(7, "back-center")]
        [InlineData(8, "top-center")]
        [InlineData(9, "surround-direct-left", "surround-direct-right")]
        [InlineData(10, "wide-left", "wide-right")]
        [InlineData(11, "top-front-left", "top-front-right")]
        [InlineData(12, "top-front-center")]
        [InlineData(13, "top-side-left", "top-side-right")]
        [InlineData(14, "low-frequency-2")]
        [InlineData(15, "low-frequency")]
        public void EveryNormativeMapBitPreservesCodedChannelOrder(int bit, params string[] expected)
        {
            ushort map = (ushort)(1 << (15 - bit));
            Assert.Equal(expected.Length, Eac3ChannelMap.GetChannelCount(map));
            Assert.Equal(expected, Eac3ChannelMap.GetChannelOrder(map));
        }

        [Fact]
        public void MaximumBsiPrefixIncludesDualMonoCompressionAndMappedChannels()
        {
            byte[] bytes = Prefix(0, true, 0xa000);
            Eac3FrameMetadata metadata = Eac3FrameMetadata.Read(bytes);
            Assert.Equal((byte)0x5a, metadata.Compression);
            Assert.Equal((byte)0x96, metadata.SecondaryCompression);
            Assert.Equal(new[] { "front-left", "front-right" }, metadata.GetChannelOrder());
            Assert.Equal(12, bytes.Length);
            Assert.Throws<InvalidDataException>(() => Eac3FrameMetadata.Read(bytes.AsSpan(0, 11)));
        }

        [Fact]
        public void AbsentMapUsesTheDeclaredBaseChannelsAndInvalidMapCountIsRejected()
        {
            Eac3FrameMetadata metadata = Eac3FrameMetadata.Read(Prefix(7, false, null));
            Assert.Null(metadata.ChannelMap); Assert.Null(metadata.Compression);
            Assert.Equal(Ac3ChannelLayout.GetChannelOrder(7, false), metadata.GetChannelOrder());
            Assert.Throws<InvalidDataException>(() => Eac3FrameMetadata.Read(Prefix(2, false, 0x8000)));
            Assert.Throws<InvalidDataException>(() => Eac3FrameMetadata.Read(Ac3DecoderTests.ReadFirstFrame("mono.ac3")));
        }

        private static byte[] Prefix(int mode, bool compression, ushort? map)
        {
            Ac3TestBitWriter writer = new(64);
            writer.Write(0xb77, 16); writer.Write(1, 2); writer.Write(0, 3); writer.Write(31, 11);
            writer.Write(0, 2); writer.Write(3, 2); writer.Write(mode, 3); writer.Write(0, 1); writer.Write(16, 5);
            writer.Write(31, 5); writer.Write(compression ? 1 : 0, 1);
            if (compression) { writer.Write(0x5a, 8); }
            if (mode == 0)
            {
                writer.Write(31, 5); writer.Write(compression ? 1 : 0, 1);
                if (compression) { writer.Write(0x96, 8); }
            }
            writer.Write(map.HasValue ? 1 : 0, 1);
            if (map.HasValue) { writer.Write(map.Value, 16); }
            return writer.Finish(true)[..Eac3FrameMetadata.MaximumPrefixBytes];
        }
    }
}
