// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class Mp4TrackMetadataTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            string file = Path.Combine(root, "track-metadata.mp4");
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "track-metadata.reference.json")));
            JsonElement expected = reference.RootElement;
            if (Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(file))) != expected.GetProperty("sha256").GetString())
            {
                throw new InvalidDataException("The track metadata fixture changed.");
            }
            Mp4ReadOptions limits = new()
            {
                MaxMetadataValues = expected.GetProperty("metadata_values").GetInt32(),
                MaxMetadataTextBytes = expected.GetProperty("metadata_text_bytes").GetInt32()
            };
            MediaInfo info;
            await using (RandomAccessStreamByteSource input = new(File.OpenRead(file)))
            {
                MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory(limits)).Build();
                info = await engine.ProbeAsync(input);
            }
            VerifyCommonTags(info.Container!.Tags!, expected.GetProperty("tags"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            MediaBuffer? retained = null;
            try
            {
                await using FileStream stream = File.OpenRead(file);
                await using RandomAccessStreamByteSource source = new(stream, leaveOpen: true);
                await source.ReadAsync(new byte[3]);
                await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, limits))
                {
                    VerifyItems(reader.Metadata, expected.GetProperty("tags"), reader.ReadMetadataValue);
                    foreach (JsonElement track in expected.GetProperty("tracks").EnumerateArray())
                    {
                        int index = track.GetProperty("index").GetInt32();
                        Mp4TrackInfo native = reader.Tracks[index];
                        if (native.Language.Code != track.GetProperty("code").GetUInt16() ||
                            native.Language.ExtendedTag != track.GetProperty("extended_tag").GetString() ||
                            native.Language.Language != track.GetProperty("language").GetString() ||
                            info.Container.StreamInfo![index].Language != native.Language.Language)
                        {
                            throw new InvalidDataException("A track language declaration was lost or mixed with another track.");
                        }
                        VerifyItems(native.Metadata, track.GetProperty("tags"), item => reader.ReadTrackMetadataValue(index, item));
                        VerifyCommonTags(info.Container.StreamInfo[index].Tags!, track.GetProperty("tags"));
                    }
                    retained = reader.ReadTrackMetadataValue(2, 3);
                    await VerifyPacketsAsync(reader, expected.GetProperty("tracks"));
                    if (stream.Position != 3)
                    {
                        throw new InvalidDataException("Track metadata access advanced the borrowed source cursor.");
                    }
                }
                if (!stream.CanRead || allocator.AllocatedBytes == 0 || !retained.Memory.Span.SequenceEqual(new byte[] { 0, 2, 255 }))
                {
                    throw new InvalidDataException("Owned track metadata did not survive reader disposal.");
                }
            }
            finally
            {
                retained?.Dispose();
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Track metadata leaked retained storage.");
            }
        }

        private static void VerifyItems(IReadOnlyList<Mp4MetadataItem> items, JsonElement expected, Func<int, MediaBuffer> read)
        {
            if (items.Count != expected.GetArrayLength())
            {
                throw new InvalidDataException("Scoped metadata item count differs.");
            }
            for (int index = 0; index < items.Count; index++)
            {
                JsonElement value = expected[index];
                Mp4MetadataItem item = items[index];
                using MediaBuffer raw = read(index);
                if (item.Namespace != value.GetProperty("namespace").GetString() || item.Name != value.GetProperty("name").GetString() ||
                    item.DataType != value.GetProperty("type").GetUInt32() || item.Locale != value.GetProperty("locale").GetUInt32() ||
                    item.Text != value.GetProperty("text").GetString() || item.SourcePath != value.GetProperty("source_path").GetString() ||
                    item.ByteLength != value.GetProperty("byte_length").GetInt32() || raw.Memory.Length != item.ByteLength ||
                    Convert.ToHexStringLower(SHA256.HashData(raw.Memory.Span)) != value.GetProperty("sha256").GetString())
                {
                    throw new InvalidDataException("A native metadata item or original encoded value differs.");
                }
            }
        }

        private static void VerifyCommonTags(IReadOnlyList<MediaTag> actual, JsonElement expected)
        {
            JsonElement[] text = expected.EnumerateArray().Where(value => value.GetProperty("text").ValueKind != JsonValueKind.Null).ToArray();
            if (actual.Count != text.Length)
            {
                throw new InvalidDataException("Common metadata tags lost their scope or included binary values.");
            }
            for (int index = 0; index < actual.Count; index++)
            {
                MediaTag tag = actual[index];
                JsonElement value = text[index];
                if (tag.Namespace != value.GetProperty("namespace").GetString() || tag.Name != value.GetProperty("name").GetString() ||
                    tag.SourcePath != value.GetProperty("source_path").GetString() || tag.Locale != value.GetProperty("locale").GetUInt32() ||
                    tag.Values.Count != 1 || tag.Values[0] != value.GetProperty("text").GetString())
                {
                    throw new InvalidDataException("Common metadata projection changed a native tag.");
                }
            }
        }

        private static async Task VerifyPacketsAsync(Mp4Reader reader, JsonElement tracks)
        {
            IncrementalHash[] hashes = Enumerable.Range(0, tracks.GetArrayLength()).Select(_ => IncrementalHash.CreateHash(HashAlgorithmName.SHA256)).ToArray();
            try
            {
                long[] counts = new long[hashes.Length];
                byte[] timing = new byte[28];
                while (await reader.ReadPacketAsync() is MediaPacket packet)
                {
                    using (packet)
                    {
                        BinaryPrimitives.WriteInt64LittleEndian(timing, packet.PresentationTime!.Value.Value);
                        BinaryPrimitives.WriteInt64LittleEndian(timing.AsSpan(8), packet.DecodeTime!.Value.Value);
                        BinaryPrimitives.WriteInt64LittleEndian(timing.AsSpan(16), packet.Duration!.Value.Value);
                        BinaryPrimitives.WriteInt32LittleEndian(timing.AsSpan(24), packet.Data.Length);
                        hashes[packet.StreamIndex].AppendData(timing);
                        hashes[packet.StreamIndex].AppendData(packet.Data.Span);
                        counts[packet.StreamIndex]++;
                    }
                }
                for (int index = 0; index < hashes.Length; index++)
                {
                    if (counts[index] != tracks[index].GetProperty("packet_count").GetInt64() ||
                        Convert.ToHexStringLower(hashes[index].GetHashAndReset()) != tracks[index].GetProperty("packet_sha256").GetString())
                    {
                        throw new InvalidDataException("Metadata access changed encoded packets or their timestamps.");
                    }
                }
            }
            finally
            {
                foreach (IncrementalHash hash in hashes)
                {
                    hash.Dispose();
                }
            }
        }
    }
}
