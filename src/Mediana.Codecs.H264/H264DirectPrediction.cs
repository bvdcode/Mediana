// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264DirectPrediction
    {
        public static void Predict(H264Picture target, H264SliceHeader header, H264BidirectionalReferences references,
            H264PictureOrder order, int x, int y, Span<byte> prediction)
        {
            H264ReferencePicture colocated = references.L1.Count > 0 && references.L1[0] is H264ReferencePicture reference
                ? reference : throw new InvalidDataException("H.264 direct prediction has no co-located picture.");
            int mbX = x / 16 * 16;
            int mbY = y / 16 * 16;
            (int reference0, int reference1) = header.DirectSpatialMotionPrediction ?
                (SpatialReference(target, H264ReferenceList.L0, mbX, mbY), SpatialReference(target, H264ReferenceList.L1, mbX, mbY)) : (-1, 0);
            bool zero = reference0 < 0 && reference1 < 0;
            if (header.DirectSpatialMotionPrediction && zero)
            {
                reference0 = 0;
                reference1 = 0;
            }
            int size = header.Sequence.Direct8x8Inference ? 8 : 4;
            for (int row = 0; row < 8; row += size)
            {
                for (int column = 0; column < 8; column += size)
                {
                    int colX = header.Sequence.Direct8x8Inference ? mbX + (x % 16 == 0 ? 0 : 12) : x + column;
                    int colY = header.Sequence.Direct8x8Inference ? mbY + (y % 16 == 0 ? 0 : 12) : y + row;
                    (H264ReferenceList list, H264MotionSample motion) = Colocated(colocated.Picture, colX, colY);
                    H264PartitionMotion l0;
                    H264PartitionMotion l1;
                    if (header.DirectSpatialMotionPrediction)
                    {
                        bool colZero = colocated.LongTermIndex is null && motion.ReferenceIndex == 0 && Math.Abs(motion.X) <= 1 && Math.Abs(motion.Y) <= 1;
                        l0 = SpatialMotion(target, H264ReferenceList.L0, mbX, mbY, reference0, zero || colZero && reference0 == 0);
                        l1 = SpatialMotion(target, H264ReferenceList.L1, mbX, mbY, reference1, zero || colZero && reference1 == 0);
                    }
                    else
                    {
                        int mapped = motion.ReferenceIndex < 0 ? 0 : MapReference(colocated.Picture.ReferenceIdentity(list, motion.ReferenceIndex), references.L0);
                        H264ReferencePicture past = references.L0.Count > mapped && references.L0[mapped] is H264ReferencePicture value
                            ? value : throw new InvalidDataException("H.264 temporal direct prediction has no mapped reference.");
                        int dx = motion.X;
                        int dy = motion.Y;
                        int futureX = 0;
                        int futureY = 0;
                        int td = (int)Math.Clamp((long)colocated.Order.Value - past.Order.Value, -128, 127);
                        if (past.LongTermIndex is null && td != 0)
                        {
                            int tb = (int)Math.Clamp((long)order.Value - past.Order.Value, -128, 127);
                            int tx = (16384 + Math.Abs(td / 2)) / td;
                            int scale = Math.Clamp((tb * tx + 32) >> 6, -1024, 1023);
                            dx = (scale * motion.X + 128) >> 8;
                            dy = (scale * motion.Y + 128) >> 8;
                            futureX = dx - motion.X;
                            futureY = dy - motion.Y;
                        }
                        l0 = new H264PartitionMotion(mapped, dx, dy);
                        l1 = new H264PartitionMotion(0, futureX, futureY);
                    }
                    H264BidirectionalPrediction.PredictPartition(target, header, references, order, x + column, y + row, size, size, l0, l1, prediction);
                }
            }
        }

        private static int SpatialReference(H264Picture target, H264ReferenceList list, int x, int y)
        {
            H264MotionSample a = target.MotionAt(list, x - 1, y);
            H264MotionSample b = target.MotionAt(list, x, y - 1);
            H264MotionSample c = target.MotionAt(list, x + 16, y - 1);
            if (!c.Available)
            {
                c = target.MotionAt(list, x - 1, y - 1);
            }
            return Minimum(a.ReferenceIndex, Minimum(b.ReferenceIndex, c.ReferenceIndex));
        }

        private static int Minimum(int first, int second) => first >= 0 && second >= 0 ? Math.Min(first, second) : Math.Max(first, second);

        private static H264PartitionMotion SpatialMotion(H264Picture target, H264ReferenceList list, int x, int y, int reference, bool zero)
        {
            if (reference < 0)
            {
                return H264PartitionMotion.None;
            }
            (int dx, int dy) = zero ? (0, 0) : H264MotionPrediction.Predict(target, list, x, y, 16, 16, reference, 0);
            return new H264PartitionMotion(reference, dx, dy);
        }

        private static (H264ReferenceList List, H264MotionSample Motion) Colocated(H264Picture picture, int x, int y)
        {
            H264MotionSample l0 = picture.MotionAt(H264ReferenceList.L0, x, y);
            if (l0.ReferenceIndex >= 0 || !picture.HasBidirectionalMotion)
            {
                return (H264ReferenceList.L0, l0);
            }
            return (H264ReferenceList.L1, picture.MotionAt(H264ReferenceList.L1, x, y));
        }

        private static int MapReference(H264PictureIdentity identity, IReadOnlyList<H264ReferencePicture?> references)
        {
            for (int index = 0; index < references.Count; index++)
            {
                if (references[index] is H264ReferencePicture reference && ReferenceEquals(reference.Picture.Identity, identity))
                {
                    return index;
                }
            }
            throw new InvalidDataException("The H.264 co-located reference is absent from list 0.");
        }
    }
}
