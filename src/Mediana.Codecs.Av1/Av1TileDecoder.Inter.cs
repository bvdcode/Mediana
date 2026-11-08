// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private bool ReadInter(int row, int column)
        {
            bool aboveIntra = row == 0 || motionBlocks[(row - 1) * sequence.MiColumns + column].Reference == 0;
            bool leftIntra = column == 0 || motionBlocks[row * sequence.MiColumns + column - 1].Reference == 0;
            int context;
            if (row > 0 && column > 0) { context = aboveIntra && leftIntra ? 3 : aboveIntra || leftIntra ? 1 : 0; }
            else if (row > 0 || column > 0) { context = (row > 0 ? aboveIntra : leftIntra) ? 2 : 0; }
            else { context = 0; }
            return symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.IsInterOffset + context * 3, 3)) != 0;
        }

        private void InterBlock(int row, int column, int width, int height, int skip)
        {
            if (!header.Quantization.Lossless && skip == 0 && (width != 1 || height != 1))
            {
                throw new NotSupportedException("AV1 quantized inter transforms larger than four samples are not implemented.");
            }
            Av1ReferencePictures pictures = references ?? throw new InvalidOperationException("AV1 inter prediction requires stored pictures.");
            int reference = ReadReference(row, column);
            Span<Av1MotionCandidate> storage = stackalloc Av1MotionCandidate[Av1MotionCandidates.Capacity];
            Av1MotionCandidates candidates = new(storage, motionBlocks, sequence, header, pictures, row, column, width, height, reference);
            candidates.Build();
            int mode;
            if (symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.NewMvOffset + candidates.NewContext * 3, 3)) == 0) { mode = 16; }
            else if (symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.ZeroMvOffset, 3)) == 0) { mode = 15; }
            else { mode = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.RefMvOffset + candidates.ReferenceContext * 3, 3)) == 0 ? 13 : 14; }
            int selected = mode == 14 ? 1 : 0;
            if (mode is 16 or 14)
            {
                int first = mode == 14 ? 1 : 0;
                for (int index = first; index < first + 2; index++)
                {
                    if (candidates.Count <= index + 1) { continue; }
                    int context = candidates.DrlContext(index);
                    int difference = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.DrlOffset + context * 3, 3));
                    selected = index + difference;
                    if (difference == 0) { break; }
                }
            }
            Av1MotionCandidate prediction = mode == 15 ? default : candidates[selected];
            int motionY = prediction.Row;
            int motionX = prediction.Column;
            if (mode == 16)
            {
                int joint = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.MvJointOffset, 5));
                if (joint is 2 or 3) { motionY += ReadMotionComponent(0); }
                if (joint is 1 or 3) { motionX += ReadMotionComponent(1); }
            }
            if (motionY is < -16384 or > 16383 || motionX is < -16384 or > 16383)
            {
                throw new InvalidDataException("The AV1 motion vector exceeds its permitted range.");
            }
            if (sequence.Tools.InterIntra && Math.Min(width, height) >= 2 && Math.Max(width, height) <= 8)
            {
                throw new NotSupportedException("AV1 inter-intra blending is not implemented.");
            }
            if (header.Inter.SwitchableMotion && Math.Min(width, height) >= 2)
            {
                throw new NotSupportedException("AV1 switchable motion modes are not implemented.");
            }
            int filterY = header.Inter.InterpolationFilter;
            int filterX = filterY;
            if (filterY == 4)
            {
                if (Math.Min(width, height) >= 2 && mode == 15) { filterY = filterX = 0; }
                else
                {
                    filterY = ReadInterpolation(row, column, reference, 0);
                    filterX = sequence.Tools.DualFilter ? ReadInterpolation(row, column, reference, 1) : filterY;
                }
            }
            Av1MotionBlock block = new()
            {
                Reference = (byte)reference, Mode = (byte)mode, Width = (byte)width, Height = (byte)height,
                Row = motionY, Column = motionX, FilterY = (byte)filterY, FilterX = (byte)filterX
            };
            StoreMotion(row, column, width, height, block);
            bool hasChroma = !sequence.Monochrome && !(height == 1 && (row & 1) == 0) && !(width == 1 && (column & 1) == 0);
            PredictInter(row, column, width, height, hasChroma, block, pictures);
            Residual(row, column, width, height, skip != 0, 0, 0, hasChroma, default, default, default, true);
            Av1BlockContext contextBlock = new()
            {
                WidthLog = (byte)BitOperations.Log2((uint)width), HeightLog = (byte)BitOperations.Log2((uint)height), Skip = (byte)skip
            };
            StoreContext(row, column, width, height, contextBlock);
        }

        private int ReadReference(int row, int column)
        {
            int aboveRef = row > 0 ? motionBlocks[(row - 1) * sequence.MiColumns + column].Reference : 0;
            int leftRef = column > 0 ? motionBlocks[row * sequence.MiColumns + column - 1].Reference : 0;
            if (ReadReferenceDecision(0, CountReferences(aboveRef, leftRef, 1, 4), CountReferences(aboveRef, leftRef, 5, 7)) != 0)
            {
                if (ReadReferenceDecision(1, CountReferences(aboveRef, leftRef, 5, 6), CountReferences(aboveRef, leftRef, 7, 7)) != 0) { return 7; }
                return ReadReferenceDecision(5, CountReferences(aboveRef, leftRef, 5, 5), CountReferences(aboveRef, leftRef, 6, 6)) != 0 ? 6 : 5;
            }
            if (ReadReferenceDecision(2, CountReferences(aboveRef, leftRef, 1, 2), CountReferences(aboveRef, leftRef, 3, 4)) != 0)
            {
                return ReadReferenceDecision(4, CountReferences(aboveRef, leftRef, 3, 3), CountReferences(aboveRef, leftRef, 4, 4)) != 0 ? 4 : 3;
            }
            return ReadReferenceDecision(3, CountReferences(aboveRef, leftRef, 1, 1), CountReferences(aboveRef, leftRef, 2, 2)) != 0 ? 2 : 1;
        }

        private static int CountReferences(int above, int left, int minimum, int maximum) =>
            (above >= minimum && above <= maximum ? 1 : 0) + (left >= minimum && left <= maximum ? 1 : 0);

        private int ReadReferenceDecision(int decision, int first, int second)
        {
            int context = first < second ? 0 : first == second ? 1 : 2;
            return symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.SingleRefOffset + (context * 6 + decision) * 3, 3));
        }

        private int ReadInterpolation(int row, int column, int reference, int direction)
        {
            Av1MotionBlock top = row > 0 ? motionBlocks[(row - 1) * sequence.MiColumns + column] : default;
            Av1MotionBlock prior = column > 0 ? motionBlocks[row * sequence.MiColumns + column - 1] : default;
            int topType = top.Reference == reference ? direction == 0 ? top.FilterY : top.FilterX : 3;
            int leftType = prior.Reference == reference ? direction == 0 ? prior.FilterY : prior.FilterX : 3;
            int context = topType == leftType ? topType : topType == 3 ? leftType : leftType == 3 ? topType : 3;
            return symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.InterpolationOffset + (direction * 8 + context) * 4, 4));
        }

        private int ReadMotionComponent(int component)
        {
            int sign = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.MvSignOffset + component * 3, 3));
            int category = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.MvClassOffset + component * 12, 12));
            int integer = 0;
            int fraction;
            int high;
            int magnitude;
            if (category == 0)
            {
                integer = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.MvClass0BitOffset + component * 3, 3));
                fraction = header.Inter.IntegerMotion ? 3 : symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.MvClass0FrOffset + (component * 2 + integer) * 5, 5));
                high = header.Inter.HighPrecisionMotion ? symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.MvClass0HpOffset + component * 3, 3)) : 1;
                magnitude = ((integer << 3) | (fraction << 1) | high) + 1;
            }
            else
            {
                for (int index = 0; index < category; index++)
                {
                    integer |= symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.MvBitOffset + (component * 10 + index) * 3, 3)) << index;
                }
                fraction = header.Inter.IntegerMotion ? 3 : symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.MvFrOffset + component * 5, 5));
                high = header.Inter.HighPrecisionMotion ? symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.MvHpOffset + component * 3, 3)) : 1;
                magnitude = (2 << (category + 2)) + ((integer << 3) | (fraction << 1) | high) + 1;
            }
            return sign != 0 ? -magnitude : magnitude;
        }

        private void StoreMotion(int row, int column, int width, int height, Av1MotionBlock block)
        {
            if (motionBlocks.IsEmpty) { return; }
            for (int y = row; y < Math.Min(row + height, sequence.MiRows); y++)
            {
                motionBlocks.Slice(y * sequence.MiColumns + column, Math.Min(width, sequence.MiColumns - column)).Fill(block);
            }
        }
    }
}
