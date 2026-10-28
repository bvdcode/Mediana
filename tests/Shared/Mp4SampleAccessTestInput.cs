using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class Mp4SampleAccessTestInput
    {
        public static async Task VerifyAsync(string root, string file, Mp4TimestampMode mode)
        {
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, file + ".json")));
            Dictionary<int, JsonElement[]> expected = reference.RootElement.GetProperty("packets").EnumerateArray()
                .GroupBy(packet => packet.GetProperty("stream_index").GetInt32())
                .ToDictionary(group => group.Key, group => group.ToArray());
            MediaBufferAllocator allocator = new(1024 * 1024);
            using FileStream input = File.OpenRead(Path.Combine(root, file));
            await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = mode }))
            {
                long metadataBytes = allocator.AllocatedBytes;
                foreach ((int stream, JsonElement[] samples) in expected)
                {
                    for (int index = samples.Length - 1; index >= 0; index--)
                    {
                        JsonElement original = samples[index];
                        Mp4SampleInfo info = reader.GetSampleInfo(stream, index);
                        long origin = mode switch
                        {
                            Mp4TimestampMode.Media => 0,
                            Mp4TimestampMode.LinearMovie => reader.Tracks[stream].Edits[0].MediaTime,
                            _ => throw new ArgumentOutOfRangeException(nameof(mode))
                        };
                        long offset = long.Parse(original.GetProperty("pos").GetString()!, CultureInfo.InvariantCulture);
                        int size = int.Parse(original.GetProperty("size").GetString()!, CultureInfo.InvariantCulture);
                        if (info.StreamIndex != stream || info.SampleIndex != index || info.Offset != offset || info.Size != size ||
                            info.PresentationTime.Value != original.GetProperty("pts").GetInt64() - origin ||
                            info.DecodeTime.Value != original.GetProperty("dts").GetInt64() - origin ||
                            info.Duration.Value != original.GetProperty("duration").GetInt64() ||
                            info.IsSync != original.GetProperty("flags").GetString()!.StartsWith('K') ||
                            info.PresentationTime.TimeBase != reader.Streams[stream].TimeBase ||
                            info.DecodeTime.TimeBase != info.PresentationTime.TimeBase ||
                            info.Duration.TimeBase != info.PresentationTime.TimeBase || allocator.AllocatedBytes != metadataBytes)
                        {
                            throw new InvalidDataException("MP4 sample metadata differs from the independent packet reference.");
                        }
                        using (MediaPacket packet = await reader.ReadPacketAtAsync(stream, index))
                        {
                            VerifyPacket(packet, info, original);
                        }
                        if (allocator.AllocatedBytes != metadataBytes)
                        {
                            throw new InvalidOperationException("A random MP4 packet retained media storage after disposal.");
                        }
                    }
                }
                int[] positions = new int[reader.Streams.Count];
                while (await reader.ReadPacketAsync() is MediaPacket packet)
                {
                    using (packet)
                    {
                        int stream = packet.StreamIndex;
                        int index = positions[stream]++;
                        VerifyPacket(packet, reader.GetSampleInfo(stream, index), expected[stream][index]);
                    }
                }
                if (positions.Sum() != expected.Values.Sum(samples => samples.Length))
                {
                    throw new InvalidDataException("Random access changed the sequential MP4 cursors.");
                }
                using MediaPacket afterEnd = await reader.ReadPacketAtAsync(0, 0);
                VerifyPacket(afterEnd, reader.GetSampleInfo(0, 0), expected[0][0]);
                if (await reader.ReadPacketAsync() is not null)
                {
                    throw new InvalidDataException("Random access restarted an exhausted MP4 cursor.");
                }
            }
            if (allocator.AllocatedBytes != 0 || !input.CanRead)
            {
                throw new InvalidOperationException("MP4 sample access retained buffers or closed the borrowed source.");
            }
        }

        private static void VerifyPacket(MediaPacket packet, Mp4SampleInfo info, JsonElement original)
        {
            string hash = "SHA256:" + Convert.ToHexStringLower(SHA256.HashData(packet.Data.Span));
            if (packet.StreamIndex != info.StreamIndex || packet.Data.Length != info.Size ||
                packet.PresentationTime != info.PresentationTime || packet.DecodeTime != info.DecodeTime ||
                packet.Duration != info.Duration || packet.IsKeyFrame != info.IsSync ||
                hash != original.GetProperty("data_hash").GetString())
            {
                throw new InvalidDataException("A random or sequential MP4 read differs from sample metadata or independent bytes.");
            }
        }
    }
}
