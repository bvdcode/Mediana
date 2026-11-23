// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264MotionPrediction
    {
        public static (int X, int Y) Predict(H264Picture picture, H264ReferenceList list,
            int x, int y, int width, int height, int reference, int partition)
        {
            H264MotionSample a = picture.NeighbourMotionAt(list, x, y, x - 1, y);
            H264MotionSample b = picture.NeighbourMotionAt(list, x, y, x, y - 1);
            H264MotionSample c = TopRight(picture, list, x, y, width);
            return Predict(a, b, c, width, height, reference, partition);
        }

        public static H264PartitionMotion SpatialDirect(H264Picture picture, H264ReferenceList list, int x, int y)
        {
            H264MotionSample a = picture.NeighbourMotionAt(list, x, y, x - 1, y);
            H264MotionSample b = picture.NeighbourMotionAt(list, x, y, x, y - 1);
            H264MotionSample c = TopRight(picture, list, x, y, 16);
            int reference = MinimumReference(a.ReferenceIndex, MinimumReference(b.ReferenceIndex, c.ReferenceIndex));
            if (reference < 0)
            {
                return H264PartitionMotion.None;
            }
            (int dx, int dy) = Predict(a, b, c, 16, 16, reference, 0);
            return new H264PartitionMotion(reference, dx, dy);
        }

        private static int MinimumReference(int first, int second) =>
            first >= 0 && second >= 0 ? Math.Min(first, second) : Math.Max(first, second);

        private static H264MotionSample TopRight(H264Picture picture, H264ReferenceList list, int x, int y, int width)
        {
            H264MotionSample sample = picture.NeighbourMotionAt(list, x, y, x + width, y - 1);
            return sample.Available ? sample : picture.NeighbourMotionAt(list, x, y, x - 1, y - 1);
        }

        private static (int X, int Y) Predict(H264MotionSample a, H264MotionSample b, H264MotionSample c,
            int width, int height, int reference, int partition)
        {
            if (width == 16 && height == 8)
            {
                if (partition == 0 && b.ReferenceIndex == reference)
                {
                    return (b.X, b.Y);
                }
                if (partition == 1 && a.ReferenceIndex == reference)
                {
                    return (a.X, a.Y);
                }
            }
            if (width == 8 && height == 16)
            {
                if (partition == 0 && a.ReferenceIndex == reference)
                {
                    return (a.X, a.Y);
                }
                if (partition == 1 && c.ReferenceIndex == reference)
                {
                    return (c.X, c.Y);
                }
            }
            if (!b.Available && !c.Available && a.Available)
            {
                b = a;
                c = a;
            }
            int matches = (a.ReferenceIndex == reference ? 1 : 0) + (b.ReferenceIndex == reference ? 1 : 0) + (c.ReferenceIndex == reference ? 1 : 0);
            if (matches == 1)
            {
                H264MotionSample selected = a.ReferenceIndex == reference ? a : b.ReferenceIndex == reference ? b : c;
                return (selected.X, selected.Y);
            }
            return (Median(a.X, b.X, c.X), Median(a.Y, b.Y, c.Y));
        }

        public static (int X, int Y) Skip(H264Picture picture, int x, int y)
        {
            H264MotionSample a = picture.NeighbourMotionAt(H264ReferenceList.L0, x, y, x - 1, y);
            H264MotionSample b = picture.NeighbourMotionAt(H264ReferenceList.L0, x, y, x, y - 1);
            if (!a.Available || !b.Available || (a.ReferenceIndex == 0 && a.X == 0 && a.Y == 0) ||
                (b.ReferenceIndex == 0 && b.X == 0 && b.Y == 0))
            {
                return (0, 0);
            }
            return Predict(picture, H264ReferenceList.L0, x, y, 16, 16, 0, 0);
        }

        private static int Median(int a, int b, int c) => Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));
    }
}
