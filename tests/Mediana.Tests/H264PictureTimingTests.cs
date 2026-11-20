// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264PictureTimingTests
    {
        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(2, 1)]
        [InlineData(3, 2)]
        [InlineData(4, 2)]
        [InlineData(5, 3)]
        [InlineData(6, 3)]
        [InlineData(7, 2)]
        [InlineData(8, 3)]
        public void PreservesEveryDisplayOrderAndRepetition(int code, int clocks)
        {
            H264PictureTiming timing = Read(Binary(code, 4) + new string('0', clocks), new() { PictureStructurePresent = true });
            Assert.Equal((H264DisplayStructure)code, timing.DisplayStructure);
            Assert.Equal(H264SourceScanTypes.None, timing.SourceScanTypes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UsesTheDeclaredNalOrVclDelayLengths(bool nal)
        {
            H264HrdTiming hrd = new(24, 32, 17, 0);
            H264VuiParameters vui = new()
            {
                NalHrdTiming = nal ? hrd : null,
                VclHrdTiming = nal ? null : hrd,
                PictureStructurePresent = true
            };
            H264PictureTiming timing = Read(new string('0', 49) + "0011 00", vui);
            Assert.Equal(H264DisplayStructure.TopBottom, timing.DisplayStructure);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 2)]
        [InlineData(2, 4)]
        public void ReadsSourceScanAndDefaultTimeOffsetWithoutHrd(int scan, int expected)
        {
            string clock = FullClock(scan, 59, 58, 23) + new string('1', 23) + "0";
            H264PictureTiming timing = Read("0000 1 " + clock, new() { PictureStructurePresent = true });
            Assert.Equal((H264SourceScanTypes)expected, timing.SourceScanTypes);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(24)]
        [InlineData(31)]
        public void ReadsDifferentClockTypesAndDeclaredOffsetWidths(int offsetBits)
        {
            string offset = new('0', offsetBits);
            H264VuiParameters vui = new() { NalHrdTiming = new(1, 3, 5, offsetBits), PictureStructurePresent = true };
            H264PictureTiming timing = Read("00000000 0101 1 " + FullClock(0, 10, 20, 3) + offset +
                "1 " + FullClock(1, 10, 20, 3) + offset + "0", vui);
            Assert.Equal(H264DisplayStructure.TopBottomTop, timing.DisplayStructure);
            Assert.Equal(H264SourceScanTypes.Progressive | H264SourceScanTypes.Interlaced, timing.SourceScanTypes);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("1 111011 0")]
        [InlineData("1 111011 1 111011 0")]
        [InlineData("1 111011 1 111011 1 10111")]
        public void ReadsPartialClockTimestamps(string time)
        {
            H264VuiParameters vui = new() { VclHrdTiming = new(1, 1, 1, 0), PictureStructurePresent = true };
            H264PictureTiming timing = Read("00 0000 1 01 0 00000 0 00 00000001 " + time, vui);
            Assert.Equal(H264SourceScanTypes.Interlaced, timing.SourceScanTypes);
        }

        [Fact]
        public void ReadsEmptyAndByteAlignedTimingWithoutInventingADisplayOrder()
        {
            Assert.Equal(new H264PictureTiming(null, H264SourceScanTypes.None), Read(string.Empty, null));
            H264PictureTiming timing = Read("00000000", new() { NalHrdTiming = new(1, 3, 5, 0) });
            Assert.Null(timing.DisplayStructure);
        }

        [Theory]
        [InlineData("1111")]
        [InlineData("0000 1 11")]
        [InlineData("0000 1 00 0 00111")]
        public void RejectsReservedSyntax(string content) =>
            Assert.Throws<InvalidDataException>(() => Read(content, new() { PictureStructurePresent = true }));

        [Theory]
        [InlineData(60, 0, 0)]
        [InlineData(0, 60, 0)]
        [InlineData(0, 0, 24)]
        public void RejectsOutOfRangeClockValues(int seconds, int minutes, int hours) =>
            Assert.Throws<InvalidDataException>(() => Read("0000 1 " + FullClock(0, seconds, minutes, hours),
                new() { PictureStructurePresent = true }));

        [Theory]
        [InlineData(new byte[] { 0x00 }, 1)]
        [InlineData(new byte[] { 0x07 }, 1)]
        [InlineData(new byte[] { 0x04, 0x00 }, 2)]
        [InlineData(new byte[] { 0x04, 0xA5 }, 0)]
        public void RejectsMissingPaddingExtraDataAndTruncatedBounds(byte[] bytes, int length) =>
            Assert.Throws<InvalidDataException>(() => ReadBytes(bytes, length));

        private static void ReadBytes(byte[] bytes, int length)
        {
            H264BitReader bits = new(bytes);
            H264PictureTimingReader.Read(ref bits, length, new() { PictureStructurePresent = true });
        }

        private static H264PictureTiming Read(string content, H264VuiParameters? vui)
        {
            string payload = content.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (payload.Length % 8 != 0)
            {
                payload += "1";
                payload = payload.PadRight((payload.Length + 7) / 8 * 8, '0');
            }
            H264BitReader bits = new(H264TestBits.Rbsp(payload + "10100101", trailing: false));
            H264PictureTiming timing = H264PictureTimingReader.Read(ref bits, payload.Length / 8, vui);
            Assert.Equal(payload.Length, bits.BitPosition);
            Assert.Equal(0xA5u, bits.ReadBits(8));
            return timing;
        }

        private static string FullClock(int scan, int seconds, int minutes, int hours) =>
            Binary(scan, 2) + "0 00000 1 00 00000001 " + Binary(seconds, 6) + Binary(minutes, 6) + Binary(hours, 5);

        private static string Binary(int value, int width) => Convert.ToString(value, 2).PadLeft(width, '0');
    }
}
