// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text.Json;
using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class Mp4IndexInfoTestInput
    {
        public static readonly string[] Names = ["avc-aac.mp4", "avc-tail.mov", "avc-mp3-22.mp4", "avc-mp3-44.mp4", "avc-timecode.mov", "avc-sound-v2.mov"];

        public static async Task VerifyAsync(string root, string file)
        {
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, file + ".json")));
            List<MediaInfo> snapshots = [];
            await using (FileStream input = File.OpenRead(Path.Combine(root, file)))
            {
                await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
                await source.ReadAsync(new byte[3]);
                foreach (Mp4TimestampMode mode in new[] { Mp4TimestampMode.Media, Mp4TimestampMode.LinearMovie })
                {
                    MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory(new() { TimestampMode = mode })).Build();
                    snapshots.Add(await engine.ProbeAsync(source));
                    if (input.Position != 3)
                    {
                        throw new InvalidDataException("Index metadata probing advanced the source cursor.");
                    }
                }
            }
            foreach (MediaInfo info in snapshots)
            {
                IReadOnlyDictionary<int, ContainerStreamInfo> tracks = info.Container!.StreamInfo
                    ?? throw new InvalidDataException("MP4 packet-index metadata is unavailable.");
                if (tracks.Count != info.Streams.Count)
                {
                    throw new InvalidDataException("MP4 track metadata does not cover every stream.");
                }
                foreach (JsonElement expected in reference.RootElement.GetProperty("streams").EnumerateArray())
                {
                    int index = expected.GetProperty("index").GetInt32();
                    string[] clock = expected.GetProperty("time_base").GetString()!.Split('/');
                    TimeBase timeBase = new(int.Parse(clock[0], CultureInfo.InvariantCulture), int.Parse(clock[1], CultureInfo.InvariantCulture));
                    JsonElement[] packets = reference.RootElement.GetProperty("packets").EnumerateArray()
                        .Where(packet => packet.GetProperty("stream_index").GetInt32() == index).ToArray();
                    ContainerPacketIndexInfo facts = tracks[index].PacketIndex
                        ?? throw new InvalidDataException("MP4 track packet-index totals are unavailable.");
                    long bytes = packets.Sum(packet => long.Parse(packet.GetProperty("size").GetString()!, CultureInfo.InvariantCulture));
                    long duration = packets.Sum(packet => packet.GetProperty("duration").GetInt64());
                    MediaDuration expectedDuration = new(duration, timeBase);
                    EncodedStreamInfo stream = info.Streams.Single(stream => stream.StreamIndex == index);
                    if (facts.PacketCount != packets.Length || facts.PayloadBytes != bytes ||
                        facts.DecodeDuration != expectedDuration || stream.CodedDuration != expectedDuration)
                    {
                        throw new InvalidDataException($"MP4 packet-index totals differ from independent packets for {file}, stream {index}.");
                    }
                    double expectedRate = 8.0 * bytes / (duration * (double)timeBase.Numerator / timeBase.Denominator);
                    if (facts.AveragePayloadBitRate is not double actualRate ||
                        Math.Abs(actualRate - expectedRate) > Math.Max(1, expectedRate) * 1e-12)
                    {
                        throw new InvalidDataException($"MP4 mean indexed payload rate differs from independent packets for {file}, stream {index}.");
                    }
                }
            }
        }
    }
}
