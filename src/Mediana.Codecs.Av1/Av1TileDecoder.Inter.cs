// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private bool ReadInter(int row, int column)
        {
            if (Segment.ForcedReference) { return Segment.Reference != 0; }
            if (Segment.GlobalMotion) { return true; }
            bool aboveIntra = row == bounds.RowStart || motionBlocks[(row - 1) * sequence.MiColumns + column].Reference == 0;
            bool leftIntra = column == bounds.ColumnStart || motionBlocks[row * sequence.MiColumns + column - 1].Reference == 0;
            int context;
            if (row > bounds.RowStart && column > bounds.ColumnStart) { context = aboveIntra && leftIntra ? 3 : aboveIntra || leftIntra ? 1 : 0; }
            else if (row > bounds.RowStart || column > bounds.ColumnStart) { context = (row > bounds.RowStart ? aboveIntra : leftIntra) ? 2 : 0; }
            else { context = 0; }
            return symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.IsInterOffset + context * 3, 3)) != 0;
        }

        private void InterBlock(int row, int column, int width, int height, int skip, bool skipMode)
        {
            Av1ReferencePictures pictures = references ?? throw new InvalidOperationException("AV1 inter prediction requires stored pictures.");
            Av1ReferencePair pair = ReadReference(row, column, width, height, skipMode, pictures);
            int reference = pair.First;
            bool compound = pair.Second != 0;
            Span<Av1MotionCandidate> storage = stackalloc Av1MotionCandidate[Av1MotionCandidates.Capacity];
            Av1MotionCandidates candidates = new(storage, motionBlocks, sequence, header, bounds, pictures, row, column, width, height, reference, motionField, pair.Second);
            candidates.Build();
            int mode;
            if (skipMode) { mode = 17; }
            else if (Segment.Skip || Segment.GlobalMotion) { mode = 15; }
            else if (compound)
            {
                ReadOnlySpan<byte> contexts = [0, 1, 1, 1, 1, 1, 2, 3, 4, 4, 4, 4, 5, 6, 7];
                int context = contexts[(candidates.ReferenceContext >> 1) * 5 + Math.Min(candidates.NewContext, 4)];
                mode = 17 + symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.CompoundModeOffset + context * 9, 9));
            }
            else if (symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.NewMvOffset + candidates.NewContext * 3, 3)) == 0) { mode = 16; }
            else if (symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.ZeroMvOffset + candidates.ZeroContext * 3, 3)) == 0) { mode = 15; }
            else { mode = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.RefMvOffset + candidates.ReferenceContext * 3, 3)) == 0 ? 13 : 14; }
            bool near = mode is 14 or 18 or 21 or 22;
            int selected = near ? 1 : 0;
            if (mode is 16 or 24 || near)
            {
                int first = near ? 1 : 0;
                for (int index = first; index < first + 2; index++)
                {
                    if (candidates.Count <= index + 1) { continue; }
                    int context = candidates.DrlContext(index);
                    int difference = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.DrlOffset + context * 3, 3));
                    selected = index + difference;
                    if (difference == 0) { break; }
                }
            }
            Av1MotionBlock block = new()
            {
                Reference = (byte)reference, SecondReference = (byte)pair.Second, Mode = (byte)mode,
                Width = (byte)width, Height = (byte)height, SkipMode = skipMode, CompoundIndex = 1
            };
            for (int list = 0; list < (compound ? 2 : 1); list++)
            {
                int vectorMode = VectorMode(mode, list);
                int position = vectorMode == 13 || vectorMode == 16 && candidates.Count <= 1 ? 0 : selected;
                Av1MotionCandidate prediction = vectorMode == 15 ? candidates.Global : candidates[position];
                int motionY = list == 0 ? prediction.Row : prediction.SecondRow;
                int motionX = list == 0 ? prediction.Column : prediction.SecondColumn;
                if (vectorMode == 16)
                {
                    int joint = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.MvJointOffset, 5));
                    if (joint is 2 or 3) { motionY += ReadMotionComponent(0); }
                    if (joint is 1 or 3) { motionX += ReadMotionComponent(1); }
                }
                if (motionY is <= -16384 or >= 16384 || motionX is <= -16384 or >= 16384)
                {
                    throw new InvalidDataException("The AV1 motion vector exceeds its permitted range.");
                }
                if (list == 0) { block.Row = motionY; block.Column = motionX; }
                else { block.SecondRow = (short)motionY; block.SecondColumn = (short)motionX; }
            }
            Av1InterIntraPrediction intra = compound ? default : ReadInterIntra(width, height);
            block.InterIntra = intra.Enabled;
            Av1WarpParameters warp = default;
            if (!compound && !intra.Enabled) { block.MotionMode = ReadMotionMode(row, column, width, height, block, pictures, out warp); }
            Av1CompoundPrediction compoundPrediction = compound ? ReadCompoundPrediction(row, column, width, height, ref block, pictures) : default;
            int filterY = header.Inter.InterpolationFilter;
            int filterX = filterY;
            if (filterY == 4)
            {
                bool global = Math.Min(width, height) >= 2 && mode is 15 or 23;
                bool translation = header.GlobalMotion[reference].Type == Av1GlobalMotionType.Translation ||
                    compound && header.GlobalMotion[pair.Second].Type == Av1GlobalMotionType.Translation;
                if (skipMode || block.MotionMode == 2 || global && !translation) { filterY = filterX = 0; }
                else
                {
                    filterY = ReadInterpolation(row, column, reference, 0, compound);
                    filterX = sequence.Tools.DualFilter ? ReadInterpolation(row, column, reference, 1, compound) : filterY;
                }
            }
            block.FilterY = (byte)filterY;
            block.FilterX = (byte)filterX;
            StoreMotion(row, column, width, height, block);
            bool hasChroma = !sequence.Monochrome && !(height == 1 && (row & 1) == 0) && !(width == 1 && (column & 1) == 0);
            PredictInter(row, column, width, height, hasChroma, block, pictures, compoundPrediction, warp);
            if (block.MotionMode == 1) { PredictOverlap(row, column, width, height, hasChroma, pictures); }
            if (intra.Enabled) { BlendInterIntra(row, column, width, height, hasChroma, intra); }
            StoreFilterBlock(row, column, width, height, reference, mode, skip != 0);
            Av1TransformSize transform = ReadInterTransformSizes(row, column, width, height, skip != 0);
            Residual(row, column, width, height, skip != 0, default, hasChroma, default, default, transform, true);
            Av1BlockContext contextBlock = new()
            {
                WidthLog = (byte)BitOperations.Log2((uint)width), HeightLog = (byte)BitOperations.Log2((uint)height), Skip = (byte)skip
            };
            StoreContext(row, column, width, height, contextBlock);
        }

        private static int VectorMode(int mode, int list) => (mode, list) switch
        {
            (>= 13 and <= 16, 0) => mode,
            (17 or 19, 0) or (17 or 20, 1) => 13,
            (18 or 21, 0) or (18 or 22, 1) => 14,
            (20 or 22 or 24, 0) or (19 or 21 or 24, 1) => 16,
            (23, 0 or 1) => 15,
            _ => throw new InvalidDataException("The AV1 inter prediction mode is invalid.")
        };

        private int ReadInterpolation(int row, int column, int reference, int direction, bool compound)
        {
            Av1MotionBlock top = row > bounds.RowStart ? motionBlocks[(row - 1) * sequence.MiColumns + column] : default;
            Av1MotionBlock prior = column > bounds.ColumnStart ? motionBlocks[row * sequence.MiColumns + column - 1] : default;
            int topType = top.Reference == reference || top.SecondReference == reference ? direction == 0 ? top.FilterY : top.FilterX : 3;
            int leftType = prior.Reference == reference || prior.SecondReference == reference ? direction == 0 ? prior.FilterY : prior.FilterX : 3;
            int context = topType == leftType ? topType : topType == 3 ? leftType : leftType == 3 ? topType : 3;
            return symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.InterpolationOffset + (direction * 8 + (compound ? 4 : 0) + context) * 4, 4));
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
