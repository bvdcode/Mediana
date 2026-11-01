// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Containers.Matroska;
using Mediana.Core.Memory;
using static Mediana.Tests.MatroskaMetadataTestData;
using static Mediana.Tests.MatroskaIndexTestData;

namespace Mediana.Tests
{
    internal static class MatroskaPacketIndexReferenceInput
    {
        public static async Task VerifyAsync()
        {
            int total = 0;
            foreach (string name in new[] { "avc-aac-attachment.mkv", "aac-lace-xiph.mka", "aac-lace-fixed.mka", "aac-lace-ebml.mka", "aac-lace-group-ebml.mka" })
            {
                total += await VerifyFileAsync(name);
            }
            Require(total == 96);
        }

        private static async Task<int> VerifyFileAsync(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Matroska", name);
            byte[] bytes = await File.ReadAllBytesAsync(path);
            using JsonDocument packetReference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.ChangeExtension(path, ".packets.json")));
            using JsonDocument metadataReference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.ChangeExtension(path, ".metadata.json")));
            Require(Convert.ToHexStringLower(SHA256.HashData(bytes)) == metadataReference.RootElement.GetProperty("input_sha256").GetString());
            JsonElement packets = packetReference.RootElement.GetProperty("packets");
            MediaBufferAllocator allocator = new(131072);
            await using EbmlElementTestSource source = new(bytes) { MaximumRead = 3 };
            MatroskaHeader header = await MatroskaHeaderReader.ReadAsync(source, allocator, 256);
            EbmlElement segment;
            IReadOnlyList<MatroskaTrack>? tracks = null;
            using (MatroskaSegmentReader segments = new(source, allocator, header.MaximumSizeWidth))
            using (MatroskaMetadataReader metadata = new(source, allocator, Limits, header.MaximumSizeWidth))
            {
                segment = await segments.ReadRootAsync(header.BodyOffset);
                long offset = segment.DataOffset;
                while (await segments.ReadChildAsync(segment, offset) is EbmlElement child)
                {
                    if (child.Id == 0x1654AE6B)
                    {
                        tracks = await MatroskaTracksReader.ReadAsync(metadata, child);
                    }
                    offset = child.EndOffset!.Value;
                }
            }
            Require(allocator.AllocatedBytes == 0);
            if (tracks is null)
            {
                throw new InvalidDataException("The independently muxed input has no tracks.");
            }
            using (MatroskaPacketIndex index = await MatroskaPacketIndexBuilder.ReadAsync(source, allocator, segment, tracks, Options, Limits, header.MaximumSizeWidth))
            using (MatroskaBlockReader blockReader = new(source, allocator, Options.MaximumBlockHeaderBytes, Options.MaximumFrameBytes))
            {
                int packet = 0;
                for (int blockIndex = 0; blockIndex < index.BlockCount; blockIndex++)
                {
                    MatroskaIndexedBlock block = index.GetBlock(blockIndex);
                    MatroskaBlockHeader layout = await blockReader.ReadAsync(block.Element);
                    Require(layout.TrackNumber == tracks[block.StreamIndex].Number && layout.FrameCount == block.FrameCount);
                    for (int frameIndex = 0; frameIndex < layout.FrameCount; frameIndex++)
                    {
                        JsonElement expected = packets[packet++];
                        VerifyPayload(blockReader.GetFrame(frameIndex), bytes, expected);
                        Require(expected.GetProperty("track_number").GetUInt64() == layout.TrackNumber);
                        if (frameIndex == 0)
                        {
                            Require((long)index.GetClusterTimestamp(block.ClusterIndex) + block.RelativeTimestamp == expected.GetProperty("pts").GetInt64());
                        }
                        if (expected.TryGetProperty("block_data_offset", out JsonElement position))
                        {
                            Require(position.GetInt64() == block.DataOffset && layout.IsKeyFrame == expected.GetProperty("keyframe").GetBoolean());
                        }
                    }
                }
                Require(packet == packets.GetArrayLength() && packet == packetReference.RootElement.GetProperty("packet_count").GetInt32());
                for (int stream = 0; stream < tracks.Count; stream++)
                {
                    JsonElement[] expectedTrack = packets.EnumerateArray().Where(value => value.GetProperty("track_number").GetUInt64() == tracks[stream].Number).ToArray();
                    Require(index.GetPacketCount(stream) == expectedTrack.Length);
                    Require(index.GetPayloadBytes(stream) == expectedTrack.Sum(value => (long)value.GetProperty("size").GetInt32()));
                    for (int trackPacket = expectedTrack.Length - 1; trackPacket >= 0; trackPacket--)
                    {
                        MatroskaPacketAddress address = index.Locate(stream, trackPacket);
                        MatroskaIndexedBlock block = index.GetBlock(address.BlockIndex);
                        Require(block.StreamIndex == stream);
                        await blockReader.ReadAsync(block.Element);
                        VerifyPayload(blockReader.GetFrame(address.FrameIndex), bytes, expectedTrack[trackPacket]);
                    }
                }
                if (name == "aac-lace-group-ebml.mka")
                {
                    Require(index.BlockCount == 1 && index.GetExtensions(0) is { DurationTicks: 64 });
                }
            }
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
            return packets.GetArrayLength();
        }

        private static void VerifyPayload(MatroskaFrameRange frame, byte[] source, JsonElement expected)
        {
            Require(frame.Length == expected.GetProperty("size").GetInt32());
            Require(Convert.ToHexStringLower(SHA256.HashData(source.AsSpan((int)frame.Offset, frame.Length))) == expected.GetProperty("sha256").GetString());
        }
    }
}
