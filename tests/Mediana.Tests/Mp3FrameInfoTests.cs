using System.Buffers.Binary;
using Mediana.Codecs.Mp3;
using Mediana.Core.Audio;

namespace Mediana.Tests
{
    public class Mp3FrameInfoTests
    {
        [Theory]
        [InlineData("FFFB9064", 2, 2)]
        [InlineData("FFFB90C4", 1, 2)]
        [InlineData("FFF38064", 2, 1)]
        [InlineData("FFF380C4", 1, 1)]
        [InlineData("FFE38064", 2, 1)]
        [InlineData("FFE380C4", 1, 1)]
        public void VersionAndChannelsDetermineExactSideInfoLayout(string header, int channels, int count)
        {
            byte[] frame = Mp3TestInput.Frame(header);
            Mp3FrameInfo info = new();
            Mp3FrameHeader parsed = info.Read(frame);
            Assert.Equal(channels, info.ChannelCount);
            Assert.Equal(count, info.GranuleCount);
            Assert.Equal(channels * count, info.Granules.Length);
            Assert.Equal(0, info.MainDataBegin);
            Assert.Equal(0, info.MainDataBits);
            Assert.Equal(parsed.FrameBytes, frame.Length);
        }

        [Fact]
        public void Mpeg1FieldsRetainGranuleMajorChannelOrder()
        {
            byte[] frame = Mp3TestInput.Frame();
            Set(frame, 0, 9, 123);
            Set(frame, 12, 4, 9);
            Set(frame, 16, 4, 6);
            for (int index = 0; index < 4; index++)
            {
                int start = 20 + index * 59;
                Set(frame, start, 12, 100 + index);
                Set(frame, start + 12, 9, 288 - index);
                Set(frame, start + 21, 8, 210 + index);
                Set(frame, start + 29, 4, 12 + index);
                Set(frame, start + 34, 5, 1);
                Set(frame, start + 39, 5, 7);
                Set(frame, start + 44, 5, 31);
                Set(frame, start + 49, 4, 5);
                Set(frame, start + 53, 3, 6);
                Set(frame, start + 56, 1, 1);
                Set(frame, start + 57, 1, 1);
                Set(frame, start + 58, 1, 1);
            }
            Mp3FrameInfo info = new();
            info.Read(frame);
            Assert.Equal(123, info.MainDataBegin);
            Assert.Equal(406, info.MainDataBits);
            Assert.Equal(9, info.LeftScaleFactorReuse);
            Assert.Equal(6, info.RightScaleFactorReuse);
            for (int index = 0; index < 4; index++)
            {
                Assert.Equal(new Mp3GranuleInfo(100 + index, 288 - index, 210 + index, 12 + index,
                    0, false, 1, 7, 31, 0, 0, 0, 5, 6, true, true, 1), info.Granules[index]);
            }
        }

        [Theory]
        [InlineData(1, false, 7)]
        [InlineData(2, false, 8)]
        [InlineData(2, true, 7)]
        [InlineData(3, false, 7)]
        public void SwitchedBlocksUseSubblockGainsAndImplicitRegions(int block, bool mixed, int region)
        {
            byte[] frame = Mp3TestInput.Frame();
            int start = 20;
            Set(frame, start + 33, 1, 1);
            Set(frame, start + 34, 2, block);
            Set(frame, start + 36, 1, mixed ? 1 : 0);
            Set(frame, start + 37, 5, 6);
            Set(frame, start + 42, 5, 24);
            Set(frame, start + 47, 3, 7);
            Set(frame, start + 50, 3, 3);
            Set(frame, start + 53, 3, 1);
            Mp3FrameInfo info = new();
            info.Read(frame);
            Assert.Equal(new Mp3GranuleInfo(0, 0, 0, 0, block, mixed, 6, 24, 0, 7, 3, 1,
                region, 63, false, false, 0), info.Granules[0]);
        }

        [Theory]
        [InlineData("FFF38074", false)]
        [InlineData("FFE38074", false)]
        [InlineData("FFF38064", true)]
        [InlineData("FFF38034", true)]
        public void LowRatePreflagIsDerivedExceptOnTheIntensityChannel(string header, bool rightPreflag)
        {
            byte[] frame = Mp3TestInput.Frame(header);
            Set(frame, 10 + 29, 9, 500);
            Set(frame, 10 + 63 + 29, 9, 511);
            Mp3FrameInfo info = new();
            info.Read(frame);
            Assert.True(info.Granules[0].Preflag);
            Assert.Equal(rightPreflag, info.Granules[1].Preflag);
            Assert.Equal(500, info.Granules[0].ScaleFactorCompression);
            Assert.Equal(511, info.Granules[1].ScaleFactorCompression);
            Assert.Equal(0, info.LeftScaleFactorReuse);
            Assert.Equal(0, info.RightScaleFactorReuse);
        }

        [Theory]
        [InlineData(4, 34)]
        [InlineData(14, 34)]
        [InlineData(4, 39)]
        [InlineData(14, 39)]
        [InlineData(4, 44)]
        [InlineData(14, 44)]
        public void ReservedHuffmanTablesAreRejected(int table, int relative)
        {
            byte[] frame = Mp3TestInput.Frame();
            Set(frame, 20 + relative, 5, table);
            Assert.Throws<InvalidDataException>(() => new Mp3FrameInfo().Read(frame));
        }

        [Theory]
        [InlineData(12, 9, 289)]
        [InlineData(33, 1, 1)]
        [InlineData(0, 12, 4095)]
        public void InvalidPairCountSwitchedTypeOrMainExtentIsRejected(int offset, int width, int value)
        {
            byte[] frame = Mp3TestInput.Frame();
            Set(frame, 20 + offset, width, value);
            Assert.Throws<InvalidDataException>(() => new Mp3FrameInfo().Read(frame));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        public void ShortBlockInEitherGranuleProhibitsScaleFactorReuse(int index)
        {
            byte[] frame = Mp3TestInput.Frame();
            Set(frame, 12, 4, 1);
            Set(frame, 20 + index * 59 + 33, 3, 6);
            Assert.Throws<InvalidDataException>(() => new Mp3FrameInfo().Read(frame));
        }

        [Theory]
        [InlineData("FFFA9064")]
        [InlineData("FFFA90C4")]
        [InlineData("FFF28064")]
        [InlineData("FFF280C4")]
        [InlineData("FFE28064")]
        [InlineData("FFE280C4")]
        public void CrcCoversProtectedHeaderAndSideInfoButNotMainData(string frameHeader)
        {
            byte[] frame = Mp3TestInput.Frame(frameHeader);
            Mp3FrameHeader header = Mp3FrameHeader.Read(frame.AsSpan(0, 4));
            ushort crc = Mp3Crc.Update(Mp3Crc.Update(ushort.MaxValue, frame.AsSpan(2, 2)), frame.AsSpan(6, header.SideInfoBytes));
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), crc);
            Mp3FrameInfo info = new();
            info.Read(frame);
            frame[6 + header.SideInfoBytes] ^= 128;
            info.Read(frame);
            frame[2] ^= 1;
            Assert.Throws<InvalidDataException>(() => info.Read(frame));
            Assert.Empty(info.Granules.ToArray());
            frame[2] ^= 1;
            frame[6] ^= 1;
            Assert.Throws<InvalidDataException>(() => info.Read(frame));
            frame[6] ^= 1;
            frame[4] ^= 1;
            Assert.Throws<InvalidDataException>(() => info.Read(frame));
        }

        [Fact]
        public void ShortAndConcatenatedPacketsAreRejectedAndReuseHasNoStaleChannels()
        {
            byte[] frame = Mp3TestInput.Frame();
            Mp3FrameInfo info = new();
            Assert.Throws<InvalidDataException>(() => info.Read(frame.AsSpan(0, 3)));
            Assert.Throws<InvalidDataException>(() => info.Read(frame.AsSpan(0, frame.Length - 1)));
            Assert.Throws<InvalidDataException>(() => info.Read([.. frame, 0]));
            Set(frame, 12, 8, 255);
            info.Read(frame);
            info.Read(Mp3TestInput.Frame("FFE380C4"));
            Assert.Single(info.Granules.ToArray());
            Assert.Equal(0, info.LeftScaleFactorReuse);
            Assert.Equal(0, info.RightScaleFactorReuse);
        }

        private static void Set(byte[] frame, int sideBit, int width, int value)
        {
            int start = 4 * 8 + sideBit;
            for (int index = 0; index < width; index++)
            {
                int bit = start + index;
                int mask = 1 << (7 - (bit & 7));
                frame[bit >> 3] = (byte)((frame[bit >> 3] & ~mask)
                    | (((value >> (width - 1 - index)) & 1) != 0 ? mask : 0));
            }
        }
    }
}
