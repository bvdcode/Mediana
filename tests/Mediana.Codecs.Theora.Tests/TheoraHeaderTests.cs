// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Theora.Tests
{
    public class TheoraHeaderTests
    {
        [Theory]
        [InlineData("main420", 96, 80, 0, 12)]
        [InlineData("chroma422", 65, 49, 2, 8)]
        [InlineData("chroma444", 96, 64, 3, 8)]
        public async Task IndependentStreamsExposeClockCropSamplingAndCompleteSetup(string name, int width, int height, int format, int rate)
        {
            List<byte[]> packets = await TheoraTestInput.ReadPacketsAsync(name);
            TheoraConfiguration config = TheoraConfiguration.Read(TheoraTestInput.Configuration(packets));
            TheoraIdentification info = config.Identification;
            Assert.Equal(width, info.Width); Assert.Equal(height, info.Height); Assert.Equal(format, info.PixelFormat);
            Assert.Equal((uint)rate, info.FrameRateNumerator); Assert.Equal(1u, info.FrameRateDenominator);
            Assert.True(info.CodedWidth >= width + info.PictureX); Assert.True(info.CodedHeight >= height + info.PictureY);
            TheoraSetup setup = TheoraSetup.Read(config.SetupHeader.Span);
            Assert.All(setup.Huffman, table => Assert.NotNull(table));
            for (int type = 0; type < 2; type++)
            {
                for (int plane = 0; plane < 3; plane++)
                {
                    for (int quality = 0; quality < 64; quality++)
                    {
                        ushort[] values = setup.Quantizer(type, plane, quality).ToArray();
                        Assert.InRange(values[0], (ushort)(16 << type), (ushort)4096);
                        Assert.All(values[1..], value => Assert.InRange(value, (ushort)(8 << type), (ushort)4096));
                    }
                }
            }
            for (int length = 0; length < packets[2].Length - 1; length++)
            { Assert.Throws<InvalidDataException>(() => TheoraSetup.Read(packets[2].AsSpan(0, length))); }
        }

        [Fact]
        public async Task InvalidIdentificationGeometryClockSamplingAndSignaturesAreRejected()
        {
            List<byte[]> packets = await TheoraTestInput.ReadPacketsAsync("main420");
            byte[] original = packets[0];
            foreach (int offset in new[] { 0, 10, 12, 14, 17, 22, 26 })
            {
                byte[] malformed = (byte[])original.Clone();
                if (offset == 0) { malformed[offset] = 0; }
                else if (offset is 22 or 26) { BinaryPrimitives.WriteUInt32BigEndian(malformed.AsSpan(offset), 0); }
                else if (offset is 10 or 12) { malformed.AsSpan(offset, 2).Clear(); }
                else { malformed.AsSpan(offset, 3).Fill(255); }
                Assert.Throws<InvalidDataException>(() => TheoraIdentification.Read(malformed));
            }
            byte[] reserved = (byte[])original.Clone(); reserved[41] |= 1;
            Assert.Throws<InvalidDataException>(() => TheoraIdentification.Read(reserved));
            Assert.Throws<InvalidDataException>(() => TheoraConfiguration.Read(XiphLacedHeaders.Write(original, packets[2], packets[1])));
        }

        [Fact]
        public void LacingRetainsBorrowedMemoryAndRejectsTruncationAndOversizedHeaders()
        {
            byte[] first = new byte[510], second = new byte[255], third = [1, 2, 3];
            byte[] data = XiphLacedHeaders.Write(first, second, third);
            XiphLacedHeaders headers = XiphLacedHeaders.Read(data);
            Assert.Equal(first, headers.First.ToArray()); Assert.Equal(second, headers.Second.ToArray()); Assert.Equal(third, headers.Third.ToArray());
            data[^1] = 9; Assert.Equal(9, headers.Third.Span[^1]);
            Assert.Throws<InvalidDataException>(() => XiphLacedHeaders.Read(new byte[] { 2, 255 }));
            Assert.Throws<InvalidDataException>(() => XiphLacedHeaders.Read(new byte[] { 2, 10, 0 }));
        }
    }
}
