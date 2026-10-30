// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    internal static class HlsEncodingTestInput
    {
        public static async Task VerifyAsync(string root, string name, int width, int height, int pictures, int segmentCount)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".mp4")));
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            using VideoFrame first = await session.ReadFrameAsync() ?? throw new InvalidDataException("The HLS source has no video frames.");
            string suffix = width == first.Format.Width ? ".encoded-qp26.yuv" : $".scaled-{width}x{height}.encoded-qp26.yuv";
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + suffix));
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            using Yuv420AreaResizer resizer = Yuv420AreaResizer.CreateFit(first.Format, width, first.Format.Height, allocator);
            using H264Encoder encoder = H264Encoder.Create(resizer.OutputFormat, session.Stream.TimeBase, allocator);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(encoder.Stream, allocator);
            List<MemoryStream> destinations = [];
            await using AvcHlsWriter writer = AvcHlsWriter.Create(encoder.Stream, (number, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (number != destinations.Count)
                {
                    throw new InvalidOperationException("HLS segment numbers are not contiguous.");
                }
                MemoryStream destination = new();
                destinations.Add(destination);
                return ValueTask.FromResult<Stream>(destination);
            }, allocator, new() { TargetDuration = new(1, new(1, 2)) });
            List<(int Stream, byte[] Data, long Pts, long Dts)> packets = [];
            int offset = 0;
            int count = 0;
            VideoFrame? input = first.Retain();
            while (input is not null)
            {
                using (input)
                using (VideoFrame scaled = resizer.Apply(input))
                {
                    if (encoder.TrySend(scaled) != CodecInputStatus.Accepted)
                    {
                        throw new InvalidOperationException("HLS encoding did not accept its next frame.");
                    }
                    using MediaPacket packet = encoder.Receive().Value ?? throw new InvalidDataException("HLS encoding produced no packet.");
                    offset = H264EncodingTestInput.VerifyPacket(scaled, encoder.Stream, packet, decoder, expected, offset);
                    long pts = packet.PresentationTime!.Value.Rescale(new(1, 90000), TimestampRounding.NearestEven).Value + 90000;
                    packets.Add((packet.StreamIndex, packet.Data.ToArray(), pts, pts));
                    await writer.WritePacketAsync(packet);
                    count++;
                }
                input = await session.ReadFrameAsync();
            }
            encoder.CompleteInput();
            await writer.CompleteAsync();
            if (count != pictures || offset != expected.Length || writer.Segments.Count != segmentCount || destinations.Any(destination => destination.CanWrite) ||
                encoder.Receive().Status != CodecOutputStatus.EndOfStream)
            {
                throw new InvalidDataException("HLS encoding lost pictures, retained an owned stream or changed the segment count.");
            }
            byte[] transport = destinations.SelectMany(destination => destination.ToArray()).ToArray();
            MpegTsTestInput.Verify(transport, [encoder.Stream], packets);
            if (writer.BytesWritten != transport.LongLength || writer.Segments.Sum(segment => segment.ByteLength) != transport.LongLength ||
                !writer.CreateVodPlaylist(number => $"segment-{number}.ts").Contains("#EXT-X-INDEPENDENT-SEGMENTS\n", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The HLS transport sizes or restart declaration differ.");
            }
            HlsVariant variant = writer.CreateVariant("video.m3u8");
            if (!HlsMasterPlaylist.Create([variant]).Contains("CODECS=\"avc1.42C029\"", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The HLS master playlist changed the encoded profile, constraints or level.");
            }
            await writer.DisposeAsync();
            encoder.Dispose();
            decoder.Dispose();
            resizer.Dispose();
            first.Dispose();
            await session.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("The completed HLS encoding pipeline retained media storage.");
            }
        }
    }
}
