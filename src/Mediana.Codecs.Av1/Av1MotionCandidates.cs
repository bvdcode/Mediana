// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1MotionCandidates(Span<Av1MotionCandidate> candidateStorage, ReadOnlySpan<Av1MotionBlock> blockStorage,
        Av1SequenceHeader sequence, Av1FrameHeader header, Av1TileBounds bounds, Av1ReferencePictures pictures, int row, int column, int width, int height, int reference, ReadOnlySpan<Av1MotionFieldSample> temporalStorage = default, int secondReference = 0)
    {
        private readonly Span<Av1MotionCandidate> candidates = candidateStorage;
        private readonly ReadOnlySpan<Av1MotionBlock> blocks = blockStorage;
        private readonly ReadOnlySpan<Av1MotionFieldSample> temporal = temporalStorage;
        internal const int Capacity = 8;
        private const int NearbyWeight = 640;
        private int count;
        private int newCount;
        internal int Count => count;
        internal int NewContext { get; private set; }
        internal int ReferenceContext { get; private set; }
        internal int ZeroContext { get; private set; }
        internal Av1MotionCandidate this[int index] => candidates[index];

        internal void Build()
        {
            bool above = ScanRow(-1);
            bool left = ScanColumn(-1);
            if (Math.Max(width, height) <= 16) { above |= Point(-1, width); }
            int close = (above ? 1 : 0) + (left ? 1 : 0);
            int nearest = count;
            int nearbyNew = newCount;
            for (int index = 0; index < nearest; index++)
            {
                candidates[index] = candidates[index] with { Weight = candidates[index].Weight + NearbyWeight };
            }
            if (header.Inter.ReferenceMotion) { ScanTemporal(); }
            above |= Point(-1, -1);
            above |= ScanRow(-3);
            left |= ScanColumn(-3);
            if (height > 1) { above |= ScanRow(-5); }
            if (width > 1) { left |= ScanColumn(-5); }
            int total = (above ? 1 : 0) + (left ? 1 : 0);
            Sort(0, nearest);
            Sort(nearest, count);
            if (count < 2)
            {
                if (secondReference == 0) { Extra(); }
                else { ExtraCompound(); }
            }
            int borderRow = 128 + height * 32;
            int borderColumn = 128 + width * 32;
            for (int index = 0; index < count; index++)
            {
                Av1MotionCandidate motion = candidates[index];
                candidates[index] = motion with
                {
                    Row = Math.Clamp(motion.Row, -row * 32 - borderRow, (sequence.MiRows - height - row) * 32 + borderRow),
                    Column = Math.Clamp(motion.Column, -column * 32 - borderColumn, (sequence.MiColumns - width - column) * 32 + borderColumn),
                    SecondRow = Math.Clamp(motion.SecondRow, -row * 32 - borderRow, (sequence.MiRows - height - row) * 32 + borderRow),
                    SecondColumn = Math.Clamp(motion.SecondColumn, -column * 32 - borderColumn, (sequence.MiColumns - width - column) * 32 + borderColumn)
                };
            }
            NewContext = close switch
            {
                0 => Math.Min(total, 1),
                1 => 3 - Math.Min(nearbyNew, 1),
                2 => 5 - Math.Min(nearbyNew, 1),
                _ => throw new InvalidDataException("The AV1 close motion-candidate count is invalid.")
            };
            ReferenceContext = close switch
            {
                0 => total,
                1 => 2 + total,
                2 => 5,
                _ => throw new InvalidDataException("The AV1 close motion-candidate count is invalid.")
            };
        }

        internal int DrlContext(int index)
        {
            if (index + 1 >= count) { return 0; }
            if (candidates[index].Weight < NearbyWeight) { return 2; }
            return candidates[index + 1].Weight < NearbyWeight ? 1 : 0;
        }

        private bool ScanRow(int delta)
        {
            int offset = 0;
            if (delta < -1)
            {
                delta += row & 1;
                offset = 1 - (column & 1);
            }
            bool matched = false;
            int end = Math.Min(Math.Min(width, sequence.MiColumns - column), 16);
            for (int index = 0; index < end;)
            {
                int y = row + delta;
                int x = column + offset + index;
                if (!Inside(y, x)) { break; }
                Av1MotionBlock block = blocks[y * sequence.MiColumns + x];
                int length = Math.Min(width, block.Width);
                if (delta < -1) { length = Math.Max(2, length); }
                if (width >= 16) { length = Math.Max(4, length); }
                if (length == 0) { break; }
                matched |= Add(block, length * 2);
                index += length;
            }
            return matched;
        }

        private bool ScanColumn(int delta)
        {
            int offset = 0;
            if (delta < -1)
            {
                offset = 1 - (row & 1);
                delta += column & 1;
            }
            bool matched = false;
            int end = Math.Min(Math.Min(height, sequence.MiRows - row), 16);
            for (int index = 0; index < end;)
            {
                int y = row + offset + index;
                int x = column + delta;
                if (!Inside(y, x)) { break; }
                Av1MotionBlock block = blocks[y * sequence.MiColumns + x];
                int length = Math.Min(height, block.Height);
                if (delta < -1) { length = Math.Max(2, length); }
                if (height >= 16) { length = Math.Max(4, length); }
                if (length == 0) { break; }
                matched |= Add(block, length * 2);
                index += length;
            }
            return matched;
        }

        private bool Point(int deltaRow, int deltaColumn)
        {
            int y = row + deltaRow;
            int x = column + deltaColumn;
            return Inside(y, x) && Add(blocks[y * sequence.MiColumns + x], 4);
        }

        private bool Add(Av1MotionBlock block, int weight)
        {
            if (block.Width == 0 || block.Reference == 0) { return false; }
            if (secondReference != 0)
            {
                if (block.Reference != reference || block.SecondReference != secondReference) { return false; }
                if (HasNewMotion(block.Mode)) { newCount++; }
                AddVector(Lower(block.Row), Lower(block.Column), weight, Lower(block.SecondRow), Lower(block.SecondColumn));
                return true;
            }
            bool matched = false;
            for (int list = 0; list < 2; list++)
            {
                if ((list == 0 ? block.Reference : block.SecondReference) != reference) { continue; }
                if (HasNewMotion(block.Mode)) { newCount++; }
                AddVector(Lower(list == 0 ? block.Row : block.SecondRow), Lower(list == 0 ? block.Column : block.SecondColumn), weight);
                matched = true;
            }
            return matched;
        }

        private void AddVector(int y, int x, int weight, int secondY = 0, int secondX = 0)
        {
            for (int index = 0; index < count; index++)
            {
                if (candidates[index].Row == y && candidates[index].Column == x &&
                    candidates[index].SecondRow == secondY && candidates[index].SecondColumn == secondX)
                {
                    candidates[index] = candidates[index] with { Weight = candidates[index].Weight + weight };
                    return;
                }
            }
            if (count < Capacity) { candidates[count++] = new(y, x, weight, secondY, secondX); }
        }

        private void ScanTemporal()
        {
            int stepWidth = width >= 16 ? 4 : 2;
            int stepHeight = height >= 16 ? 4 : 2;
            for (int y = 0; y < Math.Min(height, 16); y += stepHeight)
            {
                for (int x = 0; x < Math.Min(width, 16); x += stepWidth) { TemporalPoint(y, x); }
            }
            if (height is >= 2 and < 16 && width is >= 2 and < 16)
            {
                TemporalExtension(height, -2);
                TemporalExtension(height, width);
                TemporalExtension(height - 2, width);
            }
        }

        private void TemporalExtension(int y, int x)
        {
            if ((row & 15) + y is >= 0 and < 16 && (column & 15) + x is >= 0 and < 16) { TemporalPoint(y, x); }
        }

        private void TemporalPoint(int deltaRow, int deltaColumn)
        {
            int y = (row + deltaRow) | 1;
            int x = (column + deltaColumn) | 1;
            if (!Inside(y, x)) { return; }
            bool first = deltaRow == 0 && deltaColumn == 0;
            if (first) { ZeroContext = 1; }
            if (!Av1MotionField.TryGet(temporal, sequence, header, pictures, reference, y >> 1, x >> 1, out Av1MotionCandidate motion)) { return; }
            int motionY = Lower(motion.Row);
            int motionX = Lower(motion.Column);
            int secondY = 0;
            int secondX = 0;
            if (secondReference != 0)
            {
                if (!Av1MotionField.TryGet(temporal, sequence, header, pictures, secondReference, y >> 1, x >> 1, out Av1MotionCandidate second)) { return; }
                secondY = Lower(second.Row);
                secondX = Lower(second.Column);
            }
            if (first) { ZeroContext = Math.Abs(motionY) >= 16 || Math.Abs(motionX) >= 16 || Math.Abs(secondY) >= 16 || Math.Abs(secondX) >= 16 ? 1 : 0; }
            AddVector(motionY, motionX, 2, secondY, secondX);
        }

        private void Sort(int start, int end)
        {
            for (int index = start + 1; index < end; index++)
            {
                Av1MotionCandidate current = candidates[index];
                int target = index;
                while (target > start && candidates[target - 1].Weight < current.Weight)
                {
                    candidates[target] = candidates[target - 1];
                    target--;
                }
                candidates[target] = current;
            }
        }

        private void Extra()
        {
            int extent = Math.Min(Math.Min(Math.Min(width, height), 16), Math.Min(sequence.MiColumns - column, sequence.MiRows - row));
            for (int pass = 0; pass < 2; pass++)
            {
                for (int index = 0; index < extent && count < 2;)
                {
                    int y = pass == 0 ? row - 1 : row + index;
                    int x = pass == 0 ? column + index : column - 1;
                    if (!Inside(y, x)) { break; }
                    Av1MotionBlock block = blocks[y * sequence.MiColumns + x];
                    for (int list = 0; list < 2; list++)
                    {
                        int candidateReference = list == 0 ? block.Reference : block.SecondReference;
                        if (candidateReference == 0) { continue; }
                        int sign = Backward(candidateReference) == Backward(reference) ? 1 : -1;
                        int motionY = (list == 0 ? block.Row : block.SecondRow) * sign;
                        int motionX = (list == 0 ? block.Column : block.SecondColumn) * sign;
                        bool duplicate = false;
                        for (int candidate = 0; candidate < count; candidate++)
                        {
                            duplicate |= candidates[candidate].Row == motionY && candidates[candidate].Column == motionX;
                        }
                        if (!duplicate) { candidates[count++] = new(motionY, motionX, 2); }
                    }
                    int length = pass == 0 ? block.Width : block.Height;
                    if (length == 0) { break; }
                    index += length;
                }
            }
            for (int index = count; index < 2; index++) { candidates[index] = default; }
        }

        private bool Backward(int candidate)
        {
            int bits = sequence.Tools.OrderHintBits;
            if (bits == 0) { return false; }
            int hint = pictures.OrderHint(header.Inter.References[candidate - 1]);
            return Av1ReferenceOrdering.Distance(hint, header.OrderHint, bits) > 0;
        }

        private int Lower(int value)
        {
            if (header.Inter.HighPrecisionMotion) { return value; }
            if (header.Inter.IntegerMotion)
            {
                int rounded = (Math.Abs(value) + 3) >> 3;
                return value > 0 ? rounded << 3 : -(rounded << 3);
            }
            return value > 0 ? value & ~1 : -((-value) & ~1);
        }

        private bool Inside(int y, int x) => bounds.Contains(y, x);

        private static bool HasNewMotion(int mode) => mode is 16 or 19 or 20 or 21 or 22 or 24;
    }
}
