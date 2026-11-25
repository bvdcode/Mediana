// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual
{
    internal partial class MicrosoftMpeg4PictureDecoder
    {
        private const int CodedFlagOffset = PredictionSize - 1;
        private const int AcColumnOffset = 7;

        private void DecodeIntra(ref Mpeg4VisualBitReader bits, scoped Span<byte> pixels, scoped Span<int> predictions,
            CancellationToken cancellationToken)
        {
            for (int mbY = 0; mbY < mbHeight; mbY++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool sliceStart = mbY % sliceHeight == 0;
                for (int mbX = 0; mbX < mbWidth; mbX++)
                {
                    int pattern = MicrosoftMpeg4Codebooks.IntraMacroblock.Read(ref bits);
                    DecodeIntraMacroblock(ref bits, pixels, predictions, mbX, mbY, sliceStart, pattern, predictPattern: true);
                    if (wmv2LoopFilter)
                    {
                        FilterMacroblock(pixels, mbX, mbY, default);
                    }
                }
            }
        }

        private void DecodeIntraMacroblock(ref Mpeg4VisualBitReader bits, scoped Span<byte> pixels, scoped Span<int> predictions,
            int mbX, int mbY, bool sliceStart, int pattern, bool predictPattern)
        {
            if (predictPattern)
            {
                pattern = PredictCodedPattern(predictions, mbX, mbY, pattern);
            }
            bool predictAc = bits.Flag();
            if (macroblockTable && pattern != 0)
            {
                lumaTable = chromaTable = ReadTable(ref bits);
            }

            Span<int> coefficients = stackalloc int[64];
            for (int block = 0; block < 6; block++)
            {
                GetBlockPosition(mbX, mbY, block, out int plane, out int x, out int y);
                coefficients.Clear();
                ReadIntraBlock(ref bits, coefficients, predictions, plane, x, y, block,
                    sliceStart, predictAc, (pattern & (32 >> block)) != 0);
                WriteBlock(coefficients, pixels, plane, x, y, intra: true);
            }
        }

        private int PredictCodedPattern(Span<int> predictions, int mbX, int mbY, int pattern)
        {
            for (int block = 0; block < 4; block++)
            {
                GetBlockPosition(mbX, mbY, block, out _, out int x, out int y);
                int left = ReadPrediction(predictions, PredictionOffset(0, x - 1, y), CodedFlagOffset);
                int above = ReadPrediction(predictions, PredictionOffset(0, x, y - 1), CodedFlagOffset);
                int diagonal = ReadPrediction(predictions, PredictionOffset(0, x - 1, y - 1), CodedFlagOffset);
                int prediction = diagonal == above ? left : above;
                int mask = 32 >> block;
                pattern ^= prediction * mask;
                int current = PredictionOffset(0, x, y);
                predictions[current + CodedFlagOffset] = (pattern & mask) != 0 ? 1 : 0;
            }
            return pattern;
        }

        private void ReadIntraBlock(ref Mpeg4VisualBitReader bits, scoped Span<int> coefficients, scoped Span<int> predictions,
            int plane, int x, int y, int block, bool sliceStart, bool predictAc, bool coded)
        {
            int current = PredictionOffset(plane, x, y);
            int left = PredictionOffset(plane, x - 1, y);
            int above = PredictionOffset(plane, x, y - 1);
            int diagonal = PredictionOffset(plane, x - 1, y - 1);
            if (!wmv2 && sliceStart && (block & 2) == 0)
            {
                above = diagonal = -1;
            }

            bool luma = plane == 0;
            int scale = GetDcScale(luma);
            int a = ReadScaledDc(predictions, left, scale);
            int b = ReadScaledDc(predictions, diagonal, scale);
            int c = ReadScaledDc(predictions, above, scale);
            int leftDifference = Math.Abs(a - b);
            int aboveDifference = Math.Abs(b - c);
            bool fromAbove = leftDifference < aboveDifference || (!wmv2 && leftDifference == aboveDifference);

            int magnitude = MicrosoftMpeg4Codebooks.Dc[dcTable * 2 + (luma ? 0 : 1)].Read(ref bits);
            bool negative = false;
            if (magnitude == 119)
            {
                magnitude = bits.Read(8);
                negative = bits.Flag();
            }
            else if (magnitude != 0)
            {
                negative = bits.Flag();
            }
            int dc = ((fromAbove ? c : a) + (negative ? -magnitude : magnitude)) * scale;
            if (dc is < 0 or > 4095)
            {
                throw new InvalidDataException("Microsoft MPEG-4 intra DC coefficient is out of range.");
            }
            coefficients[0] = dc;
            predictions[current] = dc - DcBase;

            if (coded)
            {
                int table = luma ? lumaTable : 3 + chromaTable;
                ReadCoefficients(ref bits, coefficients, table, intra: true, IntraScan(predictAc, fromAbove));
            }
            if (predictAc)
            {
                int neighbor = fromAbove ? above : left;
                int offset = fromAbove ? 0 : AcColumnOffset;
                int step = fromAbove ? 1 : 8;
                for (int i = 1; i < 8; i++)
                {
                    coefficients[i * step] += ReadPrediction(predictions, neighbor, offset + i);
                }
            }
            for (int i = 1; i < 8; i++)
            {
                predictions[current + i] = coefficients[i];
                predictions[current + AcColumnOffset + i] = coefficients[i * 8];
            }
        }

        private int GetDcScale(bool luma)
        {
            if (!wmv2)
            {
                return Mpeg4PictureDecoder.DcScale(quantizer, luma);
            }
            return luma ? Math.Max(8, quantizer / 2 + 6) : Math.Max(8, (quantizer + 1) / 2 + 6);
        }

        private ReadOnlySpan<byte> IntraScan(bool predictAc, bool fromAbove)
        {
            if (!predictAc)
            {
                return wmv2 ? Wmv2Scan.Intra : Mpeg4Scan.Zigzag;
            }
            if (fromAbove)
            {
                return wmv2 ? Wmv2Scan.Horizontal : Mpeg4Scan.Horizontal;
            }
            return wmv2 ? Wmv2Scan.Vertical : Mpeg4Scan.Vertical;
        }

        private static int ReadScaledDc(ReadOnlySpan<int> predictions, int offset, int scale) =>
            (ReadPrediction(predictions, offset, 0) + DcBase + scale / 2) / scale;

        private static int ReadPrediction(ReadOnlySpan<int> predictions, int offset, int index) =>
            offset < 0 ? 0 : predictions[offset + index];
    }
}
