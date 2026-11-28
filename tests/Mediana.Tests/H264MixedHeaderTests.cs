// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    public class H264MixedHeaderTests
    {
        [Theory]
        [InlineData("avc-mixed-cabac")]
        [InlineData("avc-mixed-cavlc")]
        public Task MixedPicturesMatchIndependentReference(string name) =>
            H264FieldTestInput.VerifyMixedSequenceAsync(Path.GetDirectoryName(Mp4TestData.PathFor(name + ".mp4"))!, name);

        [Theory]
        [InlineData("avc-mixed-b-cabac-spatial")]
        [InlineData("avc-mixed-b-cavlc-spatial")]
        [InlineData("avc-mixed-b-cabac-temporal")]
        [InlineData("avc-mixed-b-cavlc-temporal")]
        public Task MixedBidirectionalPicturesMatchIndependentReference(string name) =>
            H264FieldTestInput.VerifyMixedBidirectionalSequenceAsync(Path.GetDirectoryName(Mp4TestData.PathFor(name + ".mp4"))!, name);

        [Theory]
        [InlineData("avc-mixed-cabac.mp4", true)]
        [InlineData("avc-mixed-cavlc.mp4", false)]
        public async Task FrameFieldTransitionsPreservePacketTimingAndPictureOrder(string name, bool cabac)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using (RandomAccessStreamByteSource source = new(File.OpenRead(Mp4TestData.PathFor(name))))
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                H264DecoderOptions options = new();
                H264Configuration configuration = H264Configuration.Parse(reader.Streams[0].Configuration.Span, options.HeaderLimits);
                H264PictureOrderState order = new();
                int index = 0;
                int ticks = 0;
                while (await reader.ReadPacketAsync() is MediaPacket packet)
                {
                    using (packet)
                    {
                        H264AccessUnit unit = H264AccessUnitReader.Read(packet.Data, configuration, options, default);
                        H264SliceHeader header = Assert.Single(H264SliceHeaders.Read(unit, options, default)).Header;
                        H264PictureStructure structure = index switch
                        {
                            < 8 or >= 26 => H264PictureStructure.Frame,
                            _ when (index & 1) == 0 => H264PictureStructure.TopField,
                            _ => H264PictureStructure.BottomField
                        };
                        int duration = structure switch
                        {
                            H264PictureStructure.Frame => 2,
                            H264PictureStructure.TopField or H264PictureStructure.BottomField => 1,
                            _ => throw new ArgumentOutOfRangeException(nameof(structure))
                        };
                        Assert.False(header.Sequence.FrameMbsOnly);
                        Assert.False(header.Layout.MacroblockAdaptiveFrameField);
                        Assert.Equal(cabac, header.Picture.Cabac);
                        Assert.Equal(structure, header.Structure);
                        Assert.Equal(ticks / 2 % 16, header.FrameNumber);
                        Assert.Equal(ticks, header.PictureOrderLsb);
                        order = order.Advance(header);
                        Assert.Equal(ticks, order.Current.Value);
                        Assert.Equal(new MediaTimestamp(ticks, new(1, 24)), packet.PresentationTime);
                        Assert.Equal(packet.PresentationTime, packet.DecodeTime);
                        MediaDuration actual = Assert.IsType<MediaDuration>(packet.Duration);
                        Assert.Equal(new MediaTimestamp(duration, new(1, 24)), new(actual.Value, actual.TimeBase));
                        configuration = unit.Configuration;
                        ticks += duration;
                        index++;
                    }
                }
                Assert.Equal(33, index);
                Assert.Equal(48, ticks);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
