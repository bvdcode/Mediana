// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal static class HevcBoundaryStrength
    {
        public static int Get(CodingBlockMap blocks, HevcMotionField? motion, int x, int y, bool vertical)
        {
            int px = vertical ? x - 1 : x, py = vertical ? y : y - 1;
            if (blocks.IsIntra(x, y) || blocks.IsIntra(px, py)) { return 2; }
            if (blocks.TransformBoundary(x, y, vertical) && (blocks.HasLumaResidual(x, y) || blocks.HasLumaResidual(px, py))) { return 1; }
            if (motion is null) { throw new InvalidOperationException("Inter deblocking requires the decoded motion field."); }
            return Compare(motion.Get(px, py), motion.Get(x, y));
        }

        internal static int Compare(HevcMotionSample first, HevcMotionSample second)
        {
            int firstCount = (first.Uses(0) ? 1 : 0) + (first.Uses(1) ? 1 : 0);
            int secondCount = (second.Uses(0) ? 1 : 0) + (second.Uses(1) ? 1 : 0);
            if (firstCount == 0 || secondCount == 0) { throw new InvalidOperationException("Inter deblocking requires complete prediction metadata."); }
            if (firstCount != secondCount) { return 1; }
            if (firstCount == 1)
            {
                (int order0, int _, HevcMotionVector vector0, bool _) = first.Direction(first.Uses(0) ? 0 : 1);
                (int order1, int _, HevcMotionVector vector1, bool _) = second.Direction(second.Uses(0) ? 0 : 1);
                return order0 != order1 || Different(vector0, vector1) ? 1 : 0;
            }
            bool direct = first.PictureOrder0 == second.PictureOrder0 && first.PictureOrder1 == second.PictureOrder1 &&
                !Different(first.Vector0, second.Vector0) && !Different(first.Vector1, second.Vector1);
            bool swapped = first.PictureOrder0 == second.PictureOrder1 && first.PictureOrder1 == second.PictureOrder0 &&
                !Different(first.Vector0, second.Vector1) && !Different(first.Vector1, second.Vector0);
            return direct || swapped ? 0 : 1;
        }

        private static bool Different(HevcMotionVector first, HevcMotionVector second) =>
            Math.Abs(first.X - second.X) >= 4 || Math.Abs(first.Y - second.Y) >= 4;
    }
}
