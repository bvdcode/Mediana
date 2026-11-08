// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private void PredictIntra(int x, int y, int mode, int angleDelta)
        {
            if (mode == 0)
            {
                PredictDc(x, y);
                return;
            }
            Span<byte> top = stackalloc byte[9];
            Span<byte> prior = stackalloc byte[9];
            bool topRight = IsDecoded((x >> 2) + 1, (y >> 2) - 1);
            bool bottomLeft = IsDecoded((x >> 2) - 1, (y >> 2) + 1);
            int topLimit = Math.Min(sequence.Stride - 1, x + (topRight ? 8 : 4) - 1);
            int leftLimit = Math.Min(sequence.PaddedHeight - 1, y + (bottomLeft ? 8 : 4) - 1);
            for (int index = 0; index < 8; index++)
            {
                top[index + 1] = y > 0 ? output[(y - 1) * sequence.Stride + Math.Min(topLimit, x + index)] :
                    x > 0 ? output[y * sequence.Stride + x - 1] : (byte)127;
                prior[index + 1] = x > 0 ? output[Math.Min(leftLimit, y + index) * sequence.Stride + x - 1] :
                    y > 0 ? output[(y - 1) * sequence.Stride + x] : (byte)129;
            }
            byte corner;
            if (x > 0 && y > 0)
            {
                corner = output[(y - 1) * sequence.Stride + x - 1];
            }
            else if (y > 0)
            {
                corner = top[1];
            }
            else if (x > 0)
            {
                corner = prior[1];
            }
            else
            {
                corner = 128;
            }
            top[0] = prior[0] = corner;
            ReadOnlySpan<byte> angles = [0, 90, 180, 45, 135, 113, 157, 203, 67];
            int angle = angles[mode] + angleDelta * 3;
            int dx = 0;
            int dy = 0;
            if (angle < 90) { dx = Derivative(angle); }
            else if (angle is > 90 and < 180)
            {
                dx = Derivative(180 - angle);
                dy = Derivative(angle - 90);
            }
            else if (angle > 180) { dy = Derivative(270 - angle); }
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    byte value;
                    if (angle < 90)
                    {
                        int index = (row + 1) * dx;
                        int basis = (index >> 6) + column;
                        value = basis < 7 ? Interpolate(top, basis, (index >> 1) & 31) : top[8];
                    }
                    else if (angle is > 90 and < 180)
                    {
                        int index = (column << 6) - (row + 1) * dx;
                        int basis = index >> 6;
                        if (basis >= -1)
                        {
                            value = Interpolate(top, basis, (index >> 1) & 31);
                        }
                        else
                        {
                            index = (row << 6) - (column + 1) * dy;
                            value = Interpolate(prior, index >> 6, (index >> 1) & 31);
                        }
                    }
                    else if (angle > 180)
                    {
                        int index = (column + 1) * dy;
                        value = Interpolate(prior, (index >> 6) + row, (index >> 1) & 31);
                    }
                    else
                    {
                        value = angle switch
                        {
                            90 => top[column + 1],
                            180 => prior[row + 1],
                            _ => throw new InvalidDataException("The AV1 intra direction angle is invalid.")
                        };
                    }
                    output[(y + row) * sequence.Stride + x + column] = value;
                }
            }
        }

        private bool IsDecoded(int column, int row) => column >= 0 && row >= 0 && column < sequence.MiColumns &&
            row < sequence.MiRows && decoded[row * sequence.MiColumns + column] != 0;

        private static byte Interpolate(scoped ReadOnlySpan<byte> edge, int basis, int weight)
        {
            int value = edge[basis + 1] * (32 - weight) + edge[basis + 2] * weight;
            return (byte)((value + 16) >> 5);
        }

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
