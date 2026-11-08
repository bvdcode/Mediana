// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
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
            Span<byte> top = stackalloc byte[(width + height) * 2 + 2];
            Span<byte> prior = stackalloc byte[(width + height) * 2 + 2];
            bool topRight = IsDecoded(plane, (x >> 2) + (width >> 2), (y >> 2) - 1);
            bool bottomLeft = IsDecoded(plane, (x >> 2) - 1, (y >> 2) + (height >> 2));
            int topLimit = Math.Min(stride - 1, x + width * (topRight ? 2 : 1) - 1);
            int leftLimit = Math.Min(sequence.PlaneHeight(plane) - 1, y + height * (bottomLeft ? 2 : 1) - 1);
            for (int index = 0; index < width + height; index++)
            {
                top[index + 2] = y > 0 ? output[offset + (y - 1) * stride + Math.Min(topLimit, x + index)] :
                    x > 0 ? output[offset + y * stride + x - 1] : (byte)127;
                prior[index + 2] = x > 0 ? output[offset + Math.Min(leftLimit, y + index) * stride + x - 1] :
                    y > 0 ? output[offset + (y - 1) * stride + x] : (byte)129;
            }
            byte corner;
            if (x > 0 && y > 0)
            {
                corner = output[offset + (y - 1) * stride + x - 1];
            }
            else if (y > 0)
            {
                corner = top[2];
            }
            else if (x > 0)
            {
                corner = prior[2];
            }
            else
            {
                corner = 128;
            }
            top[1] = prior[1] = corner;
            if (prediction.Filter is int filter)
            {
                Av1FilterIntraPrediction.Predict(output[(offset + y * stride + x)..], stride, width, height,
                    Math.Min(width, stride - x), Math.Min(height, sequence.PlaneHeight(plane) - y), top[1..], prior[1..], filter);
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
            Av1IntraEdges edges = new(top, prior, Math.Min(width, stride - x), Math.Min(height, sequence.PlaneHeight(plane) - y),
                y > 0, x > 0, sequence.IntraEdgeFilter && SmoothNeighbors(plane, blockRow, blockColumn));
            Av1DirectionalPrediction.Predict(output[(offset + y * stride + x)..], stride, new(width, height), edges, angle, sequence.IntraEdgeFilter);
        }

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
    }
}
