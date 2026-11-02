// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264DeblockingMotion
    {
        public static int Strength((H264PictureIdentity? Picture, int X, int Y) p0,
            (H264PictureIdentity? Picture, int X, int Y) q0, (H264PictureIdentity? Picture, int X, int Y) p1,
            (H264PictureIdentity? Picture, int X, int Y) q1)
        {
            if ((p0.Picture is null && p1.Picture is null) || (q0.Picture is null && q1.Picture is null))
            {
                throw new InvalidDataException("The H.264 inter edge has no prediction reference.");
            }
            bool sameOrder = Matches(p0, q0) && Matches(p1, q1);
            bool swappedOrder = Matches(p0, q1) && Matches(p1, q0);
            return sameOrder || swappedOrder ? 0 : 1;
        }

        private static bool Matches((H264PictureIdentity? Picture, int X, int Y) first, (H264PictureIdentity? Picture, int X, int Y) second) =>
            ReferenceEquals(first.Picture, second.Picture) && Math.Abs((long)first.X - second.X) < 4 && Math.Abs((long)first.Y - second.Y) < 4;
    }
}
