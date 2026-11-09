// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class Mp4QuickTimeMetadataTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public async Task IndexedMetadataInIsoMoviePreservesValuesAndEveryVideoAndAudioPacket(bool fullBox, bool wideHandler)
        {
            byte[] original = await Mp4TestData.ReadAsync("avc-aac-51.mp4");
            Mp4TestData.SetBrand(original, "isom"u8);
            "mp42"u8.CopyTo(original.AsSpan(Mp4TestData.PayloadOffset(original, "ftyp"), 4));
            byte[] keys = Mp4MetadataTestData.Box("keys", [0, 0, 0, 0, 0, 0, 0, 3],
                Mp4MetadataTestData.Box("mdta", "example.version"u8.ToArray()),
                Mp4MetadataTestData.Box("mdta", "example.frame-rate"u8.ToArray()),
                Mp4MetadataTestData.Box("mdta", "example.title"u8.ToArray()));
            byte[] rate = [0x42, 0x70, 0, 0];
            byte[] items = [.. Mp4MetadataTestData.Box("\0\0\0\u0001", Mp4MetadataTestData.Data(1, 0, "15"u8.ToArray())),
                .. Mp4MetadataTestData.Box("\0\0\0\u0002", Mp4MetadataTestData.Data(23, 0, rate)),
                .. Mp4MetadataTestData.Box("\0\0\0\u0003", Mp4MetadataTestData.Data(1, 0, "Пример 🎬"u8.ToArray()))];
            byte[] meta = Mp4MetadataTestData.Meta("mdta", items, keys);
            if (!fullBox)
            {
                meta = Mp4MetadataTestData.Box("meta", meta.AsSpan(12).ToArray());
            }
            if (wideHandler)
            {
                int handlerSize = (int)BinaryPrimitives.ReadUInt32BigEndian(meta.AsSpan(8));
                byte[] header = new byte[16];
                BinaryPrimitives.WriteUInt32BigEndian(header, 1);
                "hdlr"u8.CopyTo(header.AsSpan(4));
                BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(8), (ulong)handlerSize + 8);
                meta = Mp4MetadataTestData.Box("meta", header, meta.AsSpan(16, handlerSize - 8).ToArray(),
                    meta.AsSpan(8 + handlerSize).ToArray());
            }
            byte[] modified = Mp4MetadataTestData.AppendTo(original, meta, "moov");
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource originalSource = new(original, 11);
            await using TrackingRandomAccessSource modifiedSource = new(modified, 3);
            await using (Mp4Reader reference = await Mp4Reader.OpenAsync(originalSource, allocator))
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(modifiedSource, allocator))
            {
                Assert.Equal("mp42", reader.MajorBrand);
                Assert.Equal(2, reader.Streams.Count);
                Assert.Equal(MediaKind.Video, reader.Streams[0].Kind);
                Assert.Equal(MediaKind.Audio, reader.Streams[1].Kind);
                Assert.Equal("aac", reader.Streams[1].CodecId);
                Mp4MetadataItem[] metadata = reader.Metadata.Where(item => item.Namespace == "mdta").ToArray();
                Assert.Equal(3, metadata.Length);
                Assert.Equal("15", metadata[0].Text);
                Assert.Equal("example.version", metadata[0].Name);
                Assert.Equal("example.frame-rate", metadata[1].Name);
                Assert.Equal(23u, metadata[1].DataType);
                Assert.Null(metadata[1].Text);
                Assert.Equal("Пример 🎬", metadata[2].Text);
                Assert.Equal("example.title", metadata[2].Name);
                Assert.All(metadata, item => Assert.Equal("moov/meta", item.SourcePath));
                int rateIndex = reader.Metadata.ToList().FindIndex(item => item.Name == "example.frame-rate");
                using (MediaBuffer rawRate = reader.ReadMetadataValue(rateIndex))
                {
                    Assert.Equal(rate, rawRate.Memory.ToArray());
                }
                foreach (EncodedStreamInfo stream in reader.Streams)
                {
                    Assert.Equal(reference.Streams[stream.StreamIndex].Configuration.ToArray(), stream.Configuration.ToArray());
                }
                int[] packetCounts = new int[2];
                while (await reader.ReadPacketAsync() is MediaPacket packet)
                {
                    using (packet)
                    using (MediaPacket? expected = await reference.ReadPacketAsync())
                    {
                        Assert.NotNull(expected);
                        Assert.Equal(expected.StreamIndex, packet.StreamIndex);
                        Assert.Equal(expected.DecodeTime, packet.DecodeTime);
                        Assert.Equal(expected.PresentationTime, packet.PresentationTime);
                        Assert.Equal(expected.Duration, packet.Duration);
                        Assert.Equal(expected.IsKeyFrame, packet.IsKeyFrame);
                        Assert.Equal(expected.Data.ToArray(), packet.Data.ToArray());
                        packetCounts[packet.StreamIndex]++;
                    }
                }
                Assert.Null(await reference.ReadPacketAsync());
                Assert.All(packetCounts, count => Assert.True(count > 0));
                Assert.Equal(reference.Tracks[0].SampleCount, packetCounts[0]);
                Assert.Equal(reference.Tracks[1].SampleCount, packetCounts[1]);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(originalSource.IsDisposed);
            Assert.False(modifiedSource.IsDisposed);
        }

        [Fact]
        public async Task FullAndClassicLayoutsCanCoexistAcrossMovieTrackAndMediaScopes()
        {
            byte[] item = Mp4MetadataTestData.Box("©nam", Mp4MetadataTestData.Data(1, 0, "title"u8.ToArray()));
            byte[] full = Mp4MetadataTestData.Meta("mdir", item);
            byte[] classic = Mp4MetadataTestData.Box("meta", full.AsSpan(12).ToArray());
            byte[] file = await Mp4MetadataTestData.FileAsync(full);
            Mp4TestData.SetBrand(file, "isom"u8);
            file = Mp4MetadataTestData.AppendTo(file, Mp4MetadataTestData.Box("udta", classic), "moov");
            file = Mp4MetadataTestData.AppendTo(file, classic, "moov", "trak");
            file = Mp4MetadataTestData.AppendTo(file, Mp4MetadataTestData.Box("udta", classic), "moov", "trak");
            file = Mp4MetadataTestData.AppendTo(file, classic, "moov", "trak", "mdia");
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(file, 3);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                Assert.Equal(["moov/meta", "moov/udta/meta"], reader.Metadata.Select(value => value.SourcePath));
                string[] trackPaths = ["trak/meta", "trak/udta/meta", "trak/mdia/meta"];
                Assert.Equal(trackPaths.Order(),
                    reader.Tracks[0].Metadata.Select(value => value.SourcePath).Order());
                Assert.All(reader.Metadata.Concat(reader.Tracks[0].Metadata), value => Assert.Equal("title", value.Text));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false, 1u)]
        [InlineData(false, 0x01000000u)]
        [InlineData(true, 1u)]
        [InlineData(true, 0x01000000u)]
        public async Task BothLayoutsRetainHandlerVersionAndFlagsValidation(bool fullBox, uint header)
        {
            byte[] meta = Mp4MetadataTestData.Meta("mdir", []);
            BinaryPrimitives.WriteUInt32BigEndian(meta.AsSpan(20), header);
            if (!fullBox)
            {
                meta = Mp4MetadataTestData.Box("meta", meta.AsSpan(12).ToArray());
            }
            byte[] file = await Mp4MetadataTestData.FileAsync(meta);
            Mp4TestData.SetBrand(file, "isom"u8);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(file, 3);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await Mp4Reader.OpenAsync(source, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }
    }
}
