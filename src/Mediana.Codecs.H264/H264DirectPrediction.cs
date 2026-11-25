// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264DirectPrediction
    {
        public static void Predict(H264Picture target, H264SliceHeader header, H264BidirectionalReferences references,
            H264PictureOrder order, int x, int y, Span<byte> prediction)
            => PredictRegion(target, header, references, order, x, y, 8, prediction);

        public static void PredictMacroblock(H264Picture target, H264SliceHeader header, H264BidirectionalReferences references,
            H264PictureOrder order, int x, int y, Span<byte> prediction)
            => PredictRegion(target, header, references, order, x, y, 16, prediction);

        public static void ReconstructSkip(H264Picture target, H264SliceHeader header, H264BidirectionalReferences references,
            H264PictureOrder order, int x, int y, Span<byte> prediction)
            => PredictRegion(target, header, references, order, x, y, 16, prediction, reconstruct: true);

        private static void PredictRegion(H264Picture target, H264SliceHeader header, H264BidirectionalReferences references,
            H264PictureOrder order, int x, int y, int regionSize, Span<byte> prediction, bool reconstruct = false)
        {
            Span<H264DirectMotion> motions = stackalloc H264DirectMotion[16];
            int count = ReadMotion(target, header, references, order, x, y, regionSize, motions);
            if (reconstruct && count == 1 && H264StationaryBiPrediction.TryWrite(target, header, references, order, x, y, motions[0]))
            {
                return;
            }
            int size = count == 1 ? regionSize : header.Sequence.Direct8x8Inference ? 8 : 4;
            int columns = regionSize / size;
            for (int index = 0; index < count; index++)
            {
                H264BidirectionalPrediction.PredictPartition(target, header, references, order, x + index % columns * size,
                    y + index / columns * size, size, size, motions[index].L0, motions[index].L1, prediction);
            }
            if (reconstruct)
            {
                target.WriteInterPrediction(x, y, prediction);
            }
        }

        private static int ReadMotion(H264Picture target, H264SliceHeader header, H264BidirectionalReferences references,
            H264PictureOrder order, int x, int y, int regionSize, Span<H264DirectMotion> motions)
        {
            H264ReferencePicture colocated = references.L1.Count > 0 && references.L1[0] is H264ReferencePicture reference
                ? reference : throw new InvalidDataException("H.264 direct prediction has no co-located picture.");
            H264MacroblockPredictionContext context = H264MacroblockPredictionContext.Create(target, x, y);
            H264ColocatedPicture source = new(colocated, target, context, order);
            H264PictureOrder currentOrder = context.SelectOrder(order);
            int mbX = x / 16 * 16;
            int mbY = y / 16 * 16;
            H264DirectMotion spatial = default;
            H264DirectMotion colocatedZero = default;
            if (header.DirectSpatialMotionPrediction)
            {
                spatial = H264MotionPrediction.SpatialDirect(target, mbX, mbY);
                colocatedZero = new(ApplyColocatedZero(spatial.L0), ApplyColocatedZero(spatial.L1));
                if ((spatial == colocatedZero || colocated.LongTermIndex is not null) && source.SharesFrameGrid)
                {
                    motions[0] = spatial;
                    return 1;
                }
            }
            int size = header.Sequence.Direct8x8Inference ? 8 : 4;
            int count = 0;
            bool uniform = true;
            for (int row = 0; row < regionSize; row += size)
            {
                for (int column = 0; column < regionSize; column += size)
                {
                    int colX = header.Sequence.Direct8x8Inference ? mbX + ((x + column) % 16 == 0 ? 0 : 12) : x + column;
                    int colY = header.Sequence.Direct8x8Inference ? mbY + ((y + row) % 16 == 0 ? 0 : 12) : y + row;
                    H264ColocatedBlock block = source.MapBlock(colX, colY);
                    (H264ReferenceList list, H264MotionSample motion) = Colocated(block.Picture, block.X, block.Y);
                    H264PartitionMotion l0;
                    H264PartitionMotion l1;
                    if (header.DirectSpatialMotionPrediction)
                    {
                        bool colZero = colocated.LongTermIndex is null && motion.ReferenceIndex == 0 && Math.Abs(motion.X) <= 1 && Math.Abs(motion.Y) <= 1;
                        (l0, l1) = colZero ? colocatedZero : spatial;
                    }
                    else
                    {
                        int mapped = motion.ReferenceIndex < 0 ? 0 : MapReference(block.MapIdentity(
                            block.Picture.ReferenceIdentity(list, motion.ReferenceIndex, block.Address)), references.L0);
                        H264ReferencePicture past = references.L0.Count > mapped && references.L0[mapped] is H264ReferencePicture value
                            ? value : throw new InvalidDataException("H.264 temporal direct prediction has no mapped reference.");
                        int dx = motion.X;
                        int motionY = block.MapVerticalMotion(motion.Y);
                        int dy = motionY;
                        int futureX = 0;
                        int futureY = 0;
                        int td = (int)Math.Clamp((long)colocated.Order.Value - past.Order.Value, -128, 127);
                        if (past.LongTermIndex is null && td != 0)
                        {
                            int tb = (int)Math.Clamp((long)currentOrder.Value - past.Order.Value, -128, 127);
                            int tx = (16384 + Math.Abs(td / 2)) / td;
                            int scale = Math.Clamp((tb * tx + 32) >> 6, -1024, 1023);
                            dx = (scale * motion.X + 128) >> 8;
                            dy = (scale * motionY + 128) >> 8;
                            futureX = dx - motion.X;
                            futureY = dy - motionY;
                        }
                        l0 = new H264PartitionMotion(mapped, dx, dy);
                        l1 = new H264PartitionMotion(0, futureX, futureY);
                    }
                    H264DirectMotion direct = new(l0, l1);
                    uniform &= count == 0 || motions[0] == direct;
                    motions[count++] = direct;
                }
            }
            return uniform ? 1 : count;
        }

        private static H264PartitionMotion ApplyColocatedZero(H264PartitionMotion predicted) =>
            predicted.ReferenceIndex == 0 ? new H264PartitionMotion(0, 0, 0) : predicted;

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
                if (references[index] is H264ReferencePicture reference && ReferenceEquals(reference.Identity, identity))
                {
                    return index;
                }
            }
            throw new InvalidDataException("The H.264 co-located reference is absent from list 0.");
        }
    }
}
