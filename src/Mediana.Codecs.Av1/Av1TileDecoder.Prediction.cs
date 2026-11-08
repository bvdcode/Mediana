// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder<T> where T : unmanaged, IBinaryInteger<T>
    {
        private void PredictIntra(int plane, int x, int y, Av1IntraMode prediction, int width, int height, int blockRow, int blockColumn)
        {
            int mode = prediction.Mode;
            if (mode == 0 && prediction.Filter is null)
            {
                PredictDc(plane, x, y, width, height);
                return;
            }
            int stride = sequence.PlaneStride(plane);
            int offset = sequence.PlaneOffset(plane);
            int sub = sequence.PlaneShift(plane);
            bool hasTop = y > (bounds.RowStart * 4 >> sub);
            bool hasLeft = x > (bounds.ColumnStart * 4 >> sub);
            int maximumX = (bounds.ColumnEnd * 4 >> sub) - 1;
            int maximumY = (bounds.RowEnd * 4 >> sub) - 1;
            Span<T> top = stackalloc T[(width + height) * 2 + 2];
            Span<T> prior = stackalloc T[(width + height) * 2 + 2];
            bool topRight = IsDecoded(plane, (x >> 2) + (width >> 2), (y >> 2) - 1);
            bool bottomLeft = IsDecoded(plane, (x >> 2) - 1, (y >> 2) + (height >> 2));
            int topLimit = Math.Min(maximumX, x + width * (topRight ? 2 : 1) - 1);
            int leftLimit = Math.Min(maximumY, y + height * (bottomLeft ? 2 : 1) - 1);
            for (int index = 0; index < width + height; index++)
            {
                top[index + 2] = hasTop ? output[offset + (y - 1) * stride + Math.Min(topLimit, x + index)] :
                    hasLeft ? output[offset + y * stride + x - 1] : T.CreateTruncating((1 << (SampleBitDepth - 1)) - 1);
                prior[index + 2] = hasLeft ? output[offset + Math.Min(leftLimit, y + index) * stride + x - 1] :
                    hasTop ? output[offset + (y - 1) * stride + x] : T.CreateTruncating((1 << (SampleBitDepth - 1)) + 1);
            }
            T corner;
            if (hasLeft && hasTop)
            {
                corner = output[offset + (y - 1) * stride + x - 1];
            }
            else if (hasTop)
            {
                corner = top[2];
            }
            else if (hasLeft)
            {
                corner = prior[2];
            }
            else
            {
                corner = T.CreateTruncating(1 << (SampleBitDepth - 1));
            }
            top[1] = prior[1] = corner;
            if (prediction.Filter is int filter)
            {
                Av1FilterIntraPrediction.Predict(output[(offset + y * stride + x)..], stride, width, height,
                    Math.Min(width, stride - x), Math.Min(height, sequence.PlaneHeight(plane) - y), top[1..], prior[1..], filter, SampleBitDepth);
                return;
            }
            if (mode is >= 9 and <= 12)
            {
                Av1IntraPredictor.Predict(output[(offset + y * stride + x)..], stride, width, height,
                    Math.Min(width, stride - x), Math.Min(height, sequence.PlaneHeight(plane) - y), top[1..], prior[1..], mode);
                return;
            }
            ReadOnlySpan<byte> angles = [0, 90, 180, 45, 135, 113, 157, 203, 67];
            int angle = angles[mode] + prediction.AngleDelta * 3;
            Av1IntraEdges<T> edges = new(top, prior, Math.Min(width, stride - x), Math.Min(height, sequence.PlaneHeight(plane) - y),
                hasTop, hasLeft, sequence.IntraEdgeFilter && SmoothNeighbors(plane, blockRow, blockColumn));
            Av1DirectionalPrediction.Predict(output[(offset + y * stride + x)..], stride, new(width, height), edges, angle, sequence.IntraEdgeFilter, SampleBitDepth);
        }

        private void PredictDc(int plane, int x, int y, int width, int height)
        {
            int stride = sequence.PlaneStride(plane);
            int offset = sequence.PlaneOffset(plane);
            int sub = sequence.PlaneShift(plane);
            bool hasTop = y > (bounds.RowStart * 4 >> sub);
            bool hasLeft = x > (bounds.ColumnStart * 4 >> sub);
            int maximumX = (bounds.ColumnEnd * 4 >> sub) - 1;
            int maximumY = (bounds.RowEnd * 4 >> sub) - 1;
            int sum = 0;
            int count = 0;
            if (hasLeft)
            {
                for (int index = 0; index < height; index++)
                {
                    sum += int.CreateTruncating(output[offset + Math.Min(y + index, maximumY) * stride + x - 1]);
                }
                count += height;
            }
            if (hasTop)
            {
                for (int index = 0; index < width; index++)
                {
                    sum += int.CreateTruncating(output[offset + (y - 1) * stride + Math.Min(x + index, maximumX)]);
                }
                count += width;
            }
            T predicted = T.CreateTruncating(count == 0 ? 1 << (SampleBitDepth - 1) : (sum + count / 2) / count);
            for (int index = 0; index < Math.Min(height, sequence.PlaneHeight(plane) - y); index++)
            {
                output.Slice(offset + (y + index) * stride + x, Math.Min(width, stride - x)).Fill(predicted);
            }
        }
    }
}
