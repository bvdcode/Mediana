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
    internal static class H264PcmEncodingTestInput
    {
        public const int FrameCount = 8;

        public static async Task VerifyAsync(string root, int mode, int quantizer)
        {
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, $"pcm-{mode}-qp{quantizer}.yuv"));
            (byte[] _, byte[] actual) = await EncodeAsync(mode, quantizer);
            if (!actual.AsSpan().SequenceEqual(expected))
            {
                throw new InvalidDataException("PCM and neighbouring transformed macroblocks differ from independent decoded pixels.");
            }
        }

        public static async Task<(byte[] Transport, byte[] Decoded)> EncodeAsync(int mode, int quantizer)
        {
            (int width, int height) = mode switch
            {
                0 => (64, 48),
                1 => (46, 34),
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
            VideoFormat format = new(width, height, VideoPixelFormat.Yuv420P8,
                new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Left),
                new SampleAspectRatio(4, 3));
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 24), allocator, new() { Quantizer = quantizer });
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(encoder.Stream, allocator);
            H264Configuration configuration = H264Configuration.Parse(encoder.Stream.Configuration.Span, new());
            using MemoryStream transport = new();
            using MemoryStream decoded = new();
            await using MpegTsWriter writer = MpegTsWriter.Create(transport, [encoder.Stream], allocator);
            H264ReferencePicture? reference = null;
            int intraPcm = 0;
            int interPcm = 0;
            int transformed = 0;
            int skipped = 0;
            int adjusted = 0;
            int restored = 0;
            try
            {
                for (int index = 0; index < FrameCount; index++)
                {
                    using VideoFrame input = Frame(format, allocator, index, mode);
                    if (encoder.TrySend(input) != CodecInputStatus.Accepted)
                    {
                        throw new InvalidOperationException("The PCM sequence encoder did not accept its input.");
                    }
                    using MediaPacket packet = encoder.Receive().Value ?? throw new InvalidDataException("Missing PCM sequence packet.");
                    H264Picture picture = Parse(packet, configuration, reference, allocator, quantizer, out int pcm, out int coded, out int skip, out int changed, out int reset);
                    using H264Picture? previous = reference?.Picture;
                    reference = new(picture, index, default);
                    using (H264Picture reconstructed = new(picture.Sequence, allocator,
                        index == 0 ? H264SliceKind.I : H264SliceKind.P))
                    {
                        using MediaBuffer? encoded = index == 0
                            ? H264IntraEncoder.Encode(input, reconstructed, quantizer, quantizer, 0, allocator, 65536, false, out _, default)
                            : H264InterEncoder.Encode(input, reconstructed, previous!, index, quantizer, allocator,
                                new() { Quantizer = quantizer }, 65536, false, out _, default);
                        if (encoded is null || !encoded.Memory.Span.SequenceEqual(packet.Data.Span))
                        {
                            throw new InvalidDataException("Direct reconstruction changed encoded packet bytes.");
                        }
                        for (int plane = 0; plane < 3; plane++)
                        {
                            if (!reconstructed.Plane(plane).SequenceEqual(picture.Plane(plane)))
                            {
                                throw new InvalidDataException("The encoder reference differs from the decoded picture after macroblock selection.");
                            }
                        }
                    }
                    if (index == 0)
                    {
                        intraPcm += pcm;
                    }
                    else
                    {
                        interPcm += pcm;
                    }
                    adjusted += changed;
                    restored += reset;
                    transformed += coded;
                    skipped += skip;
                    await writer.WritePacketAsync(packet);
                    if (decoder.TrySend(packet) != CodecInputStatus.Accepted)
                    {
                        throw new InvalidOperationException("The PCM sequence decoder did not accept its packet.");
                    }
                    using VideoFrame output = decoder.Receive().Value ?? throw new InvalidDataException("Missing PCM sequence frame.");
                    if (packet.PresentationTime != input.Timestamp || packet.DecodeTime != input.Timestamp || packet.Duration != input.Duration ||
                        output.Timestamp != input.Timestamp || output.Duration != input.Duration ||
                        output.Format.Width != format.Width || output.Format.Height != format.Height ||
                        output.Format.PixelFormat != format.PixelFormat || output.Format.Color != format.Color ||
                        output.Format.SampleAspectRatio != format.SampleAspectRatio)
                    {
                        throw new InvalidDataException("PCM encoding changed the declared picture format or timing.");
                    }
                    for (int plane = 0; plane < 3; plane++)
                    {
                        for (int row = 0; row < output.Planes[plane].Height; row++)
                        {
                            decoded.Write(output.GetRow(plane, row));
                        }
                    }
                }
                encoder.CompleteInput();
                decoder.CompleteInput();
                await writer.CompleteAsync();
                if (intraPcm == 0 || interPcm == 0 || transformed == 0 || skipped == 0 || adjusted == 0 || restored == 0 ||
                    encoder.Receive().Status != CodecOutputStatus.EndOfStream || decoder.Receive().Status != CodecOutputStatus.EndOfStream)
                {
                    throw new InvalidDataException("The complete PCM sequence did not exercise I/P PCM, transformed neighbours, skipped macroblocks and restored quantization.");
                }
            }
            finally
            {
                reference?.Picture.Dispose();
            }
            await writer.DisposeAsync();
            decoder.Dispose();
            encoder.Dispose();
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("PCM encoding retained media storage after completion.");
            }
            return (transport.ToArray(), decoded.ToArray());
        }

        private static H264Picture Parse(MediaPacket packet, H264Configuration configuration, H264ReferencePicture? reference,
            MediaBufferAllocator allocator, int quantizer, out int pcm, out int coded, out int skipped, out int adjusted, out int restored)
        {
            H264BitReader bits = H264Nal.Payload(packet.Data.Span[4..], packet.IsKeyFrame == true ? 5 : 1);
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, packet.Data.Span[4]);
            int pcmType = header.Kind switch
            {
                H264SliceKind.I => 25,
                H264SliceKind.P => 30,
                H264SliceKind.B or H264SliceKind.Sp or H264SliceKind.Si => throw new InvalidDataException("Unexpected PCM sequence slice kind."),
                _ => throw new ArgumentOutOfRangeException(nameof(header))
            };
            if (header.Qp != quantizer || header.DeblockingIdc != 1)
            {
                throw new InvalidDataException("The fixed-QP PCM sequence changed slice quantization or deblocking.");
            }
            H264Picture target = new(header.Sequence, allocator, header.Kind);
            bool accepted = false;
            pcm = 0;
            coded = 0;
            skipped = 0;
            adjusted = 0;
            restored = 0;
            try
            {
                H264ReferencePicture?[] references = reference is null ? [] : [reference];
                int qp = header.Qp;
                int count = target.Quantizers.Length;
                int address = 0;
                while (address < count)
                {
                    if (header.Kind == H264SliceKind.P)
                    {
                        int skip = bits.ReadUe(count - address);
                        skipped += skip;
                        for (int index = 0; index < skip; index++)
                        {
                            H264InterMacroblock.Read(ref bits, target, header, references, address++, ref qp, skip: true);
                        }
                        if (address == count)
                        {
                            break;
                        }
                    }
                    H264BitReader syntax = bits;
                    int type = syntax.ReadUe(30);
                    int start = bits.BitPosition;
                    int previousQuantizer = qp;
                    if (type == pcmType)
                    {
                        syntax.AlignZero();
                        for (int sample = 0; sample < 384; sample++)
                        {
                            if (syntax.ReadBits(8) == 0)
                            {
                                throw new InvalidDataException("A Baseline PCM macroblock contains a forbidden zero sample.");
                            }
                        }
                    }
                    switch (header.Kind)
                    {
                        case H264SliceKind.I:
                            H264IntraMacroblock.Read(ref bits, target, header, address, bits.ReadUe(25), ref qp);
                            break;
                        case H264SliceKind.P:
                            H264InterMacroblock.Read(ref bits, target, header, references, address, ref qp);
                            break;
                        default:
                            throw new InvalidDataException("Unexpected PCM sequence slice kind.");
                    }
                    if (bits.BitPosition - start > 3200 || qp < header.Qp)
                    {
                        throw new InvalidDataException("A macroblock exceeds 3200 RBSP bits or lowers the requested quantization.");
                    }
                    adjusted += qp > header.Qp ? 1 : 0;
                    restored += qp == header.Qp && previousQuantizer > header.Qp ? 1 : 0;
                    if (type == pcmType)
                    {
                        pcm++;
                    }
                    else
                    {
                        coded++;
                    }
                    address++;
                }
                bits.ReadTrailingBits();
                accepted = true;
                return target;
            }
            finally
            {
                if (!accepted)
                {
                    target.Dispose();
                }
            }
        }

        private static VideoFrame Frame(VideoFormat format, MediaBufferAllocator allocator, int index, int mode)
        {
            PlaneLayout[] planes = format.CreateTightLayout();
            int length = 7;
            for (int plane = 0; plane < planes.Length; plane++)
            {
                planes[plane] = new(length, planes[plane].RowBytes + mode * 13, planes[plane].RowBytes, planes[plane].Height);
                length += planes[plane].Stride * planes[plane].Height + 7;
            }
            using WritableMediaBuffer storage = allocator.Allocate(length);
            storage.Memory.Span.Fill(253);
            Random random = new(172 + index / 2);
            for (int plane = 0; plane < 3; plane++)
            {
                int size = plane == 0 ? 16 : 8;
                for (int y = 0; y < planes[plane].Height; y++)
                {
                    Span<byte> row = storage.Memory.Span.Slice(planes[plane].Offset + y * planes[plane].Stride, planes[plane].RowBytes);
                    random.NextBytes(row);
                    for (int x = 0; x < row.Length; x++)
                    {
                        int pattern = (x / size + y / size + index / 2) % 4;
                        row[x] = pattern switch
                        {
                            0 => 128,
                            1 => x % size < 4 && y % size == 0 ? (byte)0 : row[x],
                            2 => (byte)(32 + (x * 3 + y * 2 + plane * 37) % 180),
                            3 => (byte)(1 + row[x] % 255),
                            _ => throw new InvalidOperationException("Unexpected macroblock pattern.")
                        };
                    }
                }
            }
            using MediaBuffer buffer = storage.Publish();
            return new(format, buffer, planes, new(index, new(1, 24)), new(1, new(1, 24)));
        }
    }
}
