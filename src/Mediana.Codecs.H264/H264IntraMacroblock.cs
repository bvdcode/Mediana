// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;

namespace Mediana.Codecs.H264
{
    internal static class H264IntraMacroblock
    {
        // Rec. ITU-T H.264, Table 9-4 (4:2:0 intra coded_block_pattern).
        internal static ReadOnlySpan<byte> CodedPatterns =>
            [47, 31, 15, 0, 23, 27, 29, 30, 7, 11, 13, 14, 39, 43, 45, 46, 16, 3, 5, 10, 12, 19, 21, 26,
                28, 35, 37, 42, 44, 1, 2, 4, 8, 17, 18, 20, 24, 6, 9, 22, 25, 32, 33, 34, 36, 40, 38, 41];

        public static void Read(ref H264BitReader bits, H264Picture target, H264SliceHeader header, int address, int type, ref int qp)
        {
            (int x, int y) = target.Grid.Position(address);
            if (type == 25)
            {
                ReadPcm(ref bits, target, x, y);
                target.Quantizers[address] = (byte)target.LumaQuantizer.Offset;
                return;
            }
            bool intra16 = type != 0;
            bool transform8x8 = !intra16 && header.Picture.Transform8x8 && bits.ReadFlag();
            target.Transforms8x8[address] = transform8x8 ? (byte)1 : (byte)0;
            int blockCount = transform8x8 ? 4 : 16;
            for (int index = 0; index < blockCount; index++)
            {
                (int blockX, int blockY) = transform8x8 ? (index % 2 * 8, index / 2 * 8) : H264Picture.LumaBlockPosition(index);
                int mode = 2;
                if (!intra16)
                {
                    int predicted = target.PredictedMode(x + blockX, y + blockY, header.Picture.ConstrainedIntraPrediction);
                    mode = predicted;
                    if (!bits.ReadFlag())
                    {
                        int remaining = (int)bits.ReadBits(3);
                        mode = remaining < predicted ? remaining : remaining + 1;
                    }
                }
                if (transform8x8)
                {
                    target.SetLuma8x8Mode(x + blockX, y + blockY, mode);
                }
                else
                {
                    target.SetLumaMode(x + blockX, y + blockY, mode);
                }
            }
            int chromaMode = bits.ReadUe(3);
            int pattern = intra16 ? (((type - 1) / 12) * 15) + ((((type - 1) / 4) % 3) << 4)
                : CodedPatterns[bits.ReadUe(47)];
            if (intra16 || pattern != 0)
            {
                H264Quantizer quantizer = target.LumaQuantizer;
                qp = quantizer.ApplyDelta(qp, bits.ReadSe(quantizer.MinimumDelta, quantizer.MaximumDelta));
            }
            target.Quantizers[address] = (byte)qp;
            H264IntraReconstruction.Read(ref bits, new H264CavlcResidualReader(target), target, header, x, y, type, chromaMode, pattern, qp);
        }

        internal static void ReadPcm(ref H264BitReader bits, H264Picture target, int x, int y)
        {
            bits.AlignZero();
            for (int plane = 0; plane < 3; plane++)
            {
                int size = plane == 0 ? 16 : 8;
                int verticalScale = target.VerticalScale(plane);
                int height = 16 / verticalScale;
                int address = target.Grid.Address(x, y);
                (int planeX, int planeY, int rowStep) = target.Grid.PixelOrigin(address, size, height);
                int stride = target.Stride(plane);
                Span<byte> pixels = target.Plane(plane);
                for (int row = 0; row < height; row++)
                {
                    for (int column = 0; column < size; column++)
                    {
                        int index = (planeY + row * rowStep) * stride + planeX + column;
                        int bitDepth = plane == 0 ? target.Sequence.LumaBitDepth : target.Sequence.ChromaBitDepth;
                        uint sample = bits.ReadBits(bitDepth);
                        if (target.SampleBytes == 1)
                        {
                            pixels[index] = (byte)sample;
                        }
                        else
                        {
                            MemoryMarshal.Cast<byte, ushort>(pixels)[index] = (ushort)sample;
                        }
                    }
                }
                for (int row = 0; row < height; row += 4)
                {
                    for (int column = 0; column < size; column += 4)
                    {
                        int position = target.BlockIndex(plane, x / (plane == 0 ? 1 : 2) + column, y / verticalScale + row);
                        target.Counts(plane)[position] = 16;
                        if (plane == 0)
                        {
                            target.Modes[position] = 2;
                        }
                    }
                }
            }
        }

    }
}
