// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264EncodingBlocks
    {
        public static void Transform(VideoFrame input, int plane, int x, int y, ReadOnlySpan<byte> prediction, Span<int> transformed)
        {
            Span<int> residual = stackalloc int[16];
            for (int row = 0; row < 4; row++)
            {
                ReadOnlySpan<byte> source = input.GetRow(plane, Math.Min(y + row, input.Planes[plane].Height - 1));
                for (int column = 0; column < 4; column++)
                {
                    residual[row * 4 + column] = source[Math.Min(x + column, source.Length - 1)] - prediction[row * 4 + column];
                }
            }
            H264ForwardTransform.Transform4x4(residual, transformed);
        }
    }
}
