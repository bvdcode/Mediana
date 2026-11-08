// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Av1;
using Mediana.Codecs;
using Mediana.Containers.Matroska;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Video;
using Av1ObuReader = Mediana.Formats.Video.Av1ObuReader;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class Av1ContainerTestInput
    {
        public static async Task VerifyAsync(string root, string extension)
        {
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "interOrdered.yuv"));
            await using FileStream file = File.OpenRead(Path.Combine(root, "interOrdered." + extension));
            await using RandomAccessStreamByteSource source = new(file, leaveOpen: true);
            MediaBufferAllocator allocator = new(1024 * 1024);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddContainer(new MatroskaReaderFactory()).AddVideoDecoder(new Av1DecoderFactory()).Build();
            await using (VideoSession session = await engine.OpenVideoAsync(source, allocator, new(), null))
            {
                Require(session.CanSeek && session.Stream.CodecId == "av1" && session.Stream.Format == StreamFormats.Av1);
                List<MediaTimestamp> times = [];
                int index = 0;
                while (await session.ReadFrameAsync() is VideoFrame frame)
                {
                    using (frame)
                    {
                        VerifyPixels(frame, expected, index++);
                        times.Add(frame.Timestamp ?? throw new InvalidDataException("An AV1 container frame lost its PTS."));
                        Require(frame.Duration is not null);
                    }
                }
                Require(index == 5);
                foreach (int requested in new[] { 3, 1, 4, 0, 3, 4 })
                {
                    await session.SeekAsync(times[requested]);
                    using VideoFrame frame = await session.ReadFrameAtOrAfterAsync(times[requested])
                        ?? throw new InvalidDataException("An indexed AV1 seek lost its selected frame.");
                    Require(frame.Timestamp == times[requested]);
                    VerifyPixels(frame, expected, requested);
                }
                Require(await session.ReadFrameAsync() is null);
            }
            Require(allocator.AllocatedBytes == 0 && file.Position == 0);
        }

        private static void VerifyPixels(VideoFrame frame, byte[] expected, int index)
        {
            Require(frame.Format.Width == 48 && frame.Format.Height == 40 && frame.Planes.Count == 3);
            int offset = index * 48 * 40 * 3 / 2;
            for (int plane = 0; plane < frame.Planes.Count; plane++)
            {
                for (int row = 0; row < frame.Planes[plane].Height; row++)
                {
                    ReadOnlySpan<byte> actual = frame.GetRow(plane, row);
                    Require(actual.SequenceEqual(expected.AsSpan(offset, actual.Length)));
                    offset += actual.Length;
                }
            }
        }

        public static async Task VerifyImplicitFinalSizesAsync(string root)
        {
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "interOrdered.yuv"));
            await using FileStream file = File.OpenRead(Path.Combine(root, "interOrdered.mp4"));
            await using RandomAccessStreamByteSource source = new(file, leaveOpen: true);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                Av1DecoderFactory factory = new();
                using IMediaCodec<MediaPacket, VideoFrame> decoder = factory.Create(reader.Streams[0], allocator);
                IRandomAccessInspector inspector = factory.CreateRandomAccessInspector(reader.Streams[0]);
                int index = 0;
                while (await reader.ReadPacketAsync() is MediaPacket original)
                {
                    using (original)
                    {
                        int last = LastObuOffset(original.Data.Span);
                        int headerBytes = (original.Data.Span[last] & 4) != 0 ? 2 : 1;
                        Av1ObuReader obus = new(original.Data.Span[last..]);
                        Require(obus.TryRead(out int type, out ReadOnlySpan<byte> payload) && type == 6);
                        using WritableMediaBuffer write = allocator.Allocate(last + headerBytes + payload.Length);
                        original.Data.Span[..(last + headerBytes)].CopyTo(write.Span);
                        write.Span[last] &= 0xFD;
                        payload.CopyTo(write.Span[(last + headerBytes)..]);
                        using MediaBuffer buffer = write.Publish();
                        using MediaPacket packet = new(0, buffer, original.PresentationTime, original.DecodeTime, original.Duration);
                        Require(inspector.IsRandomAccess(packet) == (index == 0));
                        Require(decoder.TrySend(packet) == CodecInputStatus.Accepted);
                        using VideoFrame frame = decoder.Receive().Value;
                        VerifyPixels(frame, expected, index++);
                    }
                }
                decoder.CompleteInput();
                Require(index == 5 && decoder.Receive().Status == CodecOutputStatus.EndOfStream);
            }
            Require(allocator.AllocatedBytes == 0);
        }

        private static int LastObuOffset(ReadOnlySpan<byte> encoded)
        {
            Av1ObuReader reader = new(encoded);
            int start = 0;
            int previous = 0;
            while (reader.TryRead(out _, out _))
            {
                start = previous;
                previous = reader.ByteOffset;
            }
            return start;
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("AV1 container decoding, indexed seeking, clocks or ownership verification failed.");
            }
        }
    }
}
