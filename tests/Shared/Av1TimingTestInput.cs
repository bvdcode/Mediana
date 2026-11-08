// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Av1;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Tests
{
    internal static class Av1TimingTestInput
    {
        internal static void Verify(string root, string fixture)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(root, fixture + ".obu"));
            byte[] expected = File.ReadAllBytes(Path.Combine(root, "timing.reference.yuv"));
            List<ReadOnlyMemory<byte>> packets = Av1PictureSequenceTestInput.Packets(encoded);
            MediaBufferAllocator allocator = new(1024 * 1024);
            EncodedStreamInfo stream = new(2, MediaKind.Video, "av1", StreamFormats.Av1, new(1, 97), [0x81, 0, 12, 0]);
            Av1DecoderFactory factory = new();
            using (IMediaCodec<MediaPacket, VideoFrame> decoder = factory.Create(stream, allocator))
            {
                for (int repetition = 0; repetition < 2; repetition++)
                {
                    int offset = 0;
                    for (int index = 0; index < packets.Count; index++)
                    {
                        using WritableMediaBuffer write = allocator.Allocate(packets[index].Length);
                        packets[index].Span.CopyTo(write.Span);
                        using MediaBuffer buffer = write.Publish();
                        MediaTimestamp timestamp = new(-400 + index * 11, stream.TimeBase);
                        MediaDuration duration = new(11, stream.TimeBase);
                        using MediaPacket packet = new(stream.StreamIndex, buffer, timestamp, duration: duration);
                        Require(factory.CreateRandomAccessInspector(stream).IsRandomAccess(packet) == (index == 0));
                        Require(decoder.TrySend(packet) == CodecInputStatus.Accepted);
                        CodecOutput<VideoFrame> output = decoder.Receive();
                        Require(output.Status == CodecOutputStatus.Available);
                        using VideoFrame frame = output.Value;
                        Require(frame.Timestamp == timestamp && frame.Duration == duration);
                        Require(frame.Format.Width == 48 && frame.Format.Height == 40);
                        Require(frame.Format.PixelFormat == VideoPixelFormat.Yuv420P8);
                        for (int plane = 0; plane < frame.Planes.Count; plane++)
                        {
                            for (int row = 0; row < frame.Planes[plane].Height; row++)
                            {
                                ReadOnlySpan<byte> actual = frame.GetRow(plane, row);
                                Require(expected.AsSpan(offset, actual.Length).SequenceEqual(actual));
                                offset += actual.Length;
                            }
                        }
                        Require(decoder.Receive().Status == CodecOutputStatus.InputRequired);
                    }
                    Require(offset == expected.Length);
                    decoder.CompleteInput();
                    Require(decoder.Receive().Status == CodecOutputStatus.EndOfStream);
                    decoder.Reset();
                    Require(allocator.AllocatedBytes == 0);
                }
            }
            Require(allocator.AllocatedBytes == 0);
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("AV1 timing reconstruction or packet ownership differs from its independent reference."); }
        }
    }
}
