// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
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
        public static async Task VerifyCabacAsync(string root, int qp)
        {
            await using FileStream input = File.OpenRead(Path.Combine(root, $"cabac-intra16-qp{qp}.yuv.gz"));
            await using GZipStream compressed = new(input, CompressionMode.Decompress);
            using MemoryStream expected = new();
            await compressed.CopyToAsync(expected);
            byte[] reference = expected.ToArray();
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "CABAC-INTRA16.json")));
            int offset = 0;
            for (int content = 0; content < 6; content++)
            {
                (byte[] transport, byte[] pixels) = await EncodeAsync(qp, H264EntropyCoding.Cabac, content);
                string name = $"qp{qp}-content{content}";
                JsonElement entry = manifest.RootElement.GetProperty("cases").EnumerateArray()
                    .Single(item => item.GetProperty("case").GetString() == name);
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(transport)),
                    entry.GetProperty("transport_sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("CABAC intra16 syntax differs from its independently decoded reference.");
                }
                if (!pixels.AsSpan().SequenceEqual(reference.AsSpan(offset, pixels.Length)))
                {
                    throw new InvalidDataException("CABAC intra16 reconstruction differs from independent reference pixels.");
                }
                offset += pixels.Length;
            }
            if (offset != reference.Length)
            {
                throw new InvalidDataException("The CABAC intra16 reference contains an unexpected picture count.");
            }
        }

        public static async Task<(byte[] Transport, byte[] Pixels)> EncodeAsync(int qp,
            H264EntropyCoding entropy = H264EntropyCoding.Cavlc, int content = 0)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            VideoFormat format = new(32, 32, VideoPixelFormat.Yuv420P8,
                new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Full, ChromaLocation.Left), new(1, 1));
            H264EncoderOptions options = new() { Quantizer = qp, EntropyCoding = entropy };
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
                                source.Span[offset++] = Sample(content, plane, x, y, frame);
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
                    if (entropy == H264EntropyCoding.Cabac && frame != 0) { bits.WriteUnsigned(0); }
                    bits.WriteSigned(0); bits.WriteUnsigned(1);
                    using H264CabacMacroblockState? state = entropy == H264EntropyCoding.Cabac ? new(parsed.Sequences[0], allocator) : null;
                    H264CabacEncoder? arithmetic = null;
                    H264CabacEncodingSyntax? syntax = null;
                    if (state is not null)
                    {
                        while ((bits.BitPosition & 7) != 0) { bits.WriteFlag(true); }
                        arithmetic = new(bits);
                        syntax = new(arithmetic, state, reconstructed, kind, qp);
                    }
                    byte[] samples = new byte[384];
                    byte[] prediction = new byte[384];
                    int[] coefficients = new int[H264Intra16Encoder.CoefficientCount];
                    int address = 0;
                    foreach ((int x, int y, int mode) in new[] { (0, 0, 2), (16, 0, 1), (0, 16, 0), (16, 16, 3) })
                    {
                        H264SourceMacroblock macroblock = new(input, x, y, samples);
                        if (syntax is not null)
                        {
                            int pattern = H264Intra16Encoder.Analyze(macroblock, reconstructed, x, y, qp, mode, prediction, coefficients);
                            if (frame != 0) { syntax.WriteSkip(address, false); }
                            syntax.WriteIntra16Type(address, mode, pattern);
                            syntax.WriteChromaMode(address);
                            syntax.WriteQuantizerDelta(0);
                            if (!H264Intra16Encoder.TryWrite(syntax, reconstructed, x, y, qp, pattern, prediction, coefficients))
                            {
                                throw new InvalidDataException("The CABAC intra16 residual could not be written.");
                            }
                            arithmetic!.WriteTermination(++address == 4);
                        }
                        else
                        {
                            if (frame != 0) { bits.WriteUnsigned(0); }
                            if (!H264Intra16Encoder.TryWrite(bits, macroblock, reconstructed, x, y, qp, qp, mode, kind))
                            {
                                throw new InvalidDataException("The intra16 reference did not fit Baseline coefficient syntax.");
                            }
                        }
                    }
                    using MediaBuffer buffer = arithmetic is null ? bits.Finish() :
                        bits.TryFinishCabac(arithmetic.BinCount, 4, 384 * 8)
                            ?? throw new InvalidDataException("The CABAC intra16 packet exceeded its byte budget.");
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

        private static byte Sample(int content, int plane, int x, int y, int frame) => content switch
        {
            0 => (byte)(64 + (x * 2 + y * 3 + plane * 35 + frame * 7) % 128),
            1 => 128,
            2 => (byte)(80 + plane * 40 + frame * 4),
            3 => plane == 0 ? (byte)((x * 7 + y * 5) % 256) : (byte)128,
            4 => (byte)(((x + y + frame) & 1) * 255),
            5 => plane == 0 ? (byte)128 : (byte)(((x / 2 + y / 2 + frame) & 1) * 255),
            _ => throw new ArgumentOutOfRangeException(nameof(content))
        };
    }
}
