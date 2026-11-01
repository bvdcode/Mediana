// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs.H264;
using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class Mp4FrameRateTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "frame-rate.reference.json")));
            foreach (JsonElement file in reference.RootElement.GetProperty("files").EnumerateArray())
            {
                string path = Path.Combine(root, file.GetProperty("file").GetString()!);
                if (Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path))) != file.GetProperty("sha256").GetString())
                {
                    throw new InvalidDataException("The frame-rate fixture changed.");
                }
                foreach (Mp4TimestampMode mode in new[] { Mp4TimestampMode.Media, Mp4TimestampMode.LinearMovie })
                {
                    MediaBufferAllocator allocator = new(1024 * 1024);
                    await using FileStream stream = File.OpenRead(path);
                    await using RandomAccessStreamByteSource source = new(stream, leaveOpen: true);
                    await source.ReadAsync(new byte[5]);
                    ContainerInfo info;
                    await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = mode }))
                    {
                        info = reader.Info;
                        VerifyInfo(info, reader, file.GetProperty("tracks"));
                        if (mode == Mp4TimestampMode.Media)
                        {
                            await Mp4TrackMetadataTestInput.VerifyPacketsAsync(reader, file.GetProperty("tracks"));
                        }
                    }
                    VerifyInfo(info, null, file.GetProperty("tracks"));
                    if (allocator.AllocatedBytes != 0 || stream.Position != 5 || !stream.CanRead)
                    {
                        throw new InvalidDataException("Frame-rate inspection changed ownership or retained indexed media storage.");
                    }
                    MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory(new() { TimestampMode = mode })).Build();
                    MediaInfo probe = await engine.ProbeAsync(source);
                    VerifyInfo(probe.Container!, null, file.GetProperty("tracks"));
                    if (stream.Position != 5)
                    {
                        throw new InvalidDataException("Average-rate probing advanced the borrowed cursor.");
                    }
                }
                await VerifyPicturesAsync(path, file.GetProperty("video_decode"));
            }
        }

        private static async Task VerifyPicturesAsync(string path, JsonElement expected)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using FileStream input = File.OpenRead(path);
            await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
            await source.ReadAsync(new byte[5]);
            await using VideoSession session = await engine.OpenVideoAsync(source, expected.GetProperty("stream_index").GetInt32(),
                new MediaOpenOptions { BufferBudgetBytes = 1024 * 1024 });
            JsonElement pts = expected.GetProperty("pts");
            TimeBase timeBase = new(1, expected.GetProperty("time_scale").GetInt32());
            using IncrementalHash pixels = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int count = 0;
            long bytes = 0;
            while (true)
            {
                using VideoFrame? frame = await session.ReadFrameAsync();
                if (frame is null)
                {
                    break;
                }
                if (count >= pts.GetArrayLength() || frame.Timestamp != new MediaTimestamp(pts[count].GetInt64(), timeBase) ||
                    frame.Format.Width != expected.GetProperty("width").GetInt32() || frame.Format.Height != expected.GetProperty("height").GetInt32())
                {
                    throw new InvalidDataException("A fractional/VFR decoded picture changed presentation order, timestamp or dimensions.");
                }
                for (int plane = 0; plane < frame.Planes.Count; plane++)
                {
                    for (int row = 0; row < frame.Planes[plane].Height; row++)
                    {
                        ReadOnlySpan<byte> values = frame.GetRow(plane, row);
                        pixels.AppendData(values);
                        bytes += values.Length;
                    }
                }
                count++;
            }
            await session.DisposeAsync();
            if (count != pts.GetArrayLength() || bytes != expected.GetProperty("bytes").GetInt64() ||
                Convert.ToHexStringLower(pixels.GetHashAndReset()) != expected.GetProperty("sha256").GetString() ||
                session.AllocatedBufferBytes != 0 || input.Position != 5 || !input.CanRead)
            {
                throw new InvalidDataException("The complete CFR/VFR/edited decoded pixels, frame count or media ownership changed.");
            }
        }

        private static void VerifyInfo(ContainerInfo info, Mp4Reader? reader, JsonElement expected)
        {
            foreach (JsonElement track in expected.EnumerateArray())
            {
                int index = track.GetProperty("index").GetInt32();
                FrameRate? rate = track.GetProperty("kind").GetString() switch
                {
                    "video" => new FrameRate(track.GetProperty("rate_numerator").GetInt64(), track.GetProperty("rate_denominator").GetInt64()),
                    "audio" => null,
                    _ => throw new InvalidDataException("The frame-rate reference has an unsupported track kind.")
                };
                ContainerStreamInfo actual = info.StreamInfo![index];
                MediaDuration duration = new(track.GetProperty("decode_ticks").GetInt64(), new(1, track.GetProperty("time_scale").GetInt32()));
                MediaDuration declared = new(track.GetProperty("declared_track_ticks").GetInt64(), new(1, track.GetProperty("movie_time_scale").GetInt32()));
                if (actual.AverageFrameRate != rate || actual.PacketIndex!.DecodeDuration != duration ||
                    actual.PacketIndex.PacketCount != track.GetProperty("sample_count").GetInt64() || actual.DeclaredDuration != declared)
                {
                    throw new InvalidDataException("The indexed video rate or its exact count/duration differs from the independent reference.");
                }
                if (reader is not null && (reader.Tracks[index].AverageFrameRate != rate ||
                    Convert.ToHexStringLower(SHA256.HashData(reader.Streams[index].Configuration.Span)) != track.GetProperty("configuration_sha256").GetString()))
                {
                    throw new InvalidDataException("The native track rate or codec configuration changed.");
                }
            }
        }
    }
}
