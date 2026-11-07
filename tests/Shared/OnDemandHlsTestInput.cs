// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    internal static class OnDemandHlsTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, "hls-on-demand.encoded-segments.yuv"));
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            using FileStream input = File.OpenRead(Path.Combine(root, "hls-on-demand.mp4"));
            await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                VideoFormat format = new(64, 48, VideoPixelFormat.Yuv420P8,
                    new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Left), new(1, 1));
                HlsVideoWindow[] windows = Enumerable.Range(0, 12)
                    .Select(number => new HlsVideoWindow(number, new(number, new(1, 1)), new(1, new(1, 1)))).ToArray();
                await using AvcHlsTranscodeSession session = AvcHlsTranscodeSession.Create(reader, 0, new H264DecoderFactory(), format, windows, allocator);
                long retained = allocator.AllocatedBytes;
                foreach (int number in Enumerable.Range(0, 12).Reverse().Append(11))
                {
                    using MemoryStream output = new();
                    await session.WriteSegmentAsync(number, output);
                    VerifyFragment(output.ToArray(), session.Stream, reference, number, allocator);
                    if (allocator.AllocatedBytes != retained)
                    {
                        throw new InvalidOperationException("An on-demand encoding request retained codec or fragment storage.");
                    }
                }
            }
            if (allocator.AllocatedBytes != 0 || !input.CanRead)
            {
                throw new InvalidOperationException("On-demand encoding retained storage or closed its borrowed source.");
            }
        }

        private static void VerifyFragment(byte[] fragment, EncodedStreamInfo stream, byte[] reference,
            int number, MediaBufferAllocator allocator)
        {
            ReadOnlyMemory<byte> moof = Box(fragment, "moof"), traf = Box(moof, "traf");
            ReadOnlyMemory<byte> run = Box(traf, "trun"), data = Box(fragment, "mdat");
            long dts = checked((long)BinaryPrimitives.ReadUInt64BigEndian(Box(traf, "tfdt").Span[4..]));
            if (run.Span[0] != 0 || BinaryPrimitives.ReadUInt32BigEndian(run.Span[4..]) != 5 || dts != number * 10240L)
            {
                throw new InvalidDataException("The independent fragment changed its decode start, sample count or composition format.");
            }
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(stream, allocator);
            int offset = 0;
            int frame = 0;
            for (int sample = 0; sample < 5; sample++)
            {
                ReadOnlySpan<byte> entry = run.Span[(12 + 16 * sample)..];
                uint duration = BinaryPrimitives.ReadUInt32BigEndian(entry);
                int bytes = checked((int)BinaryPrimitives.ReadUInt32BigEndian(entry[4..]));
                uint flags = BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
                uint composition = BinaryPrimitives.ReadUInt32BigEndian(entry[12..]);
                if (duration != 2048 || composition != 0 || flags != (sample == 0 ? 0x02000000U : 0x01010000U))
                {
                    throw new InvalidDataException("The independent fragment changed its sample timing or restart declaration.");
                }
                using WritableMediaBuffer storage = allocator.Allocate(bytes);
                data.Slice(offset, bytes).CopyTo(storage.Memory);
                using MediaBuffer payload = storage.Publish();
                using MediaPacket packet = new(0, payload, new(dts, stream.TimeBase), new(dts, stream.TimeBase), new(duration, stream.TimeBase), sample == 0);
                if (decoder.TrySend(packet) != CodecInputStatus.Accepted) { throw new InvalidDataException("The segment decoder did not accept its packet."); }
                Drain(decoder, reference, number, ref frame);
                dts += duration;
                offset += bytes;
            }
            decoder.CompleteInput();
            Drain(decoder, reference, number, ref frame);
            if (frame != 5 || offset != data.Length || decoder.Receive().Status != CodecOutputStatus.EndOfStream)
            {
                throw new InvalidDataException("The independent segment did not decode exactly its five pictures.");
            }
        }

        private static void Drain(IMediaCodec<MediaPacket, VideoFrame> decoder, byte[] reference, int number, ref int frame)
        {
            while (true)
            {
                CodecOutput<VideoFrame> output = decoder.Receive();
                if (output.Status != CodecOutputStatus.Available) { return; }
                using VideoFrame picture = output.Value;
                int expectedFrame = number * 5 + frame;
                int offset = expectedFrame * 64 * 48 * 3 / 2;
                if (frame >= 5 || picture.Timestamp != new MediaTimestamp(expectedFrame * 2048, new(1, 10240)) ||
                    picture.Format.Width != 64 || picture.Format.Height != 48 ||
                    !picture.Data.Span.SequenceEqual(reference.AsSpan(offset, 64 * 48 * 3 / 2)))
                {
                    throw new InvalidDataException("An independently decoded segment differs from its reference pixels or presentation time.");
                }
                frame++;
            }
        }

        private static ReadOnlyMemory<byte> Box(ReadOnlyMemory<byte> bytes, ReadOnlySpan<char> type)
        {
            int at = 0;
            while (at < bytes.Length)
            {
                int size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.Span[at..]));
                if (size < 8 || size > bytes.Length - at) { throw new InvalidDataException("A fragment box exceeds its parent."); }
                bool equal = true;
                for (int index = 0; index < 4; index++) { equal &= bytes.Span[at + 4 + index] == type[index]; }
                if (equal) { return bytes.Slice(at + 8, size - 8); }
                at += size;
            }
            throw new InvalidDataException("The fragment is missing a required box.");
        }
    }
}
