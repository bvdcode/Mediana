// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp8
{
    internal static class Vp8MotionReader
    {
        private static ReadOnlySpan<sbyte> ReferenceTree => [0, 2, -1, 4, -2, 6, -3, -4];
        private static ReadOnlySpan<byte> ModeContexts =>
        [7, 1, 1, 143, 14, 18, 14, 107, 135, 64, 57, 68, 60, 56, 128, 65, 159, 134, 128, 34, 234, 188, 128, 28];
        private static ReadOnlySpan<byte> SubContexts => [147, 136, 18, 106, 145, 1, 179, 121, 1, 223, 1, 34, 208, 1, 1];
        private static ReadOnlySpan<byte> Partitions =>
        [0,0,0,0,0,0,0,0,1,1,1,1,1,1,1,1,
         0,0,1,1,0,0,1,1,0,0,1,1,0,0,1,1,
         0,0,1,1,0,0,1,1,2,2,3,3,2,2,3,3,
         0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15];

        internal static void Read(Vp8FrameHeader header, ref Vp8Macroblock current, in Vp8Macroblock left,
            in Vp8Macroblock above, in Vp8Macroblock aboveLeft, int column, int row, int columns, int rows)
        {
            Vp8BooleanReader control = header.Control;
            current.Reference = control.Read(header.LastProbability) ? 2 + (control.Read(header.GoldenProbability) ? 1 : 0) : 1;
            Span<Vp8MotionVector> candidates = stackalloc Vp8MotionVector[4];
            Span<int> counts = stackalloc int[4];
            candidates.Clear(); counts.Clear(); int cursor = 0;
            Accumulate(above, 2, current.Reference, header.SignBias, candidates, counts, ref cursor);
            Accumulate(left, 2, current.Reference, header.SignBias, candidates, counts, ref cursor);
            Accumulate(aboveLeft, 1, current.Reference, header.SignBias, candidates, counts, ref cursor);
            if (counts[3] != 0 && candidates[3] == candidates[1]) { counts[1]++; }
            counts[3] = (IsSplit(above) + IsSplit(left)) * 2 + IsSplit(aboveLeft);
            if (counts[2] > counts[1]) { (counts[1], counts[2]) = (counts[2], counts[1]); (candidates[1], candidates[2]) = (candidates[2], candidates[1]); }
            Vp8MotionVector best = (counts[1] >= counts[0] ? candidates[1] : default).Clamp(column, row, columns, rows);
            Span<byte> probabilities = stackalloc byte[4];
            for (int i = 0; i < 4; i++) { probabilities[i] = ModeContexts[counts[i] * 4 + i]; }
            current.Mode = control.Tree(ReferenceTree, probabilities);
            switch (current.Mode)
            {
                case 0: current.Motion = default; break;
                case 1: current.Motion = candidates[1].Clamp(column, row, columns, rows); break;
                case 2: current.Motion = candidates[2].Clamp(column, row, columns, rows); break;
                case 3: current.Motion = ReadVector(control, header.MotionProbabilities).Add(best); break;
                case 4: ReadSplit(header, ref current, left, above, best); current.Motion = current.Blocks[15]; break;
                default: throw new InvalidDataException("Invalid VP8 inter prediction mode.");
            }
        }

        private static int IsSplit(in Vp8Macroblock block) => block.Reference != 0 && block.Mode == 4 ? 1 : 0;

        private static void Accumulate(in Vp8Macroblock block, int weight, int reference, ReadOnlySpan<int> biases,
            Span<Vp8MotionVector> candidates, Span<int> counts, ref int cursor)
        {
            if (block.Reference == 0) { return; }
            if (block.Motion == default) { counts[0] += weight; return; }
            Vp8MotionVector motion = biases[block.Reference] == biases[reference] ? block.Motion : block.Motion.Negate();
            if (candidates[cursor] != motion) { candidates[++cursor] = motion; }
            counts[cursor] += weight;
        }

        private static void ReadSplit(Vp8FrameHeader header, ref Vp8Macroblock current, in Vp8Macroblock left,
            in Vp8Macroblock above, Vp8MotionVector best)
        {
            int partition = header.Control.Tree([-3, 2, -2, 4, 0, -1], [110, 111, 150]);
            ReadOnlySpan<byte> map = Partitions.Slice(partition * 16, 16);
            int count = partition switch { 0 or 1 => 2, 2 => 4, 3 => 16, _ => throw new InvalidDataException("Invalid VP8 motion partition.") };
            for (int group = 0; group < count; group++)
            {
                int first = map.IndexOf((byte)group);
                Vp8MotionVector l = (first & 3) != 0 ? current.Blocks[first - 1] : IsSplit(left) != 0 ? left.Blocks[first + 3] : left.Motion;
                Vp8MotionVector a = first >= 4 ? current.Blocks[first - 4] : IsSplit(above) != 0 ? above.Blocks[first + 12] : above.Motion;
                int context = l == a ? l == default ? 4 : 3 : a == default ? 2 : l == default ? 1 : 0;
                int mode = header.Control.Tree([0, 2, -1, 4, -2, -3], SubContexts.Slice(context * 3, 3));
                Vp8MotionVector motion = mode switch
                {
                    0 => l, 1 => a, 2 => default, 3 => ReadVector(header.Control, header.MotionProbabilities).Add(best),
                    _ => throw new InvalidDataException("Invalid VP8 subblock motion mode.")
                };
                for (int i = first; i < 16; i++) { if (map[i] == group) { current.Blocks[i] = motion; } }
            }
        }

        private static Vp8MotionVector ReadVector(Vp8BooleanReader control, ReadOnlySpan<byte> probabilities)
        {
            int y = Component(control, probabilities[..19]), x = Component(control, probabilities[19..]);
            return new(x, y);
        }

        private static int Component(Vp8BooleanReader control, ReadOnlySpan<byte> probabilities)
        {
            int magnitude = 0;
            if (control.Read(probabilities[0]))
            {
                for (int bit = 0; bit < 3; bit++) { if (control.Read(probabilities[9 + bit])) { magnitude |= 1 << bit; } }
                for (int bit = 9; bit > 3; bit--) { if (control.Read(probabilities[9 + bit])) { magnitude |= 1 << bit; } }
                if ((magnitude & 0xfff0) == 0 || control.Read(probabilities[12])) { magnitude |= 8; }
            }
            else { magnitude = control.Tree([2, 8, 4, 6, 0, -1, -2, -3, 10, 12, -4, -5, -6, -7], probabilities[2..9]); }
            if (magnitude != 0 && control.Read(probabilities[1])) { magnitude = -magnitude; }
            return magnitude * 2;
        }
    }
}
