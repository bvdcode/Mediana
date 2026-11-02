// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    internal static class H264BaselineEncodingTestInput
    {
        public static async Task VerifyAsync(string root, bool reverse)
        {
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, $"baseline-chroma-{reverse}.yuv"));
            (byte[] _, byte[] decoded) = await EncodeAsync(reverse);
            if (!decoded.AsSpan().SequenceEqual(expected))
            {
                throw new InvalidDataException("Baseline chroma transitions differ from independent decoded samples.");
            }
        }

        public static async Task<(byte[] Transport, byte[] Decoded)> EncodeAsync(bool reverse)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            VideoFormat format = new(16, 16, VideoPixelFormat.Yuv420P8,
                new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Full, ChromaLocation.Left), new(1, 1));
            using MemoryStream transport = new();
            using MemoryStream decoded = new();
            using (H264Encoder encoder = H264Encoder.Create(format, new(1, 24), allocator,
                new() { Quantizer = 0, MotionSearchRange = 0 }))
            using (IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(encoder.Stream, allocator))
            {
                await using MpegTsWriter writer = MpegTsWriter.Create(transport, [encoder.Stream], allocator);
                H264Configuration configuration = H264Configuration.Parse(encoder.Stream.Configuration.Span, new());
                for (int index = 0; index < 5; index++)
                {
                    using WritableMediaBuffer storage = allocator.Allocate(384);
                    storage.Memory.Span[..256].Fill(128);
                    storage.Memory.Span[256..].Fill((index == 0 || index >= 3) != reverse ? (byte)0 : (byte)255);
                    using MediaBuffer buffer = storage.Publish();
                    using VideoFrame input = new(format, buffer, format.CreateTightLayout(), new(index, new(1, 24)), new(1, new(1, 24)));
                    if (encoder.TrySend(input) != CodecInputStatus.Accepted)
                    {
                        throw new InvalidOperationException("The Baseline encoder rejected its input.");
                    }
                    using MediaPacket packet = encoder.Receive().Value ?? throw new InvalidDataException("Missing Baseline packet.");
                    if (index == 1 || index == 3)
                    {
                        VerifyTransition(packet, configuration);
                    }
                    await writer.WritePacketAsync(packet);
                    if (decoder.TrySend(packet) != CodecInputStatus.Accepted)
                    {
                        throw new InvalidOperationException("The Baseline decoder rejected the encoded packet.");
                    }
                    using VideoFrame output = decoder.Receive().Value ?? throw new InvalidDataException("Missing Baseline picture.");
                    if (!output.Data.Span.SequenceEqual(input.Data.Span) || output.Timestamp != input.Timestamp || output.Duration != input.Duration)
                    {
                        throw new InvalidDataException("A flat chroma transition changed expected pixels or timing.");
                    }
                    decoded.Write(output.Data.Span);
                }
                encoder.CompleteInput();
                decoder.CompleteInput();
                await writer.CompleteAsync();
                if (encoder.Receive().Status != CodecOutputStatus.EndOfStream || decoder.Receive().Status != CodecOutputStatus.EndOfStream)
                {
                    throw new InvalidOperationException("The Baseline sequence did not complete.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Baseline encoding retained media storage.");
            }
            return (transport.ToArray(), decoded.ToArray());
        }
        private static void VerifyTransition(MediaPacket packet, H264Configuration configuration)
        {
            H264BitReader bits = H264Nal.Payload(packet.Data.Span[4..], 1);
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, packet.Data.Span[4]);
            if (header.Qp != 0 || bits.ReadUe() != 0 || bits.ReadUe() != 0 || bits.ReadSe() != 0 || bits.ReadSe() != 0 ||
                H264InterMacroblock.CodedPatterns[bits.ReadUe()] != 16 || bits.ReadSe() <= 0)
            {
                throw new InvalidDataException("The chroma transition did not signal bounded macroblock requantization.");
            }
            Span<int> levels = stackalloc int[4];
            H264Cavlc.Read(ref bits, levels, -1, maximumPrefix: 15);
            H264Cavlc.Read(ref bits, levels, -1, maximumPrefix: 15);
            bits.ReadTrailingBits();
        }
    }
}
