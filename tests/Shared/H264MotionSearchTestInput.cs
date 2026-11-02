// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    internal static class H264MotionSearchTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "MOTION-SEARCH.json")));
            foreach (JsonProperty file in manifest.RootElement.GetProperty("files").EnumerateObject())
            {
                byte[] content = await File.ReadAllBytesAsync(Path.Combine(root, file.Name));
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(content)), file.Value.GetString(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("A motion-search source fixture has changed.");
                }
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            int count = 0;
            foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
            {
                byte[] referenceBytes = await File.ReadAllBytesAsync(Path.Combine(root, $"motion-reference-{item.GetProperty("pattern").GetInt32()}.yuv"));
                byte[] inputBytes = await File.ReadAllBytesAsync(Path.Combine(root, item.GetProperty("source").GetString()!));
                Verify(item, referenceBytes, inputBytes, allocator);
                count++;
            }
            if (count != 188 || allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Motion-search regression count or retained storage differs.");
            }
        }

        private static void Verify(JsonElement item, byte[] referenceBytes, byte[] inputBytes, MediaBufferAllocator allocator)
        {
            H264SequenceParameterSet sequence = new() { WidthInMbs = 2, HeightInMapUnits = 2, FrameMbsOnly = true, Width = 32, Height = 32 };
            using H264Picture reference = new(sequence, allocator, H264SliceKind.P);
            using H264Picture target = new(sequence, allocator, H264SliceKind.P);
            int start = 0;
            for (int plane = 0; plane < 3; plane++)
            {
                referenceBytes.AsSpan(start, reference.Plane(plane).Length).CopyTo(reference.Plane(plane));
                start += reference.Plane(plane).Length;
            }
            int px = item.GetProperty("predicted_x").GetInt32();
            int py = item.GetProperty("predicted_y").GetInt32();
            target.SetMotion(H264ReferenceList.L0, 0, 0, 32, 32, 0, px, py);
            bool padded = item.GetProperty("padded").GetBoolean();
            PlaneLayout[] layout = new PlaneLayout[3];
            int offset = 3;
            for (int plane = 0; plane < 3; plane++)
            {
                int width = plane == 0 ? 32 : 16;
                int stride = width + (padded ? 7 : 0);
                layout[plane] = new PlaneLayout(offset, stride, width, width);
                offset += stride * (width - 1) + width;
            }
            using WritableMediaBuffer writable = allocator.Allocate(offset);
            writable.Memory.Span.Fill(183);
            start = 0;
            foreach (PlaneLayout plane in layout)
            {
                for (int row = 0; row < plane.Height; row++)
                {
                    inputBytes.AsSpan(start, plane.RowBytes).CopyTo(writable.Memory.Span.Slice(plane.Offset + row * plane.Stride));
                    start += plane.RowBytes;
                }
            }
            using MediaBuffer storage = writable.Publish();
            byte[] unchanged = storage.Memory.ToArray();
            using VideoFrame input = new(new VideoFormat(32, 32, VideoPixelFormat.Yuv420P8), storage, layout);
            byte[] prediction = new byte[390];
            prediction.AsSpan().Fill(183);
            int x = item.GetProperty("x").GetInt32();
            int y = item.GetProperty("y").GetInt32();
            if (H264MotionPrediction.Predict(target, H264ReferenceList.L0, x, y, 16, 16, 0, 0) != (px, py))
            {
                throw new InvalidDataException("The motion-search fixture has an unexpected predictor.");
            }
            Span<byte> samples = stackalloc byte[H264SourceMacroblock.ByteCount];
            H264SourceMacroblock source = new(input, x, y, samples);
            (int vx, int vy, bool exact) = H264MotionSearch.Select(source, reference, target, x, y, item.GetProperty("radius").GetInt32(), prediction.AsSpan(3, 384), default);
            if (vx != item.GetProperty("expected_x").GetInt32() || vy != item.GetProperty("expected_y").GetInt32() || exact != item.GetProperty("exact").GetBoolean()
                || !string.Equals(Convert.ToHexString(SHA256.HashData(prediction.AsSpan(3, 384))), item.GetProperty("prediction_sha256").GetString(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Motion search differs from the independent complete candidate reference.");
            }
            if (prediction.AsSpan(0, 3).ContainsAnyExcept((byte)183) || prediction.AsSpan(387, 3).ContainsAnyExcept((byte)183)
                || !storage.Memory.Span.SequenceEqual(unchanged) || target.Modes.ContainsAnyExcept((byte)(H264Picture.InterMode | 128))
                || target.ReferenceIndices(H264ReferenceList.L0).ContainsAnyExcept((byte)0) || target.MotionX(H264ReferenceList.L0).ContainsAnyExcept(px)
                || target.MotionY(H264ReferenceList.L0).ContainsAnyExcept(py))
            {
                throw new InvalidDataException("Motion search changed input storage, output guards or target motion state.");
            }
            start = 0;
            for (int plane = 0; plane < 3; plane++)
            {
                if (!reference.Plane(plane).SequenceEqual(referenceBytes.AsSpan(start, reference.Plane(plane).Length))
                    || target.Plane(plane).ContainsAnyExcept((byte)0) || target.Counts(plane).ContainsAnyExcept((byte)0))
                {
                    throw new InvalidDataException("Motion search changed reference/target samples or counts.");
                }
                start += reference.Plane(plane).Length;
            }
        }
    }
}
