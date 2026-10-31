// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class ServerVideoPreviewTestInput
    {
        public static async Task VerifyPngAsync(string root)
        {
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "server-preview.reference.json")));
            JsonElement file = reference.RootElement.GetProperty("files").EnumerateArray()
                .Single(file => file.GetProperty("file").GetString() == "preview-hd.mp4");
            JsonElement preview = file.GetProperty("preview");
            byte[] pixels = await File.ReadAllBytesAsync(Path.Combine(root, preview.GetProperty("file").GetString()!));
            if (pixels.Length != preview.GetProperty("bytes").GetInt32() ||
                Convert.ToHexStringLower(SHA256.HashData(pixels)) != preview.GetProperty("sha256").GetString())
            {
                throw new InvalidDataException("The independent HD sRGB preview reference changed.");
            }
            string destination = Path.Combine(Path.GetTempPath(), $"mediana-preview-{Guid.NewGuid():N}.png");
            try
            {
                if (await Sample.VideoPreviewSample.RunAsync(Path.Combine(root, "preview-hd.mp4"), destination) != 0)
                {
                    throw new InvalidOperationException("The server preview sample failed.");
                }
                int width = preview.GetProperty("width").GetInt32();
                int height = preview.GetProperty("height").GetInt32();
                PngTestInput.VerifyPreview(await File.ReadAllBytesAsync(destination), width, height, pixels, width,
                    (x, y) => (x, y), 1);
            }
            finally
            {
                File.Delete(destination);
            }
        }

        public static async Task VerifyAsync(string root, string name)
        {
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "server-preview.reference.json")));
            JsonElement expected = reference.RootElement.GetProperty("files").EnumerateArray()
                .Single(file => file.GetProperty("file").GetString() == name);
            string path = Path.Combine(root, name);
            if (Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path))) != expected.GetProperty("sha256").GetString())
            {
                throw new InvalidDataException("The server preview input differs from its independent reference.");
            }
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using FileStream input = File.OpenRead(path);
            await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
            await source.ReadAsync(new byte[5]);
            MediaInfo info = await engine.ProbeAsync(source);
            if (info.Container?.DeclaredDuration is not MediaDuration declared)
            {
                throw new InvalidDataException("The server preview input has no declared duration.");
            }
            MediaTimestamp duration = new(declared.Value, declared.TimeBase);
            if (declared != new MediaDuration(expected.GetProperty("declared_ticks").GetInt64(),
                new(1, expected.GetProperty("declared_scale").GetInt32())))
            {
                throw new InvalidDataException("The declared movie duration differs from the independently parsed movie header.");
            }
            long midpoint = duration.CompareTo(new(1, new(1, 1))) > 0
                ? duration.Rescale(new(1, 1_000_000), TimestampRounding.Floor).Value / 2
                : 0;
            if (midpoint != expected.GetProperty("midpoint_microseconds").GetInt64())
            {
                throw new InvalidDataException("The declared duration selects a different server preview target.");
            }
            await using VideoSession session = await engine.OpenVideoAsync(source,
                new MediaOpenOptions { BufferBudgetBytes = 64 * 1024 * 1024 });
            string[] clock = expected.GetProperty("time_base").GetString()!.Split('/');
            TimeBase timeBase = new(int.Parse(clock[0], System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(clock[1], System.Globalization.CultureInfo.InvariantCulture));
            foreach (JsonElement request in expected.GetProperty("requests").EnumerateArray())
            {
                MediaTimestamp target = new(request.GetProperty("target_microseconds").GetInt64(), new(1, 1_000_000));
                await session.SeekAsync(target);
                using VideoFrame? frame = await session.ReadFrameAtOrAfterAsync(target);
                JsonElement selected = request.GetProperty("selected");
                if (selected.ValueKind == JsonValueKind.Null)
                {
                    if (frame is not null)
                    {
                        throw new InvalidDataException("The server preview substituted a frame after the final presentation timestamp.");
                    }
                    continue;
                }
                if (frame is null || frame.Timestamp != new MediaTimestamp(selected.GetProperty("pts").GetInt64(), timeBase) ||
                    frame.Format.Width != expected.GetProperty("width").GetInt32() || frame.Format.Height != expected.GetProperty("height").GetInt32())
                {
                    throw new InvalidDataException($"The server preview selected the wrong picture in {name} at {target.Value} microseconds: expected {selected.GetProperty("pts").GetInt64()} at {timeBase}; actual {frame?.Timestamp?.Value} at {frame?.Timestamp?.TimeBase}.");
                }
                VerifyPixels(frame, selected.GetProperty("sha256").GetString()!);
            }
            await session.DisposeAsync();
            using (VideoFrame sample = await Sample.VideoPreviewSample.SelectFrameAsync(engine, source, info,
                new MediaOpenOptions { BufferBudgetBytes = 64 * 1024 * 1024 }))
            {
                JsonElement selected = expected.GetProperty("requests")[1].GetProperty("selected");
                if (sample.Timestamp != new MediaTimestamp(selected.GetProperty("pts").GetInt64(), timeBase))
                {
                    throw new InvalidDataException("The public preview sample selected the wrong presentation time.");
                }
                VerifyPixels(sample, selected.GetProperty("sha256").GetString()!);
            }
            if (session.AllocatedBufferBytes != 0 || input.Position != 5 || !input.CanRead)
            {
                throw new InvalidDataException("The server preview retained media storage or changed the borrowed source.");
            }
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
            if (Convert.ToHexStringLower(pixels.GetHashAndReset()) != expected)
            {
                throw new InvalidDataException("The complete server preview pixels differ from independent decoding.");
            }
        }
    }
}
