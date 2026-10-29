// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264BidirectionalPrediction
    {
        public static void PredictPartition(H264Picture target, H264SliceHeader header, H264BidirectionalReferences references,
            H264PictureOrder order, int x, int y, int width, int height, H264PartitionMotion l0, H264PartitionMotion l1, Span<byte> prediction)
        {
            if (header.Kind != H264SliceKind.B || prediction.Length != 384 || width <= 0 || height <= 0
                || x < 0 || y < 0 || x % 16 + width > 16 || y % 16 + height > 16)
            {
                throw new InvalidDataException("Invalid H.264 B prediction partition.");
            }
            H264ReferencePicture? reference0 = Resolve(references.L0, l0);
            H264ReferencePicture? reference1 = Resolve(references.L1, l1);
            if (reference0 is null && reference1 is null)
            {
                throw new InvalidDataException("An H.264 B prediction partition must use at least one list.");
            }
            target.SetMotion(H264ReferenceList.L0, x, y, width, height, l0.ReferenceIndex, l0.X, l0.Y);
            target.SetMotion(H264ReferenceList.L1, x, y, width, height, l1.ReferenceIndex, l1.X, l1.Y);
            Span<byte> block0 = stackalloc byte[256];
            Span<byte> block1 = stackalloc byte[256];
            for (int plane = 0; plane < 3; plane++)
            {
                int scale = plane == 0 ? 1 : 2;
                int count = width / scale * (height / scale);
                Span<byte> samples0 = block0[..count];
                Span<byte> samples1 = block1[..count];
                if (reference0 is not null)
                {
                    H264PredictionBlock.Read(reference0.Picture, plane, x, y, width, height, l0.X, l0.Y, samples0);
                }
                if (reference1 is not null)
                {
                    H264PredictionBlock.Read(reference1.Picture, plane, x, y, width, height, l1.X, l1.Y, samples1);
                }
                if (reference0 is not null && reference1 is not null)
                {
                    H264BiPredictionWeights weights = header.Picture.WeightedBiPrediction switch
                    {
                        0 => H264BiPredictionWeights.Average,
                        1 => H264BiPredictionWeights.Explicit(RequireWeights(header, H264ReferenceList.L0), l0.ReferenceIndex,
                            RequireWeights(header, H264ReferenceList.L1), l1.ReferenceIndex, plane),
                        2 => H264BiPredictionWeights.Implicit(order, reference0, reference1),
                        _ => throw new InvalidDataException("Invalid H.264 B weighted prediction mode.")
                    };
                    weights.Apply(samples0, samples1, samples0);
                    H264PredictionBlock.Write(plane, x, y, width, height, samples0, prediction);
                }
                else if (reference0 is not null)
                {
                    ApplySingle(header, H264ReferenceList.L0, l0.ReferenceIndex, plane, samples0);
                    H264PredictionBlock.Write(plane, x, y, width, height, samples0, prediction);
                }
                else
                {
                    ApplySingle(header, H264ReferenceList.L1, l1.ReferenceIndex, plane, samples1);
                    H264PredictionBlock.Write(plane, x, y, width, height, samples1, prediction);
                }
            }
        }

        private static H264ReferencePicture? Resolve(IReadOnlyList<H264ReferencePicture?> references, H264PartitionMotion motion)
        {
            if (motion.ReferenceIndex == -1)
            {
                return null;
            }
            if ((uint)motion.ReferenceIndex >= (uint)references.Count || references[motion.ReferenceIndex] is not H264ReferencePicture reference
                || motion.X is < short.MinValue or > short.MaxValue || motion.Y is < short.MinValue or > short.MaxValue)
            {
                throw new InvalidDataException("The H.264 B partition uses an unavailable reference or invalid motion vector.");
            }
            return reference;
        }

        private static void ApplySingle(H264SliceHeader header, H264ReferenceList list, int reference, int plane, Span<byte> samples)
        {
            switch (header.Picture.WeightedBiPrediction)
            {
                case 0:
                case 2:
                    return;
                case 1:
                    RequireWeights(header, list).Apply(reference, plane, samples);
                    return;
                default:
                    throw new InvalidDataException("Invalid H.264 B weighted prediction mode.");
            }
        }

        private static H264WeightTable RequireWeights(H264SliceHeader header, H264ReferenceList list) => (list switch
        {
            H264ReferenceList.L0 => header.Weights,
            H264ReferenceList.L1 => header.WeightsL1,
            _ => throw new ArgumentOutOfRangeException(nameof(list))
        }) ?? throw new InvalidDataException("The H.264 B partition has no explicit prediction weights.");
    }
}
