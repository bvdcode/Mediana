// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Video;

namespace Mediana.Codecs.Dv
{
    internal static class DvVideoSegment
    {
        private const int Macroblocks = 5;
        private const int TransformsPerMacroblock = 6;
        private const int TransformCount = Macroblocks * TransformsPerMacroblock;
        private const int CoefficientsPerBlock = 64;
        private const int BlockBytes = DvFrameHeader.DifBlockBytes;
        private const int PictureWidth = DvFrameHeader.Width;
        private const int LumaBytes = PictureWidth * DvFrameHeader.Height;
        private const int ChromaBytes = LumaBytes / 4;

        private static ReadOnlySpan<byte> HorizontalGroups => [18, 9, 27, 0, 36];
        private static ReadOnlySpan<byte> VerticalSequenceOffsets => [2, 6, 8, 0, 4];

        internal static void Decode(ReadOnlySpan<byte> segment, Span<byte> picture, int sequence, int segmentIndex)
        {
            Span<int> coefficients = stackalloc int[TransformCount * CoefficientsPerBlock];
            Span<DvBlockState> blocks = stackalloc DvBlockState[TransformCount];
            Span<byte> macroblockBits = stackalloc byte[BlockBytes];
            Span<byte> segmentBits = stackalloc byte[Macroblocks * BlockBytes];
            DvBitReservoir segmentReservoir = new(segmentBits);
            coefficients.Clear();

            for (int macroblock = 0; macroblock < Macroblocks; macroblock++)
            {
                ReadOnlySpan<byte> dif = segment.Slice(macroblock * BlockBytes, BlockBytes);
                if ((dif[0] >> 5) != 4 || (dif[1] >> 4) != sequence || dif[2] != segmentIndex * Macroblocks + macroblock)
                {
                    throw new InvalidDataException("A DV video DIF block is out of sequence.");
                }
                int first = macroblock * TransformsPerMacroblock;
                DvBitReservoir macroblockReservoir = new(macroblockBits);
                ReadMacroblock(dif, coefficients, blocks, first, ref macroblockReservoir);
                DvBitReader spare = macroblockReservoir.Open();
                if (ResumeIncomplete(ref spare, coefficients, blocks, first, first + TransformsPerMacroblock))
                {
                    segmentReservoir.Append(ref spare);
                }
            }

            DvBitReader segmentSpare = segmentReservoir.Open();
            if (!ResumeIncomplete(ref segmentSpare, coefficients, blocks, 0, TransformCount))
            {
                throw new InvalidDataException("A DV video segment ended before its transform blocks were complete.");
            }
            WriteMacroblocks(picture, coefficients, blocks, sequence, segmentIndex);
        }

        private static void ReadMacroblock(ReadOnlySpan<byte> dif, scoped Span<int> coefficients, scoped Span<DvBlockState> blocks,
            int first, scoped ref DvBitReservoir reservoir)
        {
            int quantizer = dif[3] & 15;
            int offset = 4;
            for (int transform = 0; transform < TransformsPerMacroblock; transform++)
            {
                int bytes = transform < 4 ? 14 : 10;
                DvBitReader bits = new(dif.Slice(offset, bytes), bytes * 8);
                int index = first + transform;
                DvCoefficients.ReadBlock(ref bits, quantizer, Block(coefficients, index), out blocks[index]);
                if (blocks[index].Complete)
                {
                    reservoir.Append(ref bits);
                }
                offset += bytes;
            }
        }

        private static bool ResumeIncomplete(scoped ref DvBitReader bits, scoped Span<int> coefficients,
            scoped Span<DvBlockState> blocks, int first, int end)
        {
            for (int i = first; i < end; i++)
            {
                if (blocks[i].Complete)
                {
                    continue;
                }
                DvCoefficients.ReadAc(ref bits, ref blocks[i], Block(coefficients, i));
                if (!blocks[i].Complete)
                {
                    return false;
                }
            }
            return true;
        }

        private static void WriteMacroblocks(Span<byte> picture, Span<int> coefficients,
            ReadOnlySpan<DvBlockState> blocks, int sequence, int segmentIndex)
        {
            int scanRow = segmentIndex % 6;
            if (scanRow > 2)
            {
                scanRow = 5 - scanRow;
            }
            for (int macroblock = 0; macroblock < Macroblocks; macroblock++)
            {
                int x = HorizontalGroups[macroblock] + segmentIndex / 3;
                int y = ((sequence + VerticalSequenceOffsets[macroblock]) % DvFrameHeader.SequenceCount) * 3 + scanRow;
                int luma = (y * PictureWidth + x) * 16;
                int chroma = (y * (PictureWidth / 2) + x) * 8;
                int first = macroblock * TransformsPerMacroblock;
                Write(first, luma, PictureWidth, picture, coefficients, blocks);
                Write(first + 1, luma + 8, PictureWidth, picture, coefficients, blocks);
                Write(first + 2, luma + 8 * PictureWidth, PictureWidth, picture, coefficients, blocks);
                Write(first + 3, luma + 8 * PictureWidth + 8, PictureWidth, picture, coefficients, blocks);
                Write(first + 4, LumaBytes + ChromaBytes + chroma, PictureWidth / 2, picture, coefficients, blocks);
                Write(first + 5, LumaBytes + chroma, PictureWidth / 2, picture, coefficients, blocks);
            }
        }

        private static Span<int> Block(Span<int> coefficients, int index) =>
            coefficients.Slice(index * CoefficientsPerBlock, CoefficientsPerBlock);

        private static void Write(int index, int offset, int stride, Span<byte> picture,
            Span<int> coefficients, ReadOnlySpan<DvBlockState> blocks) =>
            DvInverseTransform.Write(Block(coefficients, index), picture[offset..], stride, blocks[index].IsFieldTransform);
    }
}
