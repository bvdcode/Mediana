// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats.Video;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    internal static class H264TemporalEncodingTestInput
    {
        public const int FrameCount = 80;

        public static async Task VerifyAsync(string root, int mode, int qp)
        {
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, $"temporal-{mode}-qp{qp}.yuv"));
            (byte[] transport, byte[] _, byte[] actual) = await EncodeAsync(mode, qp);
            if (!actual.AsSpan().SequenceEqual(expected))
            {
                throw new InvalidDataException("The complete temporal encoding differs from independent decoded pixels.");
            }
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "TEMPORAL-ENCODING.json")));
            JsonElement reference = manifest.RootElement.GetProperty("cases").EnumerateArray().Single(item =>
                item.GetProperty("mode").GetInt32() == mode && item.GetProperty("quantizer").GetInt32() == qp);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(transport)),
                reference.GetProperty("transport_sha256").GetString(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The complete temporal transport differs from its reference hash.");
            }
        }

        public static async Task<(byte[] Transport, byte[] Source, byte[] Decoded)> EncodeAsync(int mode, int qp)
        {
            (int width, int height) = mode switch
            {
                0 => (32, 32),
                1 => (46, 34),
                2 => (32, 48),
                3 => (50, 34),
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
            VideoFormat format = new(width, height, VideoPixelFormat.Yuv420P8,
                new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Left),
                new SampleAspectRatio(4, 3));
            MediaBufferAllocator allocator = new(8 * 1024 * 1024);
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator, new() { Quantizer = qp });
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(encoder.Stream, allocator);
            using MemoryStream transport = new();
            using MemoryStream source = new();
            using MemoryStream decoded = new();
            await using MpegTsWriter writer = MpegTsWriter.Create(transport, [encoder.Stream], allocator);
            List<MemoryStream> segments = [];
            await using AvcHlsWriter hls = AvcHlsWriter.Create(encoder.Stream, (_, _) =>
            {
                MemoryStream segment = new();
                segments.Add(segment);
                return ValueTask.FromResult<Stream>(segment);
            }, allocator, new() { TargetDuration = new(2, new(1, 1)) });
            MediaTimestamp[] timestamps = new MediaTimestamp[FrameCount];
            MediaDuration[] durations = new MediaDuration[FrameCount];
            int pictures = 0;
            long clock = -120;
            long keyClock = clock;
            int frameNumber = 0;
            H264Configuration configuration = H264Configuration.Parse(encoder.Stream.Configuration.Span, new());
            for (int index = 0; index < FrameCount; index++)
            {
                int duration = mode == 3 ? (index % 3) switch { 0 => 20, 1 => 80, _ => 40 } : 40;
                timestamps[index] = new(clock, new(1, 1000));
                durations[index] = new(duration, new(1, 1000));
                using VideoFrame input = CreateFrame(format, allocator, mode, index, timestamps[index], durations[index]);
                Append(input, source);
                if (encoder.TrySend(input) != CodecInputStatus.Accepted)
                {
                    throw new InvalidOperationException("The temporal encoder rejected an input without pending output.");
                }
                using MediaPacket packet = encoder.Receive().Value ?? throw new InvalidDataException("Missing encoded temporal packet.");
                bool key = index == 0 || clock - keyClock >= 2000;
                frameNumber = key ? 0 : (frameNumber + 1) & 15;
                if (key)
                {
                    keyClock = clock;
                }
                H264BitReader bits = H264Nal.Payload(packet.Data.Span[4..], key ? 5 : 1);
                H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, packet.Data.Span[4]);
                if (packet.IsKeyFrame != key || header.IsIdr != key || header.FrameNumber != frameNumber ||
                    header.Kind != (key ? H264SliceKind.I : H264SliceKind.P) ||
                    packet.PresentationTime != input.Timestamp || packet.DecodeTime != input.Timestamp || packet.Duration != input.Duration)
                {
                    throw new InvalidDataException("The temporal encoder changed keyframe decisions, reference numbering or input timing.");
                }
                if (mode == 0 && !key && packet.Data.Length > 64)
                {
                    throw new InvalidDataException("A constant picture did not use compact inter prediction.");
                }
                await writer.WritePacketAsync(packet);
                await hls.WritePacketAsync(packet);
                if (decoder.TrySend(packet) != CodecInputStatus.Accepted)
                {
                    throw new InvalidOperationException("The temporal decoder did not accept a drained packet.");
                }
                Drain();
                clock += duration;
            }
            encoder.CompleteInput();
            decoder.CompleteInput();
            Drain();
            await hls.CompleteAsync();
            if (segments.Count != 2 || hls.Segments[0].Start != timestamps[0] ||
                hls.BytesWritten != segments.Sum(segment => segment.ToArray().LongLength) || segments.Any(segment => segment.CanWrite) ||
                !hls.CreateVodPlaylist(number => $"{number}.ts").Contains("#EXT-X-INDEPENDENT-SEGMENTS\n", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Temporal HLS changed IDR boundaries, stream ownership or independent restart declarations.");
            }
            if (pictures != FrameCount || encoder.Receive().Status != CodecOutputStatus.EndOfStream ||
                decoder.Receive().Status != CodecOutputStatus.EndOfStream)
            {
                throw new InvalidDataException("Temporal encoding did not preserve the complete sequence.");
            }
            await writer.DisposeAsync();
            await hls.DisposeAsync();
            encoder.Dispose();
            decoder.Dispose();
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Temporal encoding retained media buffers after disposal.");
            }
            return (transport.ToArray(), source.ToArray(), decoded.ToArray());

            void Drain()
            {
                while (true)
                {
                    CodecOutput<VideoFrame> output = decoder.Receive();
                    if (output.Status != CodecOutputStatus.Available)
                    {
                        break;
                    }
                    VideoFrame frame = output.Value;
                    using (frame)
                    {
                        if (pictures >= FrameCount || frame.Timestamp != timestamps[pictures] || frame.Duration != durations[pictures] ||
                            frame.Format.Width != width || frame.Format.Height != height || frame.Format.Color != format.Color ||
                            frame.Format.SampleAspectRatio != format.SampleAspectRatio)
                        {
                            throw new InvalidDataException("Temporal reconstruction changed picture timing or format.");
                        }
                        Append(frame, decoded);
                        pictures++;
                    }
                }
            }
        }

        private static void Append(VideoFrame frame, Stream output)
        {
            for (int plane = 0; plane < 3; plane++)
            {
                for (int row = 0; row < frame.Planes[plane].Height; row++)
                {
                    output.Write(frame.GetRow(plane, row));
                }
            }
        }

        private static VideoFrame CreateFrame(VideoFormat format, MediaBufferAllocator allocator, int mode, int index,
            MediaTimestamp timestamp, MediaDuration duration)
        {
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer storage = allocator.Allocate(layout.Sum(plane => plane.Stride * plane.Height));
            for (int plane = 0; plane < 3; plane++)
            {
                for (int y = 0; y < layout[plane].Height; y++)
                {
                    for (int x = 0; x < layout[plane].RowBytes; x++)
                    {
                        int sx = x + index;
                        int sy = y;
                        int value = mode switch
                        {
                            0 => 32 + plane * 64,
                            1 => 24 + ((sx * 5 + sy * 3 + plane * 35) % 192),
                            2 when plane == 0 => 96,
                            2 => 40 + ((sx * 9 + sy * 5 + plane * 31) % 176),
                            3 => 24 + ((x * 7 + (y + index) * 11 + plane * 43) % 192),
                            _ => throw new ArgumentOutOfRangeException(nameof(mode))
                        };
                        if (mode == 3 && index >= 37)
                        {
                            value = 255 - value;
                        }
                        storage.Memory.Span[layout[plane].Offset + y * layout[plane].Stride + x] = (byte)value;
                    }
                }
            }
            using MediaBuffer buffer = storage.Publish();
            return new(format, buffer, layout, timestamp, duration);
        }
    }
}
