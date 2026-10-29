namespace Mediana.Codecs.H264
{
    internal static class H264DeblockingMotion
    {
        public static int Strength(H264Picture picture, int previous, int current,
            IReadOnlyList<H264ReferencePicture?>? referencesL0, IReadOnlyList<H264ReferencePicture?>? referencesL1)
        {
            (H264Picture? Picture, int X, int Y) p0 = Resolve(picture, H264ReferenceList.L0, previous, referencesL0);
            (H264Picture? Picture, int X, int Y) q0 = Resolve(picture, H264ReferenceList.L0, current, referencesL0);
            (H264Picture? Picture, int X, int Y) p1 = picture.HasBidirectionalMotion
                ? Resolve(picture, H264ReferenceList.L1, previous, referencesL1) : (null, 0, 0);
            (H264Picture? Picture, int X, int Y) q1 = picture.HasBidirectionalMotion
                ? Resolve(picture, H264ReferenceList.L1, current, referencesL1) : (null, 0, 0);
            if ((p0.Picture is null && p1.Picture is null) || (q0.Picture is null && q1.Picture is null))
            {
                throw new InvalidDataException("The H.264 inter edge has no prediction reference.");
            }
            bool sameOrder = Matches(p0, q0) && Matches(p1, q1);
            bool swappedOrder = Matches(p0, q1) && Matches(p1, q0);
            return sameOrder || swappedOrder ? 0 : 1;
        }

        private static (H264Picture? Picture, int X, int Y) Resolve(H264Picture picture, H264ReferenceList list, int index,
            IReadOnlyList<H264ReferencePicture?>? references)
        {
            int referenceIndex = picture.ReferenceIndices(list)[index];
            if (referenceIndex == byte.MaxValue)
            {
                return (null, 0, 0);
            }
            if (references is null || referenceIndex >= references.Count || references[referenceIndex] is not H264ReferencePicture reference)
            {
                throw new InvalidDataException("The H.264 edge uses an unavailable reference picture.");
            }
            return (reference.Picture, picture.MotionX(list)[index], picture.MotionY(list)[index]);
        }

        private static bool Matches((H264Picture? Picture, int X, int Y) first, (H264Picture? Picture, int X, int Y) second) =>
            ReferenceEquals(first.Picture, second.Picture) && Math.Abs((long)first.X - second.X) < 4 && Math.Abs((long)first.Y - second.Y) < 4;
    }
}
