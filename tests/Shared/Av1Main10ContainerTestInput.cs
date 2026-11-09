// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Security.Cryptography;
using Mediana.Codecs.Av1;
using Mediana.Containers.Matroska;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class Av1Main10ContainerTestInput
    {
        internal static async Task VerifyAsync(string root, string extension)
        {
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, "main10Default.sha256"));
            Require(reference.AsSpan(0, 8).SequenceEqual("AV1HASH1"u8));
            int count = BinaryPrimitives.ReadInt32LittleEndian(reference.AsSpan(8));
            Require(count == 12 && reference.Length == 16 + count * SHA256.HashSizeInBytes);
            await using FileStream file = File.OpenRead(Path.Combine(root, "main10Default." + extension));
            await using RandomAccessStreamByteSource source = new(file, leaveOpen: true);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddContainer(new MatroskaReaderFactory()).AddVideoDecoder(new Av1DecoderFactory()).Build();
            await using (VideoSession session = await engine.OpenVideoAsync(source, allocator, new(), null))
            {
                Require(session.CanSeek && session.Stream.CodecId == "av1");
                List<MediaTimestamp> times = [];
                while (await session.ReadFrameAsync() is VideoFrame frame)
                {
                    using (frame)
                    {
                        VerifyPixels(frame, reference, times.Count, hash);
                        times.Add(frame.Timestamp ?? throw new InvalidDataException("A ten-bit AV1 frame lost its container timestamp."));
                        Require(frame.Duration is not null);
                    }
                }
                Require(times.Count == count);
                foreach (int requested in new[] { 11, 0, 5, 2, 11 })
                {
                    await session.SeekAsync(times[requested]);
                    using VideoFrame frame = await session.ReadFrameAtOrAfterAsync(times[requested])
                        ?? throw new InvalidDataException("An indexed ten-bit AV1 seek lost its selected frame.");
                    Require(frame.Timestamp == times[requested]);
                    VerifyPixels(frame, reference, requested, hash);
                }
                Require(await session.ReadFrameAsync() is null);
            }
            Require(allocator.AllocatedBytes == 0 && file.Position == 0);
        }

        private static void VerifyPixels(VideoFrame frame, byte[] reference, int index, IncrementalHash hash)
        {
            Require(frame.Format.PixelFormat == VideoPixelFormat.Yuv420P10 && frame.Planes.Count == 3);
            Require(frame.Format.Width == BinaryPrimitives.ReadUInt16LittleEndian(reference.AsSpan(12)) &&
                frame.Format.Height == BinaryPrimitives.ReadUInt16LittleEndian(reference.AsSpan(14)));
            for (int plane = 0; plane < frame.Planes.Count; plane++)
            {
                for (int row = 0; row < frame.Planes[plane].Height; row++) { hash.AppendData(frame.GetRow(plane, row)); }
            }
            Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
            Require(hash.TryGetHashAndReset(digest, out int written) && written == digest.Length &&
                digest.SequenceEqual(reference.AsSpan(16 + index * digest.Length, digest.Length)));
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("Ten-bit AV1 container decoding, seeking, timing or ownership differs from its independent reference."); }
        }
    }
}
