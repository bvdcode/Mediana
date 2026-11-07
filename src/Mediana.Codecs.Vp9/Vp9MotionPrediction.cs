// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal readonly ref struct Vp9MotionPrediction(ReadOnlySpan<Vp9BlockInfo> current, ReadOnlySpan<Vp9BlockInfo> previous,
        Vp9FrameHeader header, int stride, int row, int column, int size, int tileLeft, int tileRight, bool usePrevious)
    {
        private readonly ReadOnlySpan<Vp9BlockInfo> blocks = current, prior = previous;
        internal (Vp9MotionVector Nearest, Vp9MotionVector Near, int Context) Find(int reference, int sub = -1)
        {
            Span<Vp9MotionVector> candidates = stackalloc Vp9MotionVector[2]; candidates.Clear(); int count = 0, counter = 0; bool found = false;
            ReadOnlySpan<sbyte> search = Neighbors.Slice(size * 16, 16);
            for (int i = 0; i < 8; i++)
            {
                int r = row + search[i * 2], c = column + search[i * 2 + 1];
                if (!Inside(r, c)) { continue; }
                found = true; Vp9BlockInfo block = blocks[r * stride + c];
                if (i < 2) { counter += block.Mode < 10 ? 9 : block.Mode == 12 ? 3 : block.Mode == 13 ? 1 : 0; }
                for (int list = 0; list < 2; list++)
                {
                    if (block.Reference(list) != reference) { continue; }
                    int index = i < 2 && sub >= 0 ? SubIndex[sub * 2 + (search[i * 2 + 1] == 0 ? 1 : 0)] : 3;
                    Add(candidates, ref count, block.Vector(list, index)); break;
                }
            }
            if (usePrevious) { SameReference(candidates, ref count, prior[row * stride + column], reference); }
            if (found)
            {
                for (int i = 0; i < 8 && count < 2; i++)
                {
                    int r = row + search[i * 2], c = column + search[i * 2 + 1];
                    if (Inside(r, c)) { DifferentReference(candidates, ref count, blocks[r * stride + c], reference); }
                }
            }
            if (usePrevious && count < 2) { DifferentReference(candidates, ref count, prior[row * stride + column], reference); }
            Vp9MotionVector nearest = Clamp(candidates[0], 128), near = Clamp(candidates[1], 128);
            if (sub < 0)
            {
                nearest = Best(nearest); near = Best(near);
            }
            return (nearest, near, Contexts[counter]);
        }

        private Vp9MotionVector Best(Vp9MotionVector value)
        {
            if (!header.HighPrecisionMotion || !value.HighPrecision) { value = new(value.X / 2 * 2, value.Y / 2 * 2); }
            return Clamp(value, (160 - 4) * 8);
        }

        private Vp9MotionVector Clamp(Vp9MotionVector value, int border)
        {
            int width = Math.Max(1, Widths[size] / 8), height = Math.Max(1, Heights[size] / 8);
            return new(Math.Clamp(value.X, -column * 64 - border, (header.MiColumns - width - column) * 64 + border),
                Math.Clamp(value.Y, -row * 64 - border, (header.MiRows - height - row) * 64 + border));
        }

        private bool Inside(int r, int c) => r >= 0 && r < header.MiRows && c >= tileLeft && c < tileRight;
        private static void Add(Span<Vp9MotionVector> values, ref int count, Vp9MotionVector value)
        { if (count < 2 && (count == 0 || values[0] != value)) { values[count++] = value; } }
        private static void SameReference(Span<Vp9MotionVector> values, ref int count, Vp9BlockInfo block, int reference)
        { for (int list = 0; list < 2; list++) { if (block.Reference(list) == reference) { Add(values, ref count, block.Vector(list, 3)); return; } } }
        private void DifferentReference(Span<Vp9MotionVector> values, ref int count, Vp9BlockInfo block, int reference)
        {
            bool same = block.Vector(0, 3) == block.Vector(1, 3);
            for (int list = 0; list < 2; list++)
            {
                int candidate = block.Reference(list);
                if (candidate == 0 || candidate == reference || (list == 1 && same)) { continue; }
                Vp9MotionVector value = block.Vector(list, 3);
                if (header.SignBias[candidate] != header.SignBias[reference]) { value = value.Negate(); }
                Add(values, ref count, value);
            }
        }

        private static ReadOnlySpan<byte> Widths => [4, 4, 8, 8, 8, 16, 16, 16, 32, 32, 32, 64, 64];
        private static ReadOnlySpan<byte> Heights => [4, 8, 4, 8, 16, 8, 16, 32, 16, 32, 64, 32, 64];
        private static ReadOnlySpan<byte> SubIndex => [1, 2, 1, 3, 3, 2, 3, 3];
        private static ReadOnlySpan<byte> Contexts => [2, 3, 4, 1, 3, 9, 0, 9, 9, 5, 5, 9, 5, 9, 9, 9, 9, 9, 6];
        private static ReadOnlySpan<sbyte> Neighbors =>
        [
            -1, 0, 0, -1, -1, -1, -2, 0, 0, -2, -2, -1, -1, -2, -2, -2,
            -1, 0, 0, -1, -1, -1, -2, 0, 0, -2, -2, -1, -1, -2, -2, -2,
            -1, 0, 0, -1, -1, -1, -2, 0, 0, -2, -2, -1, -1, -2, -2, -2,
            -1, 0, 0, -1, -1, -1, -2, 0, 0, -2, -2, -1, -1, -2, -2, -2,
            0, -1, -1, 0, 1, -1, -1, -1, 0, -2, -2, 0, -2, -1, -1, -2,
            -1, 0, 0, -1, -1, 1, -1, -1, -2, 0, 0, -2, -1, -2, -2, -1,
            -1, 0, 0, -1, -1, 1, 1, -1, -1, -1, -3, 0, 0, -3, -3, -3,
            0, -1, -1, 0, 2, -1, -1, -1, -1, 1, 0, -3, -3, 0, -3, -3,
            -1, 0, 0, -1, -1, 2, -1, -1, 1, -1, -3, 0, 0, -3, -3, -3,
            -1, 1, 1, -1, -1, 2, 2, -1, -1, -1, -3, 0, 0, -3, -3, -3,
            0, -1, -1, 0, 4, -1, -1, 2, -1, -1, 0, -3, -3, 0, 2, -1,
            -1, 0, 0, -1, -1, 4, 2, -1, -1, -1, -3, 0, 0, -3, -1, 2,
            -1, 3, 3, -1, -1, 4, 4, -1, -1, -1, -1, 0, 0, -1, -1, 6
        ];
    }
}
