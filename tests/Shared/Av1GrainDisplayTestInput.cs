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

namespace Mediana.Tests
{
    internal static class Av1GrainDisplayTestInput
    {
        internal static void VerifyPictures(string root)
        {
            for (int index = 1; index <= 16; index++)
            {
                Av1PictureSequenceTestInput.Verify(root, $"grain{index:00}", [0x81, 0, 12, 0]);
            }
            Av1PictureSequenceTestInput.Verify(root, "grainMono", [0x81, 0, 28, 0]);
        }

        internal static void VerifyOwnership(string root)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(root, "grain16.obu"));
            List<ReadOnlyMemory<byte>> packets = Av1PictureSequenceTestInput.Packets(encoded);
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            EncodedStreamInfo stream = new(0, MediaKind.Video, "av1", StreamFormats.Av1, new(1, 6), new byte[] { 0x81, 0, 12, 0 });
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
            if (!condition) { throw new InvalidDataException("AV1 grain display changed a held frame, timing or buffer ownership."); }
        }
    }
}
