// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264InterPrediction
    {
        public static void Skip(H264Picture target, H264SliceHeader header, IReadOnlyList<H264ReferencePicture?> references,
            int x, int y, scoped Span<byte> prediction)
        {
            (int motionX, int motionY) = H264MotionPrediction.Skip(target, x, y);
            PredictPartition(target, header, references, x, y, 16, 16, 0, motionX, motionY, prediction);
        }

        public static bool Read<TReader>(ref H264BitReader bits, TReader reader, H264Picture target, H264SliceHeader header,
            IReadOnlyList<H264ReferencePicture?> references, int x, int y, int type, scoped Span<byte> prediction)
            where TReader : IH264PredictionReader
        {
            int partitions = type switch
            {
                0 => 1,
                1 or 2 => 2,
                3 or 4 => 4,
                _ => throw new InvalidDataException("Invalid H.264 P macroblock type.")
            };
            Span<int> subtypes = stackalloc int[4];
            Span<int> indices = stackalloc int[4];
            subtypes.Clear();
            indices.Clear();
            bool noSmallPartitions = true;
            if (partitions == 4)
            {
                for (int part = 0; part < partitions; part++)
                {
                    subtypes[part] = reader.ReadSubType(ref bits, H264SliceKind.P);
                    noSmallPartitions &= subtypes[part] == 0;
                }
            }
            for (int part = 0; part < partitions; part++)
            {
                (int partX, int partY) = Position(x, y, type, part);
                (int width, int height) = Size(type, 0);
                if (type != 4 && header.ActiveReferencesL0 > 1)
                {
                    indices[part] = reader.ReadReference(ref bits, H264ReferenceList.L0, partX, partY, width, height, header.ActiveReferencesL0 - 1);
                }
                target.SetReference(H264ReferenceList.L0, partX, partY, width, height, indices[part]);
            }
            for (int part = 0; part < partitions; part++)
            {
                (int width, int height) = Size(type, subtypes[part]);
                (int partX, int partY) = Position(x, y, type, part);
                int areaWidth = partitions == 4 ? 8 : width;
                int areaHeight = partitions == 4 ? 8 : height;
                for (int subY = 0; subY < areaHeight; subY += height)
                {
                    for (int subX = 0; subX < areaWidth; subX += width)
                    {
                        (int motionX, int motionY) = H264MotionPrediction.Predict(target, H264ReferenceList.L0, partX + subX, partY + subY,
                            width, height, indices[part], part);
                        (int deltaX, int deltaY) = reader.ReadMotionDifference(ref bits, H264ReferenceList.L0, partX + subX, partY + subY, width, height);
                        motionX += deltaX;
                        motionY += deltaY;
                        if (motionX is < short.MinValue or > short.MaxValue || motionY is < short.MinValue or > short.MaxValue)
                        {
                            throw new InvalidDataException("The H.264 motion vector exceeds its supported range.");
                        }
                        PredictPartition(target, header, references, partX + subX, partY + subY, width, height,
                            indices[part], motionX, motionY, prediction);
                    }
                }
            }
            return noSmallPartitions;
        }

        private static (int Width, int Height) Size(int type, int subtype) => type switch
        {
            0 => (16, 16),
            1 => (16, 8),
            2 => (8, 16),
            3 or 4 => subtype switch
            {
                0 => (8, 8),
                1 => (8, 4),
                2 => (4, 8),
                3 => (4, 4),
                _ => throw new InvalidDataException("Invalid H.264 P sub-macroblock type.")
            },
            _ => throw new InvalidDataException("Invalid H.264 P macroblock type.")
        };

        private static (int X, int Y) Position(int x, int y, int type, int part) => type switch
        {
            0 => (x, y),
            1 => (x, y + part * 8),
            2 => (x + part * 8, y),
            3 or 4 => (x + part % 2 * 8, y + part / 2 * 8),
            _ => throw new InvalidDataException("Invalid H.264 P macroblock type.")
        };

        private static void PredictPartition(H264Picture target, H264SliceHeader header,
            IReadOnlyList<H264ReferencePicture?> references, int x, int y, int width, int height,
            int referenceIndex, int motionX, int motionY, Span<byte> prediction)
        {
            if ((uint)referenceIndex >= (uint)references.Count || references[referenceIndex] is not H264ReferencePicture reference)
            {
                throw new InvalidDataException("The H.264 partition uses an unavailable reference picture.");
            }
            target.SetMotion(H264ReferenceList.L0, x, y, width, height, referenceIndex, motionX, motionY);
            Span<byte> block = stackalloc byte[256];
            for (int plane = 0; plane < 3; plane++)
            {
                int scale = plane == 0 ? 1 : 2;
                int blockWidth = width / scale;
                int blockHeight = height / scale;
                Span<byte> samples = block[..(blockWidth * blockHeight)];
                H264PredictionBlock.Read(reference.Picture, plane, x, y, width, height, motionX, motionY, samples);
                header.Weights?.Apply(referenceIndex, plane, samples);
                H264PredictionBlock.Write(plane, x, y, width, height, samples, prediction);
            }
        }
    }
}
