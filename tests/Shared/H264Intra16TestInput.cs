// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    internal static class H264Intra16TestInput
    {
        public static async Task<(byte[] Transport, byte[] Pixels)> EncodeAsync(int qp)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            VideoFormat format = new(32, 32, VideoPixelFormat.Yuv420P8,
                new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Full, ChromaLocation.Left), new(1, 1));
            H264EncoderOptions options = new() { Quantizer = qp };
            byte[] configuration = H264EncoderConfiguration.Create(format, allocator, options, out _);
            H264Configuration parsed = H264Configuration.Parse(configuration, new());
            EncodedStreamInfo stream = new(0, MediaKind.Video, "h264", StreamFormats.Avc, new(1, 24), configuration);
            using MemoryStream transport = new();
            using MemoryStream pixels = new();
            using (IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(stream, allocator))
            {
                await using MpegTsWriter writer = MpegTsWriter.Create(transport, [stream], allocator);
                for (int frame = 0; frame < 2; frame++)
                {
                    H264SliceKind kind = frame == 0 ? H264SliceKind.I : H264SliceKind.P;
                    using H264Picture reconstructed = new(parsed.Sequences[0], allocator, kind);
                    using WritableMediaBuffer source = allocator.Allocate(1536);
                    for (int plane = 0, offset = 0; plane < 3; plane++)
                    {
                        int size = plane == 0 ? 32 : 16;
                        for (int y = 0; y < size; y++)
                        {
                            for (int x = 0; x < size; x++)
                            {
                                source.Span[offset++] = (byte)(64 + (x * 2 + y * 3 + plane * 35 + frame * 7) % 128);
                            }
                        }
                    }
                    using MediaBuffer inputBuffer = source.Publish();
                    using VideoFrame input = new(format, inputBuffer, format.CreateTightLayout(), new(frame, stream.TimeBase), new(1, stream.TimeBase));
                    using H264BitWriter bits = new(allocator, 65536, frame == 0 ? (byte)0x65 : (byte)0x41, lengthPrefixed: true);
                    bits.WriteUnsigned(0); bits.WriteUnsigned(frame == 0 ? 7u : 5u); bits.WriteUnsigned(0); bits.WriteBits((uint)frame, 4);
                    if (frame == 0)
                    {
                        bits.WriteUnsigned(0); bits.WriteFlag(false); bits.WriteFlag(false);
                    }
                    else
                    {
                        bits.WriteFlag(false); bits.WriteFlag(false); bits.WriteFlag(false);
                    }
                    bits.WriteSigned(0); bits.WriteUnsigned(1);
                    byte[] samples = new byte[384];
                    foreach ((int x, int y, int mode) in new[] { (0, 0, 2), (16, 0, 1), (0, 16, 0), (16, 16, 3) })
                    {
                        H264SourceMacroblock macroblock = new(input, x, y, samples);
                        if (frame != 0) { bits.WriteUnsigned(0); }
                        if (!H264Intra16Encoder.TryWrite(bits, macroblock, reconstructed, x, y, qp, qp, mode, kind))
                        {
                            throw new InvalidDataException("The intra16 reference did not fit Baseline coefficient syntax.");
                        }
                    }
                    using MediaBuffer buffer = bits.Finish();
                    using MediaPacket packet = new(0, buffer, input.Timestamp, input.Timestamp, input.Duration, isKeyFrame: frame == 0);
                    await writer.WritePacketAsync(packet);
                    if (decoder.TrySend(packet) != CodecInputStatus.Accepted)
                    {
                        throw new InvalidDataException("The intra16 packet was not accepted by the decoder.");
                    }
                    using VideoFrame decoded = decoder.Receive().Value;
                    int position = 0;
                    for (int plane = 0; plane < 3; plane++)
                    {
                        ReadOnlySpan<byte> expected = reconstructed.ReadPlane(plane);
                        if (!expected.SequenceEqual(decoded.Data.Span.Slice(position, expected.Length)))
                        {
                            throw new InvalidDataException("Intra16 syntax and encoder reconstruction differ.");
                        }
                        pixels.Write(expected);
                        position += expected.Length;
                    }
                }
                decoder.CompleteInput();
                if (decoder.Receive().Status != CodecOutputStatus.EndOfStream)
                {
                    throw new InvalidDataException("The intra16 decoder did not reach EOF.");
                }
                await writer.CompleteAsync();
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Intra16 encoding retained media storage.");
            }
            return (transport.ToArray(), pixels.ToArray());
        }
    }
}
