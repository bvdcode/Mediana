// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264DirectRegionTestInput
    {
        public static void Verify()
        {
            foreach (bool inference in new[] { false, true })
                foreach (bool spatial in new[] { false, true })
                    foreach (bool uniform in new[] { false, true })
                        foreach (bool longTerm in new[] { false, true })
                            foreach (int region in new[] { 8, 16 })
                                foreach (int weights in new[] { 0, 1, 2 })
                                    foreach (int lists in new[] { 0, 1, 2, 3 })
                                    {
                                        VerifyCase(inference, spatial, uniform, longTerm, region, weights, lists);
                                    }
        }

        private static void VerifyCase(bool inference, bool spatial, bool uniform, bool longTerm,
            int region, int weights, int lists)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            H264SequenceParameterSet sequence = new()
            {
                WidthInMbs = 3,
                HeightInMapUnits = 3,
                FrameMbsOnly = true,
                Direct8x8Inference = inference
            };
            using H264Picture actual = new(sequence, allocator, H264SliceKind.B);
            using H264Picture expected = new(sequence, allocator, H264SliceKind.B);
            using H264Picture past0 = new(sequence, allocator, H264SliceKind.I);
            using H264Picture past1 = new(sequence, allocator, H264SliceKind.I);
            using H264Picture future = new(sequence, allocator, H264SliceKind.P);
            Fill(past0, 17);
            Fill(past1, 73);
            Fill(future, 149);
            H264ReferencePicture reference0 = new(past0, 0, new(0, 0), longTerm ? 1 : null);
            H264ReferencePicture reference1 = new(past1, 1, new(-4, -4), longTerm ? 2 : null);
            H264ReferencePicture colocated = new(future, 2, new(8, 8), longTerm ? 0 : null);
            H264BidirectionalReferences references = new([reference0, reference1], [colocated]);
            future.CaptureReferences(0, references.L0, []);
            for (int row = 0; row < 48; row += 4)
                for (int column = 0; column < 48; column += 4)
                {
                    H264PartitionMotion motion = Colocated(column, row, uniform);
                    future.SetMotion(H264ReferenceList.L0, column, row, 4, 4, motion.ReferenceIndex, motion.X, motion.Y);
                }
            int spatialReference = uniform ? 0 : 1;
            InitializeNeighbours(actual, lists, spatialReference);
            InitializeNeighbours(expected, lists, spatialReference);
            H264SliceHeader header = new(sequence,
                new H264PictureParameterSet(0, 0, false, false, 26, 0, 0, true, false, false, false, 2, 1, false, weights))
            {
                Kind = H264SliceKind.B,
                DirectSpatialMotionPrediction = spatial,
                ActiveReferencesL0 = 2,
                ActiveReferencesL1 = 1,
                Weights = new(2, 2, [new(3, -7, 2, 11, 5, -17), new(-2, 19, 4, -13, 1, 23)]),
                WeightsL1 = new(2, 2, [new(2, 9, 3, -15, 2, 7)])
            };
            int x = region == 8 ? 24 : 16;
            Span<byte> actualPixels = stackalloc byte[384];
            Span<byte> expectedPixels = stackalloc byte[384];
            actualPixels.Fill(201);
            expectedPixels.Fill(201);
            int size = inference ? 8 : 4;
            for (int row = 16; row < 16 + region; row += size)
                for (int column = x; column < x + region; column += size)
                {
                    int colX = inference ? (column < 24 ? 16 : 28) : column;
                    int colY = inference ? (row < 24 ? 16 : 28) : row;
                    H264PartitionMotion motion = Colocated(colX, colY, uniform);
                    H264DirectMotion direct = spatial
                        ? Spatial(lists, spatialReference, motion, longTerm)
                        : Temporal(motion, longTerm);
                    H264BidirectionalPrediction.PredictPartition(expected, header, references, new(2, 2), column, row,
                        size, size, direct.L0, direct.L1, expectedPixels);
                }
            if (region == 16)
            {
                H264DirectPrediction.PredictMacroblock(actual, header, references, new(2, 2), x, 16, actualPixels);
            }
            else
            {
                H264DirectPrediction.Predict(actual, header, references, new(2, 2), x, 16, actualPixels);
            }
            Require(actualPixels.SequenceEqual(expectedPixels) && actual.Modes.SequenceEqual(expected.Modes));
            foreach (H264ReferenceList list in new[] { H264ReferenceList.L0, H264ReferenceList.L1 })
            {
                Require(actual.ReferenceIndices(list).SequenceEqual(expected.ReferenceIndices(list)) &&
                    actual.MotionX(list).SequenceEqual(expected.MotionX(list)) && actual.MotionY(list).SequenceEqual(expected.MotionY(list)));
            }
            actual.Dispose();
            expected.Dispose();
            past0.Dispose();
            past1.Dispose();
            future.Dispose();
            Require(allocator.AllocatedBytes == 0);
        }

        private static H264DirectMotion Spatial(int lists, int reference, H264PartitionMotion motion, bool longTerm)
        {
            bool zero = !longTerm && motion.ReferenceIndex == 0 && Math.Abs(motion.X) <= 1 && Math.Abs(motion.Y) <= 1;
            H264PartitionMotion l0 = H264PartitionMotion.None;
            H264PartitionMotion l1 = H264PartitionMotion.None;
            if (lists is 0 or 1)
            {
                l0 = zero && reference == 0 ? new(reference, 0, 0) : new(reference, 12, 8);
            }
            if (lists is 0 or 2)
            {
                l1 = zero ? new(0, 0, 0) : new(0, -8, 4);
            }
            if (lists == 3)
            {
                l0 = new(0, 0, 0);
                l1 = new(0, 0, 0);
            }
            return new(l0, l1);
        }

        private static H264DirectMotion Temporal(H264PartitionMotion motion, bool longTerm)
        {
            int reference = Math.Max(0, motion.ReferenceIndex);
            int distance = reference == 0 ? 8 : 12;
            int currentDistance = reference == 0 ? 2 : 6;
            int factor = Math.Clamp((currentDistance * ((16384 + distance / 2) / distance) + 32) >> 6, -1024, 1023);
            int dx = longTerm ? motion.X : (factor * motion.X + 128) >> 8;
            int dy = longTerm ? motion.Y : (factor * motion.Y + 128) >> 8;
            return new(new(reference, dx, dy), new(0, longTerm ? 0 : dx - motion.X, longTerm ? 0 : dy - motion.Y));
        }

        private static H264PartitionMotion Colocated(int x, int y, bool uniform) => uniform
            ? new(0, 1, -1)
            : ((x / 4 + y / 4 * 3) % 4) switch
            {
                0 => new(0, 1, -1),
                1 => new(1, 12, -8),
                2 => new(0, -15, 9),
                3 => new(-1, 0, 0),
                _ => throw new InvalidDataException("Invalid co-located test block.")
            };

        private static void InitializeNeighbours(H264Picture picture, int lists, int reference)
        {
            foreach ((int x, int y) in new[] { (12, 16), (16, 12), (32, 12) })
            {
                if (lists is 0 or 1)
                {
                    picture.SetMotion(H264ReferenceList.L0, x, y, 4, 4, reference, 12, 8);
                }
                if (lists is 0 or 2)
                {
                    picture.SetMotion(H264ReferenceList.L1, x, y, 4, 4, 0, -8, 4);
                }
            }
        }

        private static void Fill(H264Picture picture, int seed)
        {
            for (int plane = 0; plane < 3; plane++)
            {
                Span<byte> pixels = picture.Plane(plane);
                for (int index = 0; index < pixels.Length; index++)
                {
                    pixels[index] = (byte)((index * 29 + index / picture.Stride(plane) * 13 + seed + plane * 41) & 255);
                }
            }
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("H.264 direct regions changed pixels, motion metadata or buffer ownership.");
            }
        }
    }
}
