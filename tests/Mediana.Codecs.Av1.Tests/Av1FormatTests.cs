// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Video;
using ObuReader = Mediana.Formats.Video.Av1ObuReader;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1FormatTests
    {
        [Theory]
        [InlineData("81000C00", 0, 8, false, true, true, 0, null)]
        [InlineData("81001C1F", 0, 8, true, true, true, 0, 16)]
        [InlineData("81004D10", 0, 10, false, true, true, 1, 1)]
        [InlineData("81204000", 1, 10, false, false, false, 0, null)]
        [InlineData("81280000", 1, 8, false, false, false, 0, null)]
        [InlineData("81400800", 2, 8, false, true, false, 0, null)]
        [InlineData("81404800", 2, 10, false, true, false, 0, null)]
        [InlineData("81406000", 2, 12, false, false, false, 0, null)]
        [InlineData("81406800", 2, 12, false, true, false, 0, null)]
        [InlineData("81486E00", 2, 12, false, true, true, 2, null)]
        [InlineData("815F7C00", 2, 12, true, true, true, 0, null)]
        public void ConfigurationCoversEveryProfileWithoutImposingDecoderLimits(string hex, int profile, int depth,
            bool monochrome, bool subX, bool subY, int chromaPosition, int? delay)
        {
            Av1CodecConfigurationRecord record = Av1CodecConfigurationRecord.Read(Convert.FromHexString(hex));
            Assert.Equal(profile, record.Profile);
            Assert.Equal(depth, record.BitDepth);
            Assert.Equal(monochrome, record.Monochrome);
            Assert.Equal(subX, record.SubsamplingX);
            Assert.Equal(subY, record.SubsamplingY);
            Assert.Equal(chromaPosition, record.ChromaPosition);
            Assert.Equal(delay, record.InitialPresentationDelay);
        }

        [Theory]
        [InlineData("")]
        [InlineData("81000C")]
        [InlineData("01000C00")]
        [InlineData("82000C00")]
        [InlineData("81000C20")]
        [InlineData("81000C01")]
        [InlineData("81600C00")]
        [InlineData("81180C00")]
        [InlineData("81008C00")]
        [InlineData("81002C00")]
        [InlineData("81000F00")]
        [InlineData("81200C00")]
        [InlineData("81201000")]
        [InlineData("81400C00")]
        [InlineData("81406400")]
        [InlineData("81406100")]
        [InlineData("81000C000801")]
        [InlineData("81000C000A0200")]
        [InlineData("81000C007A000A00")]
        [InlineData("81000C000A000A00")]
        public void InvalidHeaderAndConfigurationObuBoundariesAreRejected(string hex) =>
            Assert.Throws<InvalidDataException>(() => Av1CodecConfigurationRecord.Read(Convert.FromHexString(hex)));

        [Fact]
        public void SizedObusPreserveExtensionIdsAndImplicitSizeConsumesOnlyTheFinalRemainder()
        {
            ObuReader reader = new([0x2E, 0xB0, 1, 0x80, 0x30, 7, 8, 9], allowImplicitFinalSize: true);
            Assert.True(reader.TryRead(out int type, out ReadOnlySpan<byte> payload));
            Assert.Equal(5, type);
            Assert.Equal(5, reader.TemporalId);
            Assert.Equal(2, reader.SpatialId);
            Assert.True(payload.SequenceEqual(new byte[] { 0x80 }));
            Assert.Equal(4, reader.ByteOffset);
            Assert.True(reader.TryRead(out type, out payload));
            Assert.Equal(6, type);
            Assert.Equal(0, reader.TemporalId);
            Assert.Equal(0, reader.SpatialId);
            Assert.True(payload.SequenceEqual(new byte[] { 7, 8, 9 }));
            Assert.Equal(8, reader.ByteOffset);
            Assert.False(reader.TryRead(out _, out _));
        }

        [Theory]
        [InlineData("08")]
        [InlineData("80")]
        [InlineData("0B00")]
        [InlineData("0E")]
        [InlineData("0E0100")]
        [InlineData("0A80")]
        [InlineData("0A808080808080808000")]
        [InlineData("0A8080808010")]
        [InlineData("0A02FF")]
        [InlineData("120100")]
        public void MalformedObusCannotEscapeTheirInput(string hex)
        {
            byte[] encoded = Convert.FromHexString(hex);
            Assert.Throws<InvalidDataException>(() =>
            {
                ObuReader reader = new(encoded);
                while (reader.TryRead(out _, out _)) { }
            });
        }
    }
}
