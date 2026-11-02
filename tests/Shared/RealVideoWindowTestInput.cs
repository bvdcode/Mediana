// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class RealVideoWindowTestInput
    {
        public static async Task VerifyAsync(string root, string name)
        {
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "video-window.reference.json")));
            JsonElement file = reference.RootElement.GetProperty("files").EnumerateArray().Single(item => item.GetProperty("file").GetString() == name);
            string path = Path.Combine(root, name);
            Require(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path))) == file.GetProperty("sha256").GetString());
            string[] clock = file.GetProperty("time_base").GetString()!.Split('/');
            TimeBase timeBase = new(int.Parse(clock[0], CultureInfo.InvariantCulture), int.Parse(clock[1], CultureInfo.InvariantCulture));
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using FileStream input = File.OpenRead(path);
            await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
            await source.ReadAsync(new byte[5]);
            await using VideoSession session = await engine.OpenVideoAsync(source,
                new MediaOpenOptions { BufferBudgetBytes = 64 * 1024 * 1024 });
            foreach ((int from, int until) in new[] { (700, 1500), (0, 200), (500, 701), (200, 500), (2000, 4000), (1, 100), (500, 701), (0, 4000) })
            {
                MediaTimestamp start = new(from, new(1, 1000));
                MediaTimestamp end = new(until * 90L, new(1, 90000));
                JsonElement[] expected = file.GetProperty("frames").EnumerateArray().Where(frame =>
                {
                    MediaTimestamp timestamp = new(frame.GetProperty("movie_pts").GetInt64(), timeBase);
                    return timestamp.CompareTo(start) >= 0 && timestamp.CompareTo(end) < 0;
                }).ToArray();
                await session.SeekAsync(start);
                await using VideoTrimmer window = new(session.ReadFrameAsync, start, end);
                VideoFrame? retained = null;
                try
                {
                    foreach (JsonElement picture in expected)
                    {
                        using VideoFrame? frame = await window.ReadFrameAsync();
                        Require(frame is not null && frame.Timestamp == new MediaTimestamp(picture.GetProperty("movie_pts").GetInt64(), timeBase));
                        Require(frame!.Duration == new MediaDuration(picture.GetProperty("presentation_duration").GetInt64(), timeBase));
                        Require(frame!.Format.Width == file.GetProperty("width").GetInt32() && frame.Format.Height == file.GetProperty("height").GetInt32());
                        VerifyPixels(frame, picture.GetProperty("sha256").GetString()!);
                        retained?.Dispose();
                        retained = frame.Retain();
                    }
                    Require(await window.ReadFrameAsync() is null && await window.ReadFrameAsync() is null);
                    await window.DisposeAsync();
                    if (retained is not null)
                    {
                        VerifyPixels(retained, expected[^1].GetProperty("sha256").GetString()!);
                    }
                }
                finally
                {
                    retained?.Dispose();
                }
            }
            await session.DisposeAsync();
            Require(session.AllocatedBufferBytes == 0 && input.Position == 5 && input.CanRead);
        }

        private static void VerifyPixels(VideoFrame frame, string expected)
        {
            using IncrementalHash pixels = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (int plane = 0; plane < frame.Planes.Count; plane++)
            {
                for (int row = 0; row < frame.Planes[plane].Height; row++)
                {
                    pixels.AppendData(frame.GetRow(plane, row));
                }
            }
            Require(Convert.ToHexStringLower(pixels.GetHashAndReset()) == expected);
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("The real video window changed independently decoded pictures, presentation times or resource ownership.");
            }
        }
    }
}
