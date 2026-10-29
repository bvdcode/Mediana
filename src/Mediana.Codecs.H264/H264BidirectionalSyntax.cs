// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264BidirectionalSyntax
    {
        public static bool Read<TReader>(ref H264BitReader bits, TReader reader, H264Picture target, H264SliceHeader header,
            H264BidirectionalReferences references, H264PictureOrder order, int x, int y, int type, scoped Span<byte> prediction)
            where TReader : IH264PredictionReader
        {
            int count = H264BPartitionLayout.Count(type);
            Span<H264BPartition> partitions = stackalloc H264BPartition[count];
            bool noSmallPartitions = true;
            for (int part = 0; part < count; part++)
            {
                int subtype = type == 22 ? reader.ReadSubType(ref bits, H264SliceKind.B) : 0;
                H264BPartition partition = H264BPartitionLayout.Get(type, part, subtype);
                partitions[part] = partition;
                noSmallPartitions &= partition.Mode switch
                {
                    H264BPredictionMode.Direct => header.Sequence.Direct8x8Inference,
                    H264BPredictionMode.L0 or H264BPredictionMode.L1 or H264BPredictionMode.Bi => partition.Width >= 8 && partition.Height >= 8,
                    _ => throw new InvalidDataException("Invalid B prediction mode.")
                };
            }
            Span<int> indicesL0 = stackalloc int[count];
            Span<int> indicesL1 = stackalloc int[count];
            ReadReferences(ref bits, reader, partitions, H264ReferenceList.L0, header.ActiveReferencesL0, type, x, y, indicesL0);
            ReadReferences(ref bits, reader, partitions, H264ReferenceList.L1, header.ActiveReferencesL1, type, x, y, indicesL1);
            Span<(int X, int Y)> differencesL0 = stackalloc (int, int)[16];
            Span<(int X, int Y)> differencesL1 = stackalloc (int, int)[16];
            ReadDifferences(ref bits, reader, partitions, H264ReferenceList.L0, type, x, y, differencesL0);
            ReadDifferences(ref bits, reader, partitions, H264ReferenceList.L1, type, x, y, differencesL1);
            for (int part = 0; part < count; part++)
            {
                H264BPartition partition = partitions[part];
                if (partition.Mode == H264BPredictionMode.Direct)
                {
                    H264DirectPrediction.Predict(target, header, references, order, x + partition.X, y + partition.Y, prediction);
                    continue;
                }
                int areaWidth = type == 22 ? 8 : partition.Width;
                int areaHeight = type == 22 ? 8 : partition.Height;
                int subpart = 0;
                for (int row = 0; row < areaHeight; row += partition.Height)
                {
                    for (int column = 0; column < areaWidth; column += partition.Width)
                    {
                        int blockX = x + partition.X + column;
                        int blockY = y + partition.Y + row;
                        H264PartitionMotion l0 = Motion(target, H264ReferenceList.L0, blockX, blockY, partition, part,
                            indicesL0[part], differencesL0[part * 4 + subpart]);
                        H264PartitionMotion l1 = Motion(target, H264ReferenceList.L1, blockX, blockY, partition, part,
                            indicesL1[part], differencesL1[part * 4 + subpart]);
                        H264BidirectionalPrediction.PredictPartition(target, header, references, order, blockX, blockY,
                            partition.Width, partition.Height, l0, l1, prediction);
                        subpart++;
                    }
                }
            }
            return noSmallPartitions;
        }

        private static void ReadReferences<TReader>(ref H264BitReader bits, TReader reader, scoped ReadOnlySpan<H264BPartition> partitions,
            H264ReferenceList list, int active, int type, int x, int y, scoped Span<int> indices)
            where TReader : IH264PredictionReader
        {
            for (int part = 0; part < partitions.Length; part++)
            {
                if (!H264BPartitionLayout.Uses(partitions[part].Mode, list))
                {
                    indices[part] = -1;
                }
                else
                {
                    indices[part] = active switch
                    {
                        1 => 0,
                        >= 2 and <= 32 => reader.ReadReference(ref bits, list, x + partitions[part].X, y + partitions[part].Y,
                            type == 22 ? 8 : partitions[part].Width, type == 22 ? 8 : partitions[part].Height, active - 1),
                        _ => throw new InvalidDataException("Invalid H.264 B active reference count.")
                    };
                }
            }
        }

        private static void ReadDifferences<TReader>(ref H264BitReader bits, TReader reader, scoped ReadOnlySpan<H264BPartition> partitions,
            H264ReferenceList list, int type, int x, int y, scoped Span<(int X, int Y)> differences)
            where TReader : IH264PredictionReader
        {
            differences.Clear();
            for (int part = 0; part < partitions.Length; part++)
            {
                H264BPartition partition = partitions[part];
                if (!H264BPartitionLayout.Uses(partition.Mode, list))
                {
                    continue;
                }
                int count = type == 22 ? 64 / (partition.Width * partition.Height) : 1;
                for (int subpart = 0; subpart < count; subpart++)
                {
                    int column = type == 22 ? subpart % (8 / partition.Width) * partition.Width : 0;
                    int row = type == 22 ? subpart / (8 / partition.Width) * partition.Height : 0;
                    differences[part * 4 + subpart] = reader.ReadMotionDifference(ref bits, list,
                        x + partition.X + column, y + partition.Y + row, partition.Width, partition.Height);
                }
            }
        }

        private static H264PartitionMotion Motion(H264Picture target, H264ReferenceList list, int x, int y,
            H264BPartition partition, int part, int reference, (int X, int Y) difference)
        {
            if (reference < 0)
            {
                return H264PartitionMotion.None;
            }
            (int dx, int dy) = H264MotionPrediction.Predict(target, list, x, y, partition.Width, partition.Height, reference, part);
            return new H264PartitionMotion(reference, dx + difference.X, dy + difference.Y);
        }
    }
}
