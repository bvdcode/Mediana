// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder<T> where T : unmanaged, IBinaryInteger<T>
    {
        private const int IntraCopyDelayPixels = 256;

        private void IntraBlockCopy(int row, int column, int width, int height, int skip)
        {
            Av1ReferencePictures pictures = references ?? throw new InvalidOperationException("AV1 intra block copy requires motion candidates.");
            Span<Av1MotionCandidate> storage = stackalloc Av1MotionCandidate[Av1MotionCandidates.Capacity];
            Av1MotionCandidates candidates = new(storage, motionBlocks, sequence, header, bounds, pictures, row, column, width, height, 0);
            candidates.Build();
            Av1MotionCandidate prediction = candidates[0];
            if (prediction.Row == 0 && prediction.Column == 0) { prediction = candidates[1]; }
            if (prediction.Row == 0 && prediction.Column == 0)
            {
                int side4 = sequence.Superblock128 ? 32 : 16;
                prediction = row - side4 < bounds.RowStart
                    ? new(0, -(side4 * 4 + IntraCopyDelayPixels) * 8, 0)
                    : new(-side4 * 4 * 8, 0, 0);
            }
            int motionY = prediction.Row;
            int motionX = prediction.Column;
            int joint = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.IntrabcMvJointOffset, 5));
            if (joint is 2 or 3) { motionY += ReadMotionComponent(0, true); }
            if (joint is 1 or 3) { motionX += ReadMotionComponent(1, true); }
            bool hasChroma = !sequence.Monochrome && !(height == 1 && (row & 1) == 0) && !(width == 1 && (column & 1) == 0);
            if (!Av1IntraBlockVectorValidator.IsValid(sequence, bounds, row, column, width, height, motionY, motionX, hasChroma))
            {
                throw new InvalidDataException("The AV1 intra block vector references unavailable reconstructed samples.");
            }
            Av1MotionBlock block = new()
            {
                Intrabc = true,
                Mode = 0,
                Width = (byte)width,
                Height = (byte)height,
                Row = motionY,
                Column = motionX,
                FilterY = 3,
                FilterX = 3,
                CompoundIndex = 1
            };
            StoreMotion(row, column, width, height, block);
            for (int plane = 0; plane < (hasChroma ? 3 : 1); plane++)
            {
                int sub = sequence.PlaneShift(plane);
                int x = (column >> sub) * 4;
                int y = (row >> sub) * 4;
                int stride = sequence.PlaneStride(plane);
                int rows = sequence.PlaneHeight(plane);
                int planeWidth = Math.Min(Math.Max(4, width * 4 >> sub), stride - x);
                int planeHeight = Math.Min(Math.Max(4, height * 4 >> sub), rows - y);
                Av1IntraBlockCopy.Apply(output.Slice(sequence.PlaneOffset(plane), stride * rows), stride,
                    x, y, planeWidth, planeHeight, motionX >> 3, motionY >> 3, sub, sub);
            }
            Av1TransformSize transform = ReadInterTransformSizes(row, column, width, height, skip != 0);
            Residual(row, column, width, height, skip != 0, default, hasChroma, default, default, transform, true);
            Av1BlockContext<T> context = new()
            {
                WidthLog = (byte)BitOperations.Log2((uint)width),
                HeightLog = (byte)BitOperations.Log2((uint)height),
                Skip = (byte)skip
            };
            StoreContext(row, column, width, height, context);
        }
    }
}
