// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal static class H264LevelBurstTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            string expected = (await File.ReadAllTextAsync(Path.Combine(root, "level-burst.sha256"))).Trim();
            (byte[] _, byte[] decoded) = await CaptureAsync();
            if (!string.Equals(expected, Convert.ToHexString(SHA256.HashData(decoded)), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The complete level-burst picture differs from independent decoded samples.");
            }
        }

        public static async Task<(byte[] Transport, byte[] Decoded)> CaptureAsync()
        {
            MediaBufferAllocator allocator = new(8 * 1024 * 1024);
            TimeBase clock = new(1, 172);
            VideoFormat format = new(640, 352, VideoPixelFormat.Yuv420P8,
                new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Left),
                new SampleAspectRatio(4, 3));
            using MemoryStream transport = new();
            byte[] samples;
            {
                using VideoFrame input = NoiseFrame(format, clock, allocator);
                using H264Encoder encoder = H264Encoder.Create(format, clock, allocator, new() { Quantizer = 26 });
                if (encoder.TrySend(input) != CodecInputStatus.Accepted)
                {
                    throw new InvalidOperationException("The fixed-QP encoder rejected the initial burst.");
                }
                using MediaPacket packet = encoder.Receive().Value ?? throw new InvalidDataException("Missing initial burst packet.");
                if ((Int128)(packet.Data.Length - 4) * 8 * 172 <= 50000000)
                {
                    throw new InvalidDataException("The reference no longer exceeds the instantaneous level bitrate.");
                }
                using IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(encoder.Stream, allocator);
                if (decoder.TrySend(packet) != CodecInputStatus.Accepted)
                {
                    throw new InvalidOperationException("The decoder rejected the initial burst.");
                }
                decoder.CompleteInput();
                using VideoFrame decoded = decoder.Receive().Value ?? throw new InvalidDataException("Missing initial burst picture.");
                if (decoded.Timestamp != input.Timestamp || decoded.Duration != input.Duration ||
                    decoded.Data.Length != input.Data.Length || decoded.Format.Width != format.Width ||
                    decoded.Format.Height != format.Height || decoded.Format.PixelFormat != format.PixelFormat ||
                    decoded.Format.Color != format.Color || decoded.Format.SampleAspectRatio != format.SampleAspectRatio ||
                    decoder.Receive().Status != CodecOutputStatus.EndOfStream)
                {
                    throw new InvalidDataException("The initial burst changed picture timing, format or completed state.");
                }
                samples = decoded.Data.ToArray();
                await using MpegTsWriter writer = MpegTsWriter.Create(transport, [encoder.Stream], allocator);
                await writer.WritePacketAsync(packet);
                await writer.CompleteAsync();
                encoder.Reset();
                if (encoder.TrySend(input) != CodecInputStatus.Accepted)
                {
                    throw new InvalidOperationException("The reset encoder rejected the initial burst.");
                }
                using MediaPacket reset = encoder.Receive().Value ?? throw new InvalidDataException("Missing reset burst packet.");
                if (!reset.Data.Span.SequenceEqual(packet.Data.Span))
                {
                    throw new InvalidDataException("Reset changed the fixed-QP initial burst.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("The initial burst retained media buffers.");
            }
            return (transport.ToArray(), samples);
        }

        private static VideoFrame NoiseFrame(VideoFormat format, TimeBase clock, MediaBufferAllocator allocator)
        {
            PlaneLayout[] planes = format.CreateTightLayout();
            using WritableMediaBuffer storage = allocator.Allocate(planes.Sum(plane => plane.Stride * plane.Height));
            Random random = new(172);
            random.NextBytes(storage.Memory.Span);
            using MediaBuffer buffer = storage.Publish();
            return new(format, buffer, planes, new(0, clock), new(1, clock));
        }
    }
}
