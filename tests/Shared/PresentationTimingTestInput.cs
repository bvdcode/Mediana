// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class PresentationTimingTestInput
    {
        public static async Task VerifyReferenceAsync(string root, string name, Mp4TimestampMode mode)
        {
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "video-window.reference.json")));
            JsonElement file = reference.RootElement.GetProperty("files").EnumerateArray().Single(item => item.GetProperty("file").GetString() == name);
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(root, name));
            Require(Convert.ToHexStringLower(SHA256.HashData(bytes)) == file.GetProperty("sha256").GetString());
            TrackingRandomAccessSource source = new(bytes);
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = mode });
            string[] parts = file.GetProperty("time_base").GetString()!.Split('/');
            TimeBase clock = new(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
            long origin = mode switch
            {
                Mp4TimestampMode.Media => 0,
                Mp4TimestampMode.LinearMovie => file.GetProperty("edit_media_start").GetInt64(),
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
            int count = file.GetProperty("frames").GetArrayLength();
            long originalStorage = allocator.AllocatedBytes;
            int reads = source.Reads.Count;
            foreach (JsonElement frame in file.GetProperty("frames").EnumerateArray().Reverse())
            {
                int index = frame.GetProperty("sample_index").GetInt32();
                MediaDuration duration = new(frame.GetProperty("presentation_duration").GetInt64(), clock);
                Require(reader.GetPresentationDuration(0, index) == duration && source.Reads.Count == reads);
                Mp4SampleInfo raw = reader.GetSampleInfo(0, index);
                Require(raw.Duration == new MediaDuration(frame.GetProperty("decode_duration").GetInt64(), clock));
                Require(raw.PresentationTime == new MediaTimestamp(frame.GetProperty("media_pts").GetInt64() - origin, clock));
            }
            Require(allocator.AllocatedBytes == originalStorage + (name is "frame-rate-vfr.mp4" or "frame-rate-edited.mp4" ? count * sizeof(long) : 0));
            using (MediaPacket? first = await reader.ReadPacketAsync())
            {
                Require(first?.DecodeTime == reader.GetSampleInfo(0, 0).DecodeTime);
            }
            await reader.DisposeAsync();
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
            bool disposed = false;
            try { reader.GetPresentationDuration(0, 0); }
            catch (ObjectDisposedException) { disposed = true; }
            Require(disposed);
        }

        public static async Task VerifyLimitsAsync(string root)
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(root, "frame-rate-vfr.mp4"));
            MediaBufferAllocator probeAllocator = new(1024 * 1024);
            long metadataBytes;
            int count;
            await using (Mp4Reader probe = await Mp4Reader.OpenAsync(new TrackingRandomAccessSource(bytes), probeAllocator))
            {
                metadataBytes = probeAllocator.AllocatedBytes;
                count = probe.Tracks[0].SampleCount;
            }
            MediaBufferAllocator allocator = new(metadataBytes + count * 20);
            TrackingRandomAccessSource source = new(bytes);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            using (WritableMediaBuffer blocker = allocator.Allocate(1))
            {
                bool limited = false;
                try { reader.GetPresentationDuration(0, 0); }
                catch (MediaMemoryLimitException) { limited = true; }
                Require(limited && allocator.AllocatedBytes == metadataBytes + 1);
            }
            using CancellationTokenSource cancelled = new();
            await cancelled.CancelAsync();
            bool cancellation = false;
            try { reader.GetPresentationDuration(0, 0, cancelled.Token); }
            catch (OperationCanceledException) { cancellation = true; }
            Require(cancellation && allocator.AllocatedBytes == metadataBytes);
            int reads = source.Reads.Count;
            Require(reader.GetPresentationDuration(0, 0) is not null);
            Require(allocator.AllocatedBytes == metadataBytes + count * sizeof(long) && source.Reads.Count == reads);
            for (int index = 0; index < 100; index++)
            {
                reader.GetPresentationDuration(0, index % count);
            }
            Require(allocator.AllocatedBytes == metadataBytes + count * sizeof(long) && source.Reads.Count == reads);
            await reader.DisposeAsync();
            Require(allocator.AllocatedBytes == 0);
        }

        public static async Task VerifySignedAndUnknownAsync(string root)
        {
            byte[] original = await File.ReadAllBytesAsync(Path.Combine(root, "frame-rate-vfr.mp4"));
            byte[] shifted = original.ToArray();
            int composition = shifted.AsSpan().IndexOf("ctts"u8) + 4;
            int entries = BinaryPrimitives.ReadInt32BigEndian(shifted.AsSpan(composition + 4));
            shifted[composition] = 1;
            for (int index = 0; index < entries; index++)
            {
                int offset = composition + 12 + index * 8;
                int value = BinaryPrimitives.ReadInt32BigEndian(shifted.AsSpan(offset));
                BinaryPrimitives.WriteInt32BigEndian(shifted.AsSpan(offset), value - 90000);
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using (Mp4Reader first = await Mp4Reader.OpenAsync(new TrackingRandomAccessSource(original), allocator))
            await using (Mp4Reader second = await Mp4Reader.OpenAsync(new TrackingRandomAccessSource(shifted), allocator))
            {
                for (int index = 0; index < first.Tracks[0].SampleCount; index++)
                {
                    Require(first.GetPresentationDuration(0, index) == second.GetPresentationDuration(0, index));
                    Require(second.GetSampleInfo(0, index).PresentationTime.Value == first.GetSampleInfo(0, index).PresentationTime.Value - 90000);
                }
            }
            byte[] duplicate = await File.ReadAllBytesAsync(Path.Combine(root, "frame-rate-cfr.mp4"));
            composition = duplicate.AsSpan().IndexOf("ctts"u8) + 4;
            Require(BinaryPrimitives.ReadUInt32BigEndian(duplicate.AsSpan(composition + 8)) == 1);
            Require(BinaryPrimitives.ReadUInt32BigEndian(duplicate.AsSpan(composition + 16)) == 1);
            BinaryPrimitives.WriteUInt32BigEndian(duplicate.AsSpan(composition + 20), 3003);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(new TrackingRandomAccessSource(duplicate), allocator))
            {
                Require(reader.GetSampleInfo(0, 0).PresentationTime == reader.GetSampleInfo(0, 1).PresentationTime);
                Require(reader.GetPresentationDuration(0, 0) is null && reader.GetPresentationDuration(0, 1) is null);
                using MediaPacket packet = await reader.ReadPacketAtAsync(0, 0);
                Require(packet.Duration == new MediaDuration(3003, new(1, 90000)));
            }
            Require(allocator.AllocatedBytes == 0);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using VideoSession session = await engine.OpenVideoAsync(new TrackingRandomAccessSource(duplicate));
            int unknownFrames = 0;
            while (true)
            {
                using VideoFrame? frame = await session.ReadFrameAsync();
                if (frame is null)
                {
                    break;
                }
                if (frame.Timestamp == new MediaTimestamp(6006, new(1, 90000)))
                {
                    Require(frame.Duration is null);
                    unknownFrames++;
                }
            }
            Require(unknownFrames == 2);
            await session.DisposeAsync();
            Require(session.AllocatedBufferBytes == 0);
        }

        public static void VerifyRetainedPacket()
        {
            MediaBufferAllocator allocator = new(4);
            using WritableMediaBuffer writable = allocator.Allocate(4);
            new byte[] { 1, 2, 3, 4 }.CopyTo(writable.Memory.Span);
            using MediaBuffer buffer = writable.Publish();
            using MediaPacket source = new(2, buffer, new(7, new(1, 100)), new(3, new(1, 100)), new(50, new(1, 1000)), true);
            using MediaPacket corrected = source.RetainWithDuration(new(20, new(1, 1000)));
            using MediaPacket unknown = source.RetainWithDuration(null);
            Require(corrected.StreamIndex == source.StreamIndex && corrected.PresentationTime == source.PresentationTime &&
                corrected.DecodeTime == source.DecodeTime && corrected.IsKeyFrame == source.IsKeyFrame);
            Require(source.Duration == new MediaDuration(50, new(1, 1000)) && corrected.Duration == new MediaDuration(20, new(1, 1000)) && unknown.Duration is null);
            source.Dispose();
            buffer.Dispose();
            Require(corrected.Data.Span.SequenceEqual(new byte[] { 1, 2, 3, 4 }) && unknown.Data.Equals(corrected.Data));
            corrected.Dispose();
            Require(allocator.AllocatedBytes == 4);
            unknown.Dispose();
            Require(allocator.AllocatedBytes == 0);
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("Presentation timing changed independent durations, raw packet facts, source cursors or budgeted storage.");
            }
        }
    }
}
