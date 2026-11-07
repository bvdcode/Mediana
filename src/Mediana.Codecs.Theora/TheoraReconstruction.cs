// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Theora
{
    internal static partial class TheoraReconstruction
    {
        private static ReadOnlySpan<byte> Zigzag => [0,1,8,16,9,2,3,10,17,24,32,25,18,11,4,5,12,19,26,33,40,48,
            41,34,27,20,13,6,7,14,21,28,35,42,49,56,57,50,43,36,29,22,15,23,30,37,44,51,58,59,52,45,38,31,39,46,53,60,61,54,47,55,62,63];

        internal static void Reconstruct(TheoraLayout layout, TheoraSetup setup, TheoraFrameState state,
            ReadOnlySpan<int> qualities, Span<byte> output, ReadOnlySpan<byte> previous, ReadOnlySpan<byte> golden, CancellationToken token)
        {
            Span<short> residual = stackalloc short[64];
            for (int plane = 0; plane < 3; plane++)
            {
                int width = layout.PlaneWidth(plane), height = layout.PlaneHeight(plane), offset = layout.PlaneOffset(plane);
                Span<byte> target = output.Slice(offset, width * height);
                for (int by = 0; by < height / 8; by++)
                {
                    token.ThrowIfCancellationRequested();
                    for (int bx = 0; bx < width / 8; bx++)
                    {
                        int block = layout.Block(plane, bx, by), mode = state.Modes[layout.Macro(block)];
                        bool coded = state.Coded[block] != 0;
                        int reference = coded ? TheoraDcPrediction.Reference(mode) : 1;
                        ReadOnlySpan<byte> source = reference switch
                        { 0 => default, 1 => previous, 2 => golden, _ => throw new InvalidDataException("Invalid Theora reference.") };
                        int dc = 0;
                        if (coded)
                        {
                            int type = mode == 1 ? 0 : 1;
                            ReadOnlySpan<ushort> dcQuantizer = setup.Quantizer(type, plane, qualities[0]);
                            ReadOnlySpan<short> coefficients = state.Coefficients.Slice(block * 64, 64);
                            if (state.CoefficientCounts[block] < 2) { dc = (short)((coefficients[0] * dcQuantizer[0] + 15) >> 5); }
                            else
                            {
                                ReadOnlySpan<ushort> ac = setup.Quantizer(type, plane, qualities[state.QualityIndices[block]]);
                                residual[0] = (short)(coefficients[0] * dcQuantizer[0]);
                                for (int i = 1; i < 64; i++) { int natural = Zigzag[i]; residual[natural] = (short)(coefficients[i] * ac[natural]); }
                                TheoraInverseDct.Transform(residual);
                            }
                        }
                        int mx = coded ? state.MotionX[block] : 0, my = coded ? state.MotionY[block] : 0;
                        Predict(layout, plane, bx * 8, by * 8, mx, my, reference, source, target);
                        if (coded)
                        { AddResidual(target, width, height, bx * 8, by * 8, residual, state.CoefficientCounts[block] < 2, dc); }

                    }
                }
            }
        }
    }
}
