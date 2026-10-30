// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264AccessUnitTests
    {
        [Fact]
        public async Task ReadsACompletePictureWithDelimiterSupplementalDataFillerAndEndMarkers()
        {
            (H264Configuration configuration, byte[] slice) = await InputAsync();
            byte[] packet = Packet([9, 0xF0], [6, 200, 4, 0, 0, 3, 0, 4, 128], slice, [12, 255, 255, 128], [10], [11]);
            H264AccessUnit unit = H264AccessUnitReader.Read(packet, configuration, new H264DecoderOptions());
            Assert.Equal(slice, Assert.Single(unit.Slices).ToArray());
            Assert.Same(configuration, unit.Configuration);
            Assert.True(unit.EndSequence);
            Assert.True(unit.EndStream);
        }

        [Theory]
        [InlineData(new byte[] { 6, 200, 2, 1, 128 })]
        [InlineData(new byte[] { 6, 200, 255, 128 })]
        [InlineData(new byte[] { 6, 128 })]
        [InlineData(new byte[] { 9, 0 })]
        [InlineData(new byte[] { 0x29, 0xF0 })]
        [InlineData(new byte[] { 12, 254, 128 })]
        [InlineData(new byte[] { 10, 128 })]
        [InlineData(new byte[] { 0x2B })]
        [InlineData(new byte[] { 0x80 })]
        public async Task RejectsMalformedAuxiliaryNals(byte[] nal)
        {
            (H264Configuration configuration, _) = await InputAsync();
            Assert.Throws<InvalidDataException>(() => H264AccessUnitReader.Read(Packet(nal), configuration, new H264DecoderOptions()));
        }

        [Fact]
        public async Task RejectsMultiplePicturesInvalidBoundariesAndDataAfterEndMarkers()
        {
            (H264Configuration configuration, byte[] slice) = await InputAsync();
            Assert.Throws<InvalidDataException>(() => Read(Packet(slice, slice)));
            Assert.Throws<InvalidDataException>(() => Read(Packet(slice, [6, 200, 0, 128])));
            Assert.Throws<InvalidDataException>(() => Read(Packet([12, 128], [9, 0xF0], slice)));
            Assert.Throws<InvalidDataException>(() => Read(Packet([10], slice)));
            Assert.Throws<InvalidDataException>(() => Read(Packet([11], [11])));
            Assert.Throws<InvalidDataException>(() => Read([]));
            Assert.Throws<NotSupportedException>(() => Read(Packet([2, 128])));
            Assert.Throws<OperationCanceledException>(() => H264AccessUnitReader.Read(Packet(slice), configuration,
                new H264DecoderOptions(), new CancellationToken(true)));

            H264AccessUnit Read(byte[] data) => H264AccessUnitReader.Read(data, configuration, new H264DecoderOptions());
        }

        internal static byte[] Packet(params byte[][] nals)
        {
            byte[] packet = new byte[nals.Sum(nal => nal.Length + 4)];
            int offset = 0;
            foreach (byte[] nal in nals)
            {
                BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(offset), (uint)nal.Length);
                nal.CopyTo(packet, offset + 4);
                offset += nal.Length + 4;
            }
            return packet;
        }

        private static Task<(H264Configuration Configuration, byte[] Nal)> InputAsync() =>
            H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-tail.mov"), 0);
    }
}
