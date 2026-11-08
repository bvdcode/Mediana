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
using Mediana.Execution;
using Mediana.Processing;
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
                await using AvcHlsTranscodeSession session = AvcHlsTranscodeSession.Create(reader, 0, new H264DecoderFactory(), format,
                    new MediaDuration(1, new(1, 1)), allocator);
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

        public static async Task VerifyEditedAsync(string root, int ticks, int frequency)
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(root, "frame-rate-edited.mp4"));
            await VerifyEditedAsync(bytes, new(ticks, new(1, frequency)), new(0, new(1, 90000)),
                new(68351, new(1, 90000)), 19, new(1515, new(1, 90000)));
        }

        public static async Task VerifyFractionalEditsAsync(string root, bool wide)
        {
            byte[] bytes = Mp4EditTestData.Replace(await File.ReadAllBytesAsync(Path.Combine(root, "avc-tail.mov")), wide,
                (1, -1, 65536), (1001, 7, 65536));
            await VerifyEditedAsync(bytes, new(1, new(1, 2)), new(1, new(1, 1000)),
                new(1002, new(1, 1000)), 6, new(51025, new(1, 256000)));
        }

        private static async Task VerifyEditedAsync(byte[] bytes, MediaDuration target, MediaTimestamp expectedStart,
            MediaTimestamp expectedEnd, int expectedPictures, MediaDuration expectedOpeningDuration)
        {
            MediaBufferAllocator allocator = new(8 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(bytes);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator,
                new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                VideoFormat format;
                using (MediaDecodePipeline<VideoFrame> inspect = new(reader, new H264DecoderFactory().Create(reader.Streams[0], allocator), 0))
                {
                    using VideoFrame first = (await inspect.ReadAsync())!;
                    format = first.Format;
                }
                long retained = allocator.AllocatedBytes;
                source.Reads.Clear();
                await using AvcHlsTranscodeSession session = AvcHlsTranscodeSession.Create(reader, 0,
                    new H264DecoderFactory(), format, target, allocator,
                    preparation: new VideoPreparationOptions { MaximumWidth = 64 });
                RequireEdited(source.Reads.Count == 0, "Edited HLS planning read encoded media.");
                RequireEdited(session.Windows[0].Start == expectedStart && session.Windows[^1].End == expectedEnd,
                    "Edited HLS planning changed the actual displayed extent.");
                for (int index = 1; index < session.Windows.Count; index++)
                { RequireEdited(session.Windows[index - 1].End == session.Windows[index].Start, "Edited HLS windows are not contiguous."); }
                using MemoryStream initialization = new();
                await session.WriteInitializationAsync(initialization);
                RequireEdited(source.Reads.Count == 0 && retained == allocator.AllocatedBytes,
                    "Edited HLS initialization read payload or retained temporary storage.");
                int pictures = 0;
                byte[]? tail = null;
                for (int number = session.Windows.Count - 1; number >= 0; number--)
                {
                    using MemoryStream fragment = new();
                    await session.WriteSegmentAsync(number, fragment);
                    byte[] output = fragment.ToArray();
                    ReadOnlyMemory<byte> traf = Box(Box(output, "moof"), "traf"), run = Box(traf, "trun");
                    int count = checked((int)BinaryPrimitives.ReadUInt32BigEndian(run.Span[4..]));
                    RequireEdited(count > 0, "An edited HLS window contained no pictures.");
                    pictures += count;
                    HlsVideoWindow window = session.Windows[number];
                    long start = checked((long)BinaryPrimitives.ReadUInt64BigEndian(Box(traf, "tfdt").Span[4..]));
                    RequireEdited(window.Start == new MediaTimestamp(start, session.Stream.TimeBase), "An edited HLS fragment changed its start.");
                    long duration = 0;
                    for (int sample = 0; sample < count; sample++)
                    { duration += BinaryPrimitives.ReadUInt32BigEndian(run.Span[(12 + sample * 16)..]); }
                    RequireEdited(window.End == new MediaTimestamp(checked(start + duration), session.Stream.TimeBase),
                        "An edited HLS fragment changed its exact end.");
                    if (number == 0)
                    {
                        RequireEdited(new MediaTimestamp(expectedOpeningDuration.Value, expectedOpeningDuration.TimeBase) ==
                            new MediaTimestamp(BinaryPrimitives.ReadUInt32BigEndian(run.Span[12..]), session.Stream.TimeBase),
                            "The partial opening picture was dropped or its display duration changed.");
                    }
                    if (number == session.Windows.Count - 1) { tail = output; }
                    RequireEdited(retained == allocator.AllocatedBytes, "Edited HLS encoding retained temporary storage.");
                }
                RequireEdited(expectedPictures == pictures, "Edited HLS changed the displayed picture count.");
                using MemoryStream repeated = new();
                await session.WriteSegmentAsync(session.Windows.Count - 1, repeated);
                RequireEdited(tail is not null && tail.AsSpan().SequenceEqual(repeated.ToArray()) && retained == allocator.AllocatedBytes,
                    "A repeated edited HLS window changed its bytes or retained temporary storage.");
            }
            RequireEdited(allocator.AllocatedBytes == 0, "Edited HLS retained source or codec storage after disposal.");
        }

        private static void RequireEdited(bool condition, string message)
        {
            if (!condition) { throw new InvalidDataException(message); }
        }

        private static void VerifyFragment(byte[] fragment, EncodedStreamInfo stream, byte[] reference,
            int number, MediaBufferAllocator allocator)
        {
            ReadOnlyMemory<byte> moof = Box(fragment, "moof"), traf = Box(moof, "traf");
            ReadOnlyMemory<byte> run = Box(traf, "trun"), data = Box(fragment, "mdat");
            long dts = checked((long)BinaryPrimitives.ReadUInt64BigEndian(Box(traf, "tfdt").Span[4..]));
            if (run.Span[0] != 0 || BinaryPrimitives.ReadUInt32BigEndian(run.Span[4..]) != 5 ||
                new MediaTimestamp(dts, stream.TimeBase) != new MediaTimestamp(number, new(1, 1)))
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
                if (new MediaTimestamp(duration, stream.TimeBase) != new MediaTimestamp(1, new(1, 5)) ||
                    composition != 0 || flags != (sample == 0 ? 0x02000000U : 0x01010000U))
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

        internal static ReadOnlyMemory<byte> Box(ReadOnlyMemory<byte> bytes, ReadOnlySpan<char> type)
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
