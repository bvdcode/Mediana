// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Containers.Matroska;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class MatroskaBlockReferenceInput
    {
        public static async Task VerifyAsync()
        {
            foreach (string name in new[] { "avc-aac-attachment.mkv", "aac-lace-xiph.mka", "aac-lace-fixed.mka", "aac-lace-ebml.mka", "aac-lace-group-ebml.mka" })
            {
                await VerifyFileAsync(name);
            }
        }

        private static async Task VerifyFileAsync(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Matroska", name);
            byte[] bytes = await File.ReadAllBytesAsync(path);
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.ChangeExtension(path, ".packets.json")));
            JsonElement packets = reference.RootElement.GetProperty("packets");
            int packet = 0;
            MediaBufferAllocator allocator = new(4096);
            await using EbmlElementTestSource source = new(bytes) { MaximumRead = 3 };
            MatroskaHeader header = await MatroskaHeaderReader.ReadAsync(source, allocator, 256);
            using (MatroskaSegmentReader segments = new(source, allocator, header.MaximumSizeWidth))
            using (EbmlElementReader elements = new(source, allocator, maximumSizeWidth: header.MaximumSizeWidth))
            using (MatroskaBlockReader blocks = new(source, allocator, 4096, 1024 * 1024))
            using (WritableMediaBuffer scalar = allocator.Allocate(sizeof(ulong)))
            {
                EbmlElement root = await segments.ReadRootAsync(header.BodyOffset);
                long offset = root.DataOffset;
                while (await segments.ReadChildAsync(root, offset) is EbmlElement element)
                {
                    if (element.Id == 0x1F43B675)
                    {
                        ulong? timestamp = null;
                        long childOffset = element.DataOffset;
                        while (await elements.ReadAsync(childOffset, element.EndOffset) is EbmlElement child)
                        {
                            switch (child.Id)
                            {
                                case 0xE7:
                                    int size = checked((int)child.DataSize!.Value);
                                    await source.ReadExactlyAtAsync(child.DataOffset, scalar.Memory[..size]);
                                    timestamp = EbmlInteger.ReadUnsigned(scalar.Memory.Span[..size], 0);
                                    break;
                                case 0xA3:
                                    packet = await VerifyBlockAsync(child, timestamp, blocks, bytes, packets, packet);
                                    break;
                                case 0xA0:
                                    long groupOffset = child.DataOffset;
                                    while (await elements.ReadAsync(groupOffset, child.EndOffset) is EbmlElement groupChild)
                                    {
                                        if (groupChild.Id == 0xA1)
                                        {
                                            packet = await VerifyBlockAsync(groupChild, timestamp, blocks, bytes, packets, packet);
                                        }
                                        groupOffset = groupChild.EndOffset!.Value;
                                    }
                                    break;
                            }
                            childOffset = child.EndOffset!.Value;
                        }
                    }
                    offset = element.EndOffset!.Value;
                }
                await segments.VerifyDocumentEndAsync(root);
                Require(packet == packets.GetArrayLength() && packet == reference.RootElement.GetProperty("packet_count").GetInt32());
                Require(allocator.AllocatedBytes == 3360);
            }
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
        }

        private static async Task<int> VerifyBlockAsync(EbmlElement block, ulong? clusterTimestamp,
            MatroskaBlockReader reader, byte[] source, JsonElement packets, int packet)
        {
            MatroskaBlockHeader header = await reader.ReadAsync(block);
            for (int index = 0; index < header.FrameCount; index++)
            {
                JsonElement expected = packets[packet++];
                MatroskaFrameRange frame = reader.GetFrame(index);
                Require(header.TrackNumber == expected.GetProperty("track_number").GetUInt64());
                Require(frame.Length == expected.GetProperty("size").GetInt32());
                Require(SHA256.HashData(source.AsSpan((int)frame.Offset, frame.Length)).AsSpan()
                    .SequenceEqual(Convert.FromHexString(expected.GetProperty("sha256").GetString()!)));
                if (index == 0)
                {
                    Require(clusterTimestamp is not null && (long)clusterTimestamp.Value + header.RelativeTimestamp == expected.GetProperty("pts").GetInt64());
                }
                if (expected.TryGetProperty("block_data_offset", out JsonElement position))
                {
                    Require(block.DataOffset == position.GetInt64());
                    Require(header.IsKeyFrame == expected.GetProperty("keyframe").GetBoolean());
                }
            }
            return packet;
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("A Matroska frame differs from the independent payload or stored timestamp reference.");
            }
        }
    }
}
