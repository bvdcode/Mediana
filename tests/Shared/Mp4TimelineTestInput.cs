// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class Mp4TimelineTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "avc-aac.mp4.json")));
            Dictionary<int, Queue<JsonElement>> expected = reference.RootElement.GetProperty("packets").EnumerateArray()
                .GroupBy(packet => packet.GetProperty("stream_index").GetInt32())
                .ToDictionary(group => group.Key, group => new Queue<JsonElement>(group));
            MediaBufferAllocator allocator = new(1024 * 1024);
            using FileStream input = File.OpenRead(Path.Combine(root, "avc-aac.mp4"));
            await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
            Mp4ReadOptions options = new() { TimestampMode = Mp4TimestampMode.LinearMovie };
            MediaTimestamp? previous = null;
            List<int> order = [];
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, options))
            {
                if (reader.TimestampMode != Mp4TimestampMode.LinearMovie || reader.Tracks[0].Edits[0].MediaTime != 4096 ||
                    reader.Tracks[1].Edits[0].MediaTime != 1024)
                {
                    throw new InvalidDataException("The AVC/AAC source does not match its original edit declarations.");
                }
                while (await reader.ReadPacketAsync() is MediaPacket packet)
                {
                    using (packet)
                    {
                        JsonElement original = expected[packet.StreamIndex].Dequeue();
                        long origin = packet.StreamIndex switch
                        {
                            0 => 4096,
                            1 => 1024,
                            _ => throw new InvalidDataException("Unexpected AVC/AAC reference stream.")
                        };
                        MediaTimestamp pts = packet.PresentationTime!.Value;
                        MediaTimestamp dts = packet.DecodeTime!.Value;
                        if (pts.Value != original.GetProperty("pts").GetInt64() - origin ||
                            dts.Value != original.GetProperty("dts").GetInt64() - origin ||
                            packet.Duration!.Value.Value != original.GetProperty("duration").GetInt64() ||
                            pts.TimeBase != reader.Streams[packet.StreamIndex].TimeBase ||
                            dts.TimeBase != pts.TimeBase || packet.Duration.Value.TimeBase != pts.TimeBase ||
                            (previous is not null && dts.CompareTo(previous.Value) < 0))
                        {
                            throw new InvalidDataException("Linear movie timestamps or merged decode order differ from the independent packet reference.");
                        }
                        string hash = "SHA256:" + Convert.ToHexStringLower(SHA256.HashData(packet.Data.Span));
                        if (hash != original.GetProperty("data_hash").GetString() ||
                            packet.IsKeyFrame != original.GetProperty("flags").GetString()!.StartsWith('K'))
                        {
                            throw new InvalidDataException("Timeline translation changed compressed bytes or sync indications.");
                        }
                        order.Add(packet.StreamIndex);
                        previous = dts;
                    }
                }
            }
            if (order.Count != 84 || !order.Take(5).SequenceEqual(new[] { 0, 0, 1, 0, 1 }) ||
                expected.Values.Any(queue => queue.Count != 0) || allocator.AllocatedBytes != 0 || !input.CanRead)
            {
                throw new InvalidDataException("Linear movie reading omitted samples, retained buffers or changed source ownership.");
            }
        }
    }
}
