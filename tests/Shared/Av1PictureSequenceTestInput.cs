// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using Mediana.Codecs;
using Mediana.Codecs.Av1;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Video;
using Av1ObuReader = Mediana.Formats.Video.Av1ObuReader;

namespace Mediana.Tests
{
    internal static class Av1PictureSequenceTestInput
    {
        internal static void Verify(string root, string fixture, byte[] configuration)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(root, fixture + ".obu"));
            using FileStream reference = File.OpenRead(Path.Combine(root, fixture + ".sha256"));
            using BinaryReader reader = new(reference);
            Require(reader.ReadBytes(8).AsSpan().SequenceEqual("AV1HASH1"u8));
            int count = reader.ReadInt32();
            int width = reader.ReadUInt16();
            int height = reader.ReadUInt16();
            byte[] expected = reader.ReadBytes(checked(count * SHA256.HashSizeInBytes));
            Require(count > 0 && width > 0 && height > 0);
            Require(expected.Length == count * SHA256.HashSizeInBytes && reference.Position == reference.Length);
            List<ReadOnlyMemory<byte>> packets = Packets(encoded);
            Require(packets.Count == count);
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            Av1CodecConfigurationRecord config = Av1CodecConfigurationRecord.Read(configuration);
            EncodedStreamInfo stream = new(3, MediaKind.Video, "av1", StreamFormats.Av1, new(1, 24), configuration);
            Av1DecoderFactory factory = new();
            IRandomAccessInspector inspector = factory.CreateRandomAccessInspector(stream);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (IMediaCodec<MediaPacket, VideoFrame> decoder = factory.Create(stream, allocator))
            {
                Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
                for (int repetition = 0; repetition < 2; repetition++)
                {
                    for (int index = 0; index < count; index++)
                    {
                        using WritableMediaBuffer write = allocator.Allocate(packets[index].Length);
                        packets[index].Span.CopyTo(write.Span);
                        using MediaBuffer buffer = write.Publish();
                        MediaTimestamp timestamp = new(index - 7, stream.TimeBase);
                        MediaDuration duration = new(1, stream.TimeBase);
                        using MediaPacket packet = new(stream.StreamIndex, buffer, timestamp, duration: duration);
                        Require(inspector.IsRandomAccess(packet) == (index == 0));
                        Require(decoder.TrySend(packet) == CodecInputStatus.Accepted);
                        CodecOutput<VideoFrame> output = decoder.Receive();
                        Require(output.Status == CodecOutputStatus.Available);
                        using VideoFrame frame = output.Value;
                        Require(frame.Timestamp == timestamp && frame.Duration == duration);
                        Require(frame.Format.Width == width && frame.Format.Height == height);
                        Require(frame.Planes.Count == (config.Monochrome ? 1 : 3));
                        if (!config.Monochrome) { Require(frame.Format.PixelFormat == VideoPixelFormat.Yuv420P8); }
                        for (int plane = 0; plane < frame.Planes.Count; plane++)
                        {
                            for (int row = 0; row < frame.Planes[plane].Height; row++)
                            {
                                hash.AppendData(frame.GetRow(plane, row));
                            }
                        }
                        Require(hash.TryGetHashAndReset(digest, out int written) && written == digest.Length);
                        if (!digest.SequenceEqual(expected.AsSpan(index * digest.Length, digest.Length)))
                        {
                            throw new InvalidDataException($"AV1 {fixture} reconstruction differs from the independent reference at frame {index}.");
                        }
                        Require(decoder.Receive().Status == CodecOutputStatus.InputRequired);
                    }
                    decoder.CompleteInput();
                    Require(decoder.Receive().Status == CodecOutputStatus.EndOfStream);
                    decoder.Reset();
                    Require(allocator.AllocatedBytes == 0);
                }
            }
            Require(allocator.AllocatedBytes == 0);
        }

        internal static List<ReadOnlyMemory<byte>> Packets(byte[] encoded)
        {
            List<ReadOnlyMemory<byte>> packets = [];
            Av1ObuReader reader = new(encoded);
            int start = 0;
            int previous = 0;
            while (reader.TryRead(out int type, out _))
            {
                if (type == 2 && previous > start)
                {
                    packets.Add(encoded.AsMemory(start, previous - start));
                    start = previous;
                }
                previous = reader.ByteOffset;
            }
            if (start < encoded.Length) { packets.Add(encoded.AsMemory(start)); }
            return packets;
        }

        internal static void VerifyOwnership(string root, string fixture, byte[] configuration)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(root, fixture + ".obu"));
            List<ReadOnlyMemory<byte>> packets = Av1PictureSequenceTestInput.Packets(encoded);
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            EncodedStreamInfo stream = new(0, MediaKind.Video, "av1", StreamFormats.Av1, new(1, 6), configuration);
            using (IMediaCodec<MediaPacket, VideoFrame> decoder = new Av1DecoderFactory().Create(stream, allocator))
            {
                Send(decoder, allocator, packets[0], 0);
                using VideoFrame held = decoder.Receive().Value;
                byte[] digest = SHA256.HashData(held.Data.Span);
                for (int index = 1; index < packets.Count; index++)
                {
                    Send(decoder, allocator, packets[index], index);
                    using VideoFrame current = decoder.Receive().Value;
                    Require(SHA256.HashData(held.Data.Span).AsSpan().SequenceEqual(digest));
                }
                decoder.CompleteInput();
                Require(decoder.Receive().Status == CodecOutputStatus.EndOfStream);
                decoder.Reset();
                Require(allocator.AllocatedBytes == held.Data.Length);
                using CancellationTokenSource cancellation = new();
                cancellation.Cancel();
                bool cancelled = false;
                try { Send(decoder, allocator, packets[0], 0, cancellation.Token); }
                catch (OperationCanceledException exception) when (exception.CancellationToken == cancellation.Token) { cancelled = true; }
                Require(cancelled && allocator.AllocatedBytes == held.Data.Length);
                decoder.Reset();
                Send(decoder, allocator, packets[0], 0);
                using VideoFrame restarted = decoder.Receive().Value;
                Require(SHA256.HashData(restarted.Data.Span).AsSpan().SequenceEqual(digest));
                Require(SHA256.HashData(held.Data.Span).AsSpan().SequenceEqual(digest));
            }
            Require(allocator.AllocatedBytes == 0);
        }

        private static void Send(IMediaCodec<MediaPacket, VideoFrame> decoder, MediaBufferAllocator allocator,
            ReadOnlyMemory<byte> encoded, int index, CancellationToken token = default)
        {
            using WritableMediaBuffer write = allocator.Allocate(encoded.Length);
            encoded.Span.CopyTo(write.Span);
            using MediaBuffer buffer = write.Publish();
            using MediaPacket packet = new(0, buffer, new MediaTimestamp(index, new(1, 6)), duration: new MediaDuration(1, new(1, 6)));
            Require(decoder.TrySend(packet, token) == CodecInputStatus.Accepted);
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("AV1 picture output, timing or buffer ownership is invalid."); }
        }
    }
}
