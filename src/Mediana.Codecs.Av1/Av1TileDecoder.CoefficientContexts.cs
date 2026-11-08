// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder<T> where T : unmanaged, IBinaryInteger<T>
    {
        private int CoefficientSkipContext(int plane, int x4, int y4, int blockWidth, int blockHeight, Av1TransformSize size)
        {
            Span<Av1BlockContext<T>> top = CoefficientAbove(plane);
            Span<Av1BlockContext<T>> prior = CoefficientLeft(plane);
            int mask = prior.Length - 1;
            int topLevel = 0;
            int leftLevel = 0;
            for (int index = 0; index < size.Width4 && x4 + index < sequence.PlaneColumns(plane); index++)
            {
                ref Av1BlockContext<T> context = ref top[x4 + index];
                if (plane == 0) { topLevel = Math.Max(topLevel, context.Level); }
                else { topLevel |= context.Level | context.Dc; }
            }
            for (int index = 0; index < size.Height4 && y4 + index < sequence.PlaneRows(plane); index++)
            {
                ref Av1BlockContext<T> context = ref prior[(y4 + index) & mask];
                if (plane == 0) { leftLevel = Math.Max(leftLevel, context.Level); }
                else { leftLevel |= context.Level | context.Dc; }
            }
            if (plane != 0)
            {
                return 7 + (topLevel != 0 ? 1 : 0) + (leftLevel != 0 ? 1 : 0) +
                    (blockWidth * blockHeight > size.Width4 * size.Height4 ? 3 : 0);
            }
            if (blockWidth == size.Width4 && blockHeight == size.Height4) { return 0; }
            if (topLevel == 0 && leftLevel == 0) { return 1; }
            if (topLevel == 0 || leftLevel == 0) { return 2 + (Math.Max(topLevel, leftLevel) > 3 ? 1 : 0); }
            if (Math.Max(topLevel, leftLevel) <= 3) { return 4; }
            return Math.Min(topLevel, leftLevel) <= 3 ? 5 : 6;
        }

        private int CoefficientDcSign(int plane, int x4, int y4, Av1TransformSize size)
        {
            int sum = 0;
            Span<Av1BlockContext<T>> top = CoefficientAbove(plane);
            Span<Av1BlockContext<T>> prior = CoefficientLeft(plane);
            int mask = prior.Length - 1;
            for (int index = 0; index < size.Width4 && x4 + index < sequence.PlaneColumns(plane); index++)
            {
                sum += DcSign(top[x4 + index].Dc);
            }
            for (int index = 0; index < size.Height4 && y4 + index < sequence.PlaneRows(plane); index++)
            {
                sum += DcSign(prior[(y4 + index) & mask].Dc);
            }
            return sum;
        }

        private void StoreCoefficientContext(int plane, int x4, int y4, Av1TransformSize size, byte level, byte dc)
        {
            Span<Av1BlockContext<T>> top = CoefficientAbove(plane);
            Span<Av1BlockContext<T>> prior = CoefficientLeft(plane);
            int mask = prior.Length - 1;
            for (int index = 0; index < size.Width4 && x4 + index < sequence.PlaneColumns(plane); index++)
            {
                top[x4 + index].Level = level;
                top[x4 + index].Dc = dc;
            }
            for (int index = 0; index < size.Height4 && y4 + index < sequence.PlaneRows(plane); index++)
            {
                prior[(y4 + index) & mask].Level = level;
                prior[(y4 + index) & mask].Dc = dc;
            }
        }

        private static int DcSign(int value) => value switch
        {
            0 => 0,
            1 => -1,
            2 => 1,
            _ => throw new InvalidDataException("The AV1 DC sign context is invalid.")
        };
    }
}
