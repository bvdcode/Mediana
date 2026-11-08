// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private void PredictIntra(int plane, int x, int y, Av1IntraMode prediction, int width, int height)
        {
            int mode = prediction.Mode;
            if (mode == 0 && prediction.Filter is null)
            {
                PredictDc(plane, x, y, width, height);
                return;
            }
            int stride = sequence.PlaneStride(plane);
            int offset = sequence.PlaneOffset(plane);
            Span<byte> top = stackalloc byte[width + height + 1];
            Span<byte> prior = stackalloc byte[width + height + 1];
            bool topRight = IsDecoded(plane, (x >> 2) + (width >> 2), (y >> 2) - 1);
            bool bottomLeft = IsDecoded(plane, (x >> 2) - 1, (y >> 2) + (height >> 2));
            int topLimit = Math.Min(stride - 1, x + width * (topRight ? 2 : 1) - 1);
            int leftLimit = Math.Min(sequence.PlaneHeight(plane) - 1, y + height * (bottomLeft ? 2 : 1) - 1);
            for (int index = 0; index < width + height; index++)
            {
                top[index + 1] = y > 0 ? output[offset + (y - 1) * stride + Math.Min(topLimit, x + index)] :
                    x > 0 ? output[offset + y * stride + x - 1] : (byte)127;
                prior[index + 1] = x > 0 ? output[offset + Math.Min(leftLimit, y + index) * stride + x - 1] :
                    y > 0 ? output[offset + (y - 1) * stride + x] : (byte)129;
            }
            byte corner;
            if (x > 0 && y > 0)
            {
                corner = output[offset + (y - 1) * stride + x - 1];
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
            if (prediction.Filter is int filter)
            {
                Av1FilterIntraPrediction.Predict(output[(offset + y * stride + x)..], stride, width, height,
                    Math.Min(width, stride - x), Math.Min(height, sequence.PlaneHeight(plane) - y), top, prior, filter);
                return;
            }
            if (mode is >= 9 and <= 12)
            {
                Av1IntraPredictor.Predict(output[(offset + y * stride + x)..], stride, width, height,
                    Math.Min(width, stride - x), Math.Min(height, sequence.PlaneHeight(plane) - y), top, prior, mode);
                return;
            }
            ReadOnlySpan<byte> angles = [0, 90, 180, 45, 135, 113, 157, 203, 67];
            int angle = angles[mode] + prediction.AngleDelta * 3;
            int dx = 0;
            int dy = 0;
            if (angle < 90) { dx = Derivative(angle); }
            else if (angle is > 90 and < 180)
            {
                dx = Derivative(180 - angle);
                dy = Derivative(angle - 90);
            }
            else if (angle > 180) { dy = Derivative(270 - angle); }
            for (int row = 0; row < Math.Min(height, sequence.PlaneHeight(plane) - y); row++)
            {
                for (int column = 0; column < Math.Min(width, stride - x); column++)
                {
                    byte value;
                    if (angle < 90)
                    {
                        int index = (row + 1) * dx;
                        int basis = (index >> 6) + column;
                        value = basis < width + height - 1 ? Interpolate(top, basis, (index >> 1) & 31) : top[width + height];
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
                        int basis = (index >> 6) + row;
                        value = basis < width + height - 1 ? Interpolate(prior, basis, (index >> 1) & 31) : prior[width + height];
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
                    output[offset + (y + row) * stride + x + column] = value;
                }
            }
        }

        private bool IsDecoded(int plane, int column, int row) => column >= 0 && row >= 0 && column < sequence.PlaneColumns(plane) &&
            row < sequence.PlaneRows(plane) && decoded[sequence.PlaneOffset(plane) / 16 + row * sequence.PlaneColumns(plane) + column] != 0;

        private void PredictDc(int plane, int x, int y, int width, int height)
        {
            int stride = sequence.PlaneStride(plane);
            int offset = sequence.PlaneOffset(plane);
            int sum = 0;
            int count = 0;
            if (x > 0)
            {
                for (int index = 0; index < height; index++)
                {
                    sum += output[offset + Math.Min(y + index, sequence.PlaneHeight(plane) - 1) * stride + x - 1];
                }
                count += height;
            }
            if (y > 0)
            {
                for (int index = 0; index < width; index++)
                {
                    sum += output[offset + (y - 1) * stride + Math.Min(x + index, stride - 1)];
                }
                count += width;
            }
            byte predicted = count == 0 ? (byte)128 : (byte)((sum + count / 2) / count);
            for (int index = 0; index < Math.Min(height, sequence.PlaneHeight(plane) - y); index++)
            {
                output.Slice(offset + (y + index) * stride + x, Math.Min(width, stride - x)).Fill(predicted);
            }
        }

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
