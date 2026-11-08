// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static partial class Av1DirectionalPrediction
    {
        internal static void Predict(Span<byte> destination, int stride, Av1TransformSize size, Av1IntraEdges edges, int angle, bool filter)
        {
            PrepareEdges(size, edges, angle, filter, out int upsampleAbove, out int upsampleLeft);
            ReadOnlySpan<byte> above = edges.Above;
            ReadOnlySpan<byte> left = edges.Left;
            int dx = 0;
            int dy = 0;
            if (angle < 90) { dx = Derivative(angle); }
            else if (angle is > 90 and < 180) { dx = Derivative(180 - angle); dy = Derivative(angle - 90); }
            else if (angle > 180) { dy = Derivative(270 - angle); }
            int maximumX = (size.Width + size.Height - 1) << upsampleAbove;
            int maximumY = (size.Width + size.Height - 1) << upsampleLeft;
            for (int y = 0; y < edges.Height; y++)
            {
                Span<byte> row = destination.Slice(y * stride, edges.Width);
                for (int x = 0; x < row.Length; x++)
                {
                    byte value;
                    if (angle < 90)
                    {
                        int index = (y + 1) * dx;
                        int basis = (index >> (6 - upsampleAbove)) + (x << upsampleAbove);
                        value = basis < maximumX ? Interpolate(above, basis, ((index << upsampleAbove) >> 1) & 31) : above[maximumX + 2];
                    }
                    else if (angle is > 90 and < 180)
                    {
                        int index = (x << 6) - (y + 1) * dx;
                        int basis = index >> (6 - upsampleAbove);
                        if (basis >= -(1 << upsampleAbove))
                        {
                            value = Interpolate(above, basis, ((index << upsampleAbove) >> 1) & 31);
                        }
                        else
                        {
                            index = (y << 6) - (x + 1) * dy;
                            value = Interpolate(left, index >> (6 - upsampleLeft), ((index << upsampleLeft) >> 1) & 31);
                        }
                    }
                    else if (angle > 180)
                    {
                        int index = (x + 1) * dy;
                        int basis = (index >> (6 - upsampleLeft)) + (y << upsampleLeft);
                        value = basis < maximumY ? Interpolate(left, basis, ((index << upsampleLeft) >> 1) & 31) : left[maximumY + 2];
                    }
                    else
                    {
                        value = angle switch
                        {
                            90 => above[x + 2],
                            180 => left[y + 2],
                            _ => throw new InvalidDataException("The AV1 intra direction angle is invalid.")
                        };
                    }
                    row[x] = value;
                }
            }
        }

        private static byte Interpolate(ReadOnlySpan<byte> edge, int basis, int weight) =>
            (byte)((edge[basis + 2] * (32 - weight) + edge[basis + 3] * weight + 16) >> 5);

        private static int Derivative(int angle)
        {
            ReadOnlySpan<ushort> values =
            [
                0, 0, 0, 1023, 0, 0, 547, 0, 0, 372, 0, 0, 0, 0, 273, 0, 0, 215,
                0, 0, 178, 0, 0, 151, 0, 0, 132, 0, 0, 116, 0, 0, 102, 0, 0, 0,
                90, 0, 0, 80, 0, 0, 71, 0, 0, 64, 0, 0, 57, 0, 0, 51, 0, 0,
                45, 0, 0, 0, 40, 0, 0, 35, 0, 0, 31, 0, 0, 27, 0, 0, 23, 0,
                0, 19, 0, 0, 15, 0, 0, 0, 0, 11, 0, 0, 7, 0, 0, 3, 0, 0,
            ];
            if ((uint)angle >= values.Length || values[angle] == 0)
            {
                throw new InvalidDataException("The AV1 directional derivative is undefined.");
            }
            return values[angle];
        }
    }
}
