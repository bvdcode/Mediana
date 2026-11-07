// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class Mp4VideoFragmentTests
    {
        [Fact]
        public async Task NewInitializationDescribesTrackTransformAspectAndConfigurationWithoutRetainingWork()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            VideoFormat format = H264EncoderTests.Format(30, 22);
            using H264Encoder encoder = H264Encoder.Create(format, new(1001, 30000), allocator);
            Mp4VideoTrackDefinition track = new(encoder.Stream, format, 17);
            using MemoryStream output = new();
            await Mp4VideoFragmentWriter.WriteInitializationAsync(track, output, allocator);
            Assert.Equal(0, allocator.AllocatedBytes);
            ReadOnlyMemory<byte> init = output.ToArray();
            ReadOnlyMemory<byte> mdhd = Mp4TestBoxes.Get(init, "moov", "trak", "mdia", "mdhd");
            Assert.Equal(30000u, Mp4TestBoxes.UInt32(mdhd, 12));
            Assert.Equal(0u, Mp4TestBoxes.UInt32(mdhd, 16));
            ReadOnlyMemory<byte> tkhd = Mp4TestBoxes.Get(init, "moov", "trak", "tkhd");
            Assert.Equal(17u, Mp4TestBoxes.UInt32(tkhd, 12));
            Assert.Equal(40u << 16, Mp4TestBoxes.UInt32(tkhd, 76));
            Assert.Equal(22u << 16, Mp4TestBoxes.UInt32(tkhd, 80));
            ReadOnlyMemory<byte> entry = Mp4TestBoxes.Get(init, "moov", "trak", "mdia", "minf", "stbl", "stsd")[8..];
            ReadOnlyMemory<byte> visual = Mp4TestBoxes.Get(entry, "avc3");
            Assert.Equal(30, BinaryPrimitives.ReadUInt16BigEndian(visual.Span[24..]));
            Assert.Equal(22, BinaryPrimitives.ReadUInt16BigEndian(visual.Span[26..]));
            Assert.Equal(encoder.Stream.Configuration.ToArray(), Mp4TestBoxes.Get(visual[78..], "avcC").ToArray());
            Assert.Equal(4u, Mp4TestBoxes.UInt32(Mp4TestBoxes.Get(visual[78..], "pasp"), 0));
            Assert.Equal(3u, Mp4TestBoxes.UInt32(Mp4TestBoxes.Get(visual[78..], "pasp"), 4));
            Assert.Equal(18u, Mp4TestBoxes.UInt32(Mp4TestBoxes.Get(init, "moov", "mvhd"), 96));
            Assert.Equal(17u, Mp4TestBoxes.UInt32(Mp4TestBoxes.Get(init, "moov", "mvex", "trex"), 4));
            Assert.True(output.CanWrite);
        }

        [Fact]
        public async Task PacketFragmentPreservesSignedCompositionTimesAndBorrowedPayloads()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264Encoder encoder = H264Encoder.Create(H264EncoderTests.Format(16, 16), new(1, 1000), allocator);
            Mp4VideoTrackDefinition track = new(encoder.Stream, H264EncoderTests.Format(16, 16), 7);
            using WritableMediaBuffer storage = allocator.Allocate(3);
            new byte[] { 17, 23, 41 }.CopyTo(storage.Memory);
            using MediaBuffer data = storage.Publish();
            using MediaPacket first = new(0, data, new(120, new(1, 1000)), new(100, new(1, 1000)), new(40, new(1, 1000)), true);
            using MediaPacket next = new(0, data, new(120, new(1, 1000)), new(140, new(1, 1000)), new(40, new(1, 1000)), false);
            long retained = allocator.AllocatedBytes;
            using MemoryStream output = new();
            long bytes = await Mp4VideoFragmentWriter.WriteFragmentAsync(track, [first, next], 19, output, allocator);
            Assert.Equal(retained, allocator.AllocatedBytes);
            Assert.Equal(output.Length, bytes);
            ReadOnlyMemory<byte> fragment = output.ToArray();
            Assert.Equal(19u, Mp4TestBoxes.UInt32(Mp4TestBoxes.Get(fragment, "moof", "mfhd"), 4));
            Assert.Equal(7u, Mp4TestBoxes.UInt32(Mp4TestBoxes.Get(fragment, "moof", "traf", "tfhd"), 4));
            Assert.Equal(100ul, Mp4TestBoxes.UInt64(Mp4TestBoxes.Get(fragment, "moof", "traf", "tfdt"), 4));
            ReadOnlyMemory<byte> run = Mp4TestBoxes.Get(fragment, "moof", "traf", "trun");
            Assert.Equal(1, run.Span[0]);
            Assert.Equal(2u, Mp4TestBoxes.UInt32(run, 4));
            Assert.Equal(40u, Mp4TestBoxes.UInt32(run, 12));
            Assert.Equal(20, BinaryPrimitives.ReadInt32BigEndian(run.Span[24..]));
            Assert.Equal(-20, BinaryPrimitives.ReadInt32BigEndian(run.Span[40..]));
            Assert.Equal(new byte[] { 17, 23, 41, 17, 23, 41 }, Mp4TestBoxes.Get(fragment, "mdat").ToArray());
            Assert.Equal(new byte[] { 17, 23, 41 }, first.Data.ToArray());
            Assert.True(output.CanWrite);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public async Task IncompleteOrInexactPacketTimingAndNoncontiguousClocksPublishNothing(int invalid)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264Encoder encoder = H264Encoder.Create(H264EncoderTests.Format(16, 16), new(1, 1000), allocator);
            Mp4VideoTrackDefinition track = new(encoder.Stream, H264EncoderTests.Format(16, 16));
            using WritableMediaBuffer storage = allocator.Allocate(1);
            using MediaBuffer data = storage.Publish();
            MediaTimestamp? pts = new(0, new(1, 1000)), dts = pts;
            MediaDuration? duration = new(40, new(1, 1000));
            bool? sync = true;
            switch (invalid)
            {
                case 0: pts = null; break;
                case 1: dts = null; break;
                case 2: duration = null; break;
                case 3: sync = null; break;
                case 4: dts = new(-1, new(1, 1000)); break;
                case 5: duration = new(1, new(1, 30000)); break;
                case 6: break;
                default: throw new ArgumentOutOfRangeException(nameof(invalid));
            }
            using MediaPacket first = new(0, data, pts, dts, duration, sync);
            using MediaPacket next = new(0, data, new(41, new(1, 1000)), new(41, new(1, 1000)), new(40, new(1, 1000)), true);
            using MemoryStream output = new();
            await Assert.ThrowsAsync<InvalidDataException>(() => Mp4VideoFragmentWriter.WriteFragmentAsync(track, [first, next], 1, output, allocator).AsTask());
            Assert.Empty(output.ToArray());
            Assert.Equal(1, allocator.AllocatedBytes);
        }
    }
}
