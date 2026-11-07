// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Theora
{
    internal static class TheoraDcPrediction
    {
        internal static int Reference(int mode) => mode switch
        { 1 => 0, 0 or 2 or 3 or 4 or 7 => 1, 5 or 6 => 2, _ => throw new InvalidDataException("Invalid Theora macroblock mode.") };

        internal static void Restore(TheoraLayout layout, TheoraFrameState state, CancellationToken token)
        {
            Span<int> last = stackalloc int[3]; Span<int> neighbors = stackalloc int[4];
            for (int plane = 0; plane < 3; plane++)
            {
                last.Clear(); int columns = layout.PlaneWidth(plane) / 8, rows = layout.PlaneHeight(plane) / 8;
                for (int y = 0; y < rows; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (int x = 0; x < columns; x++)
                    {
                        int block = layout.Block(plane, x, y);
                        if (state.Coded[block] == 0) { continue; }
                        int reference = Reference(state.Modes[layout.Macro(block)]), mask = 0; neighbors.Clear();
                        if (x > 0) { ReadNeighbor(layout, state, plane, x - 1, y, reference, 0, neighbors, ref mask); }
                        if (y > 0)
                        {
                            if (x > 0) { ReadNeighbor(layout, state, plane, x - 1, y - 1, reference, 1, neighbors, ref mask); }
                            ReadNeighbor(layout, state, plane, x, y - 1, reference, 2, neighbors, ref mask);
                            if (x + 1 < columns) { ReadNeighbor(layout, state, plane, x + 1, y - 1, reference, 3, neighbors, ref mask); }
                        }
                        int l = neighbors[0], dl = neighbors[1], d = neighbors[2], dr = neighbors[3];
                        int prediction = mask switch
                        {
                            0 => last[reference],
                            1 or 3 => l,
                            2 => dl,
                            4 or 6 or 12 => d,
                            5 => (l + d) / 2,
                            7 or 15 => (29 * l - 26 * dl + 29 * d) / 32,
                            8 => dr,
                            9 or 11 or 13 => (75 * l + 53 * dr) / 128,
                            10 => (dl + dr) / 2,
                            14 => (3 * dl + 10 * d + 3 * dr) / 16,
                            _ => throw new InvalidDataException("Invalid Theora DC predictor mask.")
                        };
                        if ((mask & 7) == 7)
                        {
                            if (Math.Abs(prediction - d) > 128) { prediction = d; }
                            else if (Math.Abs(prediction - l) > 128) { prediction = l; }
                            else if (Math.Abs(prediction - dl) > 128) { prediction = dl; }
                        }
                        short dc = (short)(state.Coefficients[block * 64] + prediction);
                        state.Coefficients[block * 64] = dc; last[reference] = dc;
                    }
                }
            }
        }

        private static void ReadNeighbor(TheoraLayout layout, TheoraFrameState state, int plane, int x, int y,
            int reference, int index, Span<int> neighbors, ref int mask)
        {
            int block = layout.Block(plane, x, y);
            if (state.Coded[block] != 0 && Reference(state.Modes[layout.Macro(block)]) == reference)
            { mask |= 1 << index; neighbors[index] = state.Coefficients[block * 64]; }
        }
    }
}
