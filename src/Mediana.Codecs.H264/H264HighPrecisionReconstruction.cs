// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264HighPrecisionReconstruction
    {
        public static void Write(ReadOnlySpan<ushort> prediction, int predictionStride, ReadOnlySpan<int> residual, int size,
            Span<ushort> output, int stride, int maximum)
        {
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    int index = row * size + column;
                    output[row * stride + column] = (ushort)Math.Clamp((long)prediction[row * predictionStride + column] + residual[index], 0, maximum);
                }
            }
        }
    }
}
