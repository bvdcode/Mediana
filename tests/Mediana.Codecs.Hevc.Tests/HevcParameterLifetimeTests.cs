// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats.Video;
using Mediana.IO;

namespace Mediana.Codecs.Hevc.Tests
{
    public class HevcParameterLifetimeTests
    {
        [Fact]
        public async Task SlicesCannotDisagreeAboutPriorPictureOutput()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "slices8.mp4")));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            HevcConfiguration configuration = HevcConfiguration.Read(reader.Streams[0].Configuration, new());
            using MediaPacket packet = await reader.ReadPacketAtAsync(0, 0);
            NalPayload[] units = NalPayload.ReadLengthPrefixed(packet.Data, configuration.LengthSize)
                .Where(nal => nal.Type is >= 16 and <= 21).ToArray();
            Assert.Equal(2, units.Length);
            HevcSliceHeader first = HevcSliceHeader.Read(units[0], configuration.Pictures);
            byte[] changed = units[1].Encoded.ToArray();
            changed[2] ^= 0x40;
            HevcSliceHeader second = HevcSliceHeader.Read(new(changed), configuration.Pictures);
            Assert.NotEqual(first.NoOutputOfPriorPictures, second.NoOutputOfPriorPictures);
            Assert.Throws<InvalidDataException>(() => HevcSliceHeaders.Partition([first, second]));
        }

        [Fact]
        public async Task ReplacingASequenceRebindsPictureParametersWithoutChangingTheInitialConfiguration()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "main8.mp4")));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            HevcDecoderOptions options = new();
            HevcConfiguration initial = HevcConfiguration.Read(reader.Streams[0].Configuration, options);
            HevcDecoderConfiguration record = HevcDecoderConfiguration.Read(reader.Streams[0].Configuration);
            ReadOnlyMemory<byte> sequenceBytes = record.Arrays.Single(array => array.Type == 33).Units[0];
            HevcConfiguration updated = initial.WithParameter(new(sequenceBytes), options);
            PictureParameters originalPicture = Assert.Single(initial.Pictures.Values);
            PictureParameters updatedPicture = Assert.Single(updated.Pictures.Values);
            Assert.NotSame(originalPicture.Sequence, updatedPicture.Sequence);
            Assert.Same(Assert.Single(updated.Sequences.Values), updatedPicture.Sequence);
            Assert.Same(Assert.Single(initial.Sequences.Values), originalPicture.Sequence);
            Assert.Throws<NotSupportedException>(() => initial.WithParameter(new(sequenceBytes), new() { MaxConfigurationBytes = sequenceBytes.Length - 1 }));
        }

        [Fact]
        public async Task FailedPictureAllocationReleasesDecoderStorageAndRequiresReset()
        {
            MediaBufferAllocator inputAllocator = new(1024 * 1024), decoderAllocator = new(1);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "main10.mp4")));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, inputAllocator);
            HevcDecoderFactory factory = new();
            Assert.Null(factory.GetUnsupportedReason(reader.Streams[0]));
            using IMediaCodec<MediaPacket, VideoFrame> decoder = factory.Create(reader.Streams[0], decoderAllocator);
            using MediaPacket packet = await reader.ReadPacketAtAsync(0, 0);
            Assert.Throws<MediaMemoryLimitException>(() => decoder.TrySend(packet));
            Assert.Equal(0, decoderAllocator.AllocatedBytes);
            Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            decoder.Reset();
            Assert.Throws<MediaMemoryLimitException>(() => decoder.TrySend(packet));
            decoder.Dispose();
            Assert.Equal(0, decoderAllocator.AllocatedBytes);
        }
    }
}
