// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal static class H264IntraModeTestInput
    {
        public static void Verify()
        {
            for (int mode = 0; mode < 9; mode++)
            {
                VerifyMode(mode);
            }
            VerifyAvailability();
            VerifyEmptyResidual();
        }

        public static void VerifyMode(int mode)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264Picture target = new(new() { WidthInMbs = 2, HeightInMapUnits = 2, FrameMbsOnly = true }, allocator, H264SliceKind.I))
            using (VideoFrame input = CreateFrame(allocator, Expected(mode)))
            {
                target.Modes.Fill(2);
                ReadOnlySpan<byte> top = [12, 231, 64, 179, 26, 207, 102, 151];
                ReadOnlySpan<byte> left = [193, 47, 218, 83];
                top.CopyTo(target.Plane(0).Slice(7 * 32 + 8, 8));
                for (int row = 0; row < 4; row++)
                {
                    target.Plane(0)[(8 + row) * 32 + 7] = left[row];
                }
                target.Plane(0)[7 * 32 + 7] = 117;
                byte[] original = target.Plane(0).ToArray();
                Span<byte> prediction = stackalloc byte[16];
                Span<byte> samples = stackalloc byte[H264SourceMacroblock.ByteCount];
                H264SourceMacroblock source = new(input, 8, 8, samples);
                int selected = H264IntraModeSelection.Select(source.Luma, target, 8, 8, prediction);
                if (selected != mode || !prediction.SequenceEqual(Expected(mode)) || !target.Plane(0).SequenceEqual(original))
                {
                    throw new InvalidDataException($"Intra4x4 mode {mode} did not select its exact independent directional samples without changing references.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Directional prediction retained media storage.");
            }
        }

        public static void VerifyAvailability()
        {
            ReadOnlySpan<int> masks = [4, 141, 262, 399, 4, 141, 262, 511];
            for (int flags = 0; flags < masks.Length; flags++)
            {
                for (int mode = 0; mode < 9; mode++)
                {
                    bool available = H264IntraPrediction.IsLumaModeAvailable(mode, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0);
                    if (available != ((masks[flags] & (1 << mode)) != 0))
                    {
                        throw new InvalidDataException("Directional prediction accepted an unavailable neighbour or rejected a legal direction.");
                    }
                }
            }
        }

        public static void VerifyEmptyResidual()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (VideoFrame input = CreateFrame(allocator, []))
            using (H264Encoder encoder = H264Encoder.Create(input.Format, new(1, 1000), allocator))
            {
                if (encoder.TrySend(input) != CodecInputStatus.Accepted)
                {
                    throw new InvalidOperationException("The intra encoder did not accept a flat picture.");
                }
                using MediaPacket packet = encoder.Receive().Value ?? throw new InvalidDataException("Missing flat picture.");
                H264Configuration configuration = H264Configuration.Parse(encoder.Stream.Configuration.Span, new());
                H264BitReader bits = H264Nal.Payload(packet.Data.Span[4..], 5);
                H264SliceHeaderReader.Read(ref bits, configuration, packet.Data.Span[4]);
                for (int macroblock = 0; macroblock < 4; macroblock++)
                {
                    if (bits.ReadUe() != 0)
                    {
                        throw new InvalidDataException("Flat IDR requires an intra4x4 macroblock.");
                    }
                    for (int block = 0; block < 16; block++)
                    {
                        if (!bits.ReadFlag())
                        {
                            throw new InvalidDataException("Equal predictions must prefer the one-bit predicted mode.");
                        }
                    }
                    if (bits.ReadUe() != 0 || bits.ReadUe() != 3)
                    {
                        throw new InvalidDataException("An exact flat prediction wrote unnecessary residual syntax.");
                    }
                }
                bits.ReadTrailingBits();
                using H264Picture decoded = H264IntraDecoder.Decode(packet.Data.Span[4..], configuration, allocator);
                for (int plane = 0; plane < 3; plane++)
                {
                    if (decoded.Plane(plane).ContainsAnyExcept((byte)128))
                    {
                        throw new InvalidDataException("Omitting zero residuals changed a flat picture.");
                    }
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Flat intra encoding retained media storage.");
            }
        }

        private static VideoFrame CreateFrame(MediaBufferAllocator allocator, ReadOnlySpan<byte> block)
        {
            VideoFormat format = new(32, 32, VideoPixelFormat.Yuv420P8,
                new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Left),
                new SampleAspectRatio(1, 1));
            PlaneLayout[] planes = format.CreateTightLayout();
            using WritableMediaBuffer storage = allocator.Allocate(32 * 32 * 3 / 2);
            storage.Memory.Span.Fill(128);
            for (int row = 0; row < block.Length / 4; row++)
            {
                block.Slice(row * 4, 4).CopyTo(storage.Memory.Span.Slice((8 + row) * 32 + 8, 4));
            }
            using MediaBuffer buffer = storage.Publish();
            return new(format, buffer, planes, new(0, new(1, 1000)), new(40, new(1, 1000)));
        }

        // Rec. ITU-T H.264 8.3.1.2: top 12/231/64/179/26/207/102/151, left 193/47/218/83, corner 117.
        private static ReadOnlySpan<byte> Expected(int mode) => mode switch
        {
            0 => [12, 231, 64, 179, 12, 231, 64, 179, 12, 231, 64, 179, 12, 231, 64, 179],
            1 => [193, 193, 193, 193, 47, 47, 47, 47, 218, 218, 218, 218, 83, 83, 83, 83],
            2 => [128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128],
            3 => [135, 135, 112, 110, 135, 112, 110, 136, 112, 110, 136, 141, 110, 136, 141, 139],
            4 => [110, 93, 135, 135, 138, 110, 93, 135, 126, 138, 110, 93, 142, 126, 138, 110],
            5 => [65, 122, 148, 122, 110, 93, 135, 135, 138, 65, 122, 148, 126, 110, 93, 135],
            6 => [155, 110, 93, 135, 120, 138, 155, 110, 133, 126, 120, 138, 151, 142, 133, 126],
            7 => [122, 148, 122, 103, 135, 135, 112, 110, 148, 122, 103, 117, 135, 112, 110, 136],
            8 => [120, 126, 133, 142, 133, 142, 151, 117, 151, 117, 83, 83, 83, 83, 83, 83],
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }
}
