// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264MbaffMotionTestInput
    {
        public static void Verify()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            foreach (bool field in new[] { false, true })
            {
                foreach (bool neighbourField in new[] { false, true })
                {
                    VerifyGeometry(allocator, field, neighbourField);
                }
            }
            VerifyAvailability(allocator);
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("MBAFF motion validation retained picture storage.");
            }
        }

        private static void VerifyGeometry(MediaBufferAllocator allocator, bool field, bool neighbourField)
        {
            using H264Picture picture = Create(allocator, field, neighbourField);
            int leftLower = field && !neighbourField ? 7 : 6;
            int top = field && neighbourField ? 2 : 3;
            int bottomLeft = field == neighbourField ? 7 : 6;
            foreach (H264ReferenceList list in new[] { H264ReferenceList.L0, H264ReferenceList.L1 })
            {
                Check(picture, list, 8, -1, 0, 6, neighbourField, field);
                Check(picture, list, 8, -1, 8, leftLower, neighbourField, field);
                Check(picture, list, 8, 0, -1, top, neighbourField, field);
                Check(picture, list, 9, -1, 0, bottomLeft, neighbourField, field);
                Check(picture, list, 9, 0, -1, field ? 3 : 8, field ? neighbourField : field, field);
                Check(picture, list, 8, 3, 3, 8, field, field);
                (int x, int y) = picture.Grid.Position(8);
                (int dx, int dy) = H264MotionPrediction.Predict(picture, list, x, y, 16, 8,
                    ScaleReference(list, neighbourField, field), 0);
                H264MotionSample expected = Expected(list, top, neighbourField, field);
                if (dx != expected.X || dy != expected.Y)
                {
                    throw new InvalidDataException("The 16x8 motion predictor did not use the scaled top neighbour.");
                }
            }
        }

        private static void Check(H264Picture picture, H264ReferenceList list, int current,
            int relativeX, int relativeY, int neighbour, bool neighbourField, bool field)
        {
            (int x, int y) = picture.Grid.Position(current);
            H264MotionSample actual = picture.NeighbourMotionAt(list, x, y, x + relativeX, y + relativeY);
            H264MotionSample expected = Expected(list, neighbour, neighbourField, field);
            if (actual != expected)
            {
                throw new InvalidDataException($"MBAFF motion mismatch: current={current}; field={field}; neighbourField={neighbourField}; relative=({relativeX},{relativeY}); expected={expected}; actual={actual}.");
            }
        }

        private static H264MotionSample Expected(H264ReferenceList list, int address, bool neighbourField, bool field)
        {
            int y = -11 - address * 2;
            int expectedY = (field, neighbourField) switch
            {
                (true, false) => y / 2,
                (false, true) => y * 2,
                (true, true) or (false, false) => y
            };
            return new(true, ScaleReference(list, neighbourField, field), 100 + address, expectedY);
        }

        private static int ScaleReference(H264ReferenceList list, bool neighbourField, bool field)
        {
            int reference = list switch
            {
                H264ReferenceList.L0 => 3,
                H264ReferenceList.L1 => 4,
                _ => throw new ArgumentOutOfRangeException(nameof(list))
            };
            return (field, neighbourField) switch
            {
                (true, false) => reference * 2,
                (false, true) => reference / 2,
                (true, true) or (false, false) => reference
            };
        }

        private static void VerifyAvailability(MediaBufferAllocator allocator)
        {
            using H264Picture picture = Create(allocator, true, false);
            (int x, int y) = picture.Grid.Position(8);
            picture.FirstMacroblock = 8;
            H264MotionSample unavailable = new(false, -1, 0, 0);
            if (picture.NeighbourMotionAt(H264ReferenceList.L0, x, y, x - 1, y) != unavailable ||
                picture.NeighbourMotionAt(H264ReferenceList.L0, x, y, x, y - 1) != unavailable)
            {
                throw new InvalidDataException("MBAFF motion crossed the slice boundary.");
            }
            Check(picture, H264ReferenceList.L0, 8, 1, 1, 8, true, true);
            picture.FirstMacroblock = 0;
            (int leftX, int leftY) = picture.Grid.Position(6);
            picture.SetMotion(H264ReferenceList.L0, leftX, leftY, 16, 16, -1, 0, 0);
            if (picture.NeighbourMotionAt(H264ReferenceList.L0, x, y, x - 1, y) != new H264MotionSample(true, -1, 0, 0))
            {
                throw new InvalidDataException("MBAFF motion changed an absent reference into a field reference.");
            }
            picture.Modes[picture.BlockIndex(0, leftX + 12, leftY)] = byte.MaxValue;
            if (picture.NeighbourMotionAt(H264ReferenceList.L0, x, y, x - 1, y) != unavailable)
            {
                throw new InvalidDataException("MBAFF motion used an undecoded neighbour.");
            }
            (int bottomX, int bottomY) = picture.Grid.Position(9);
            if (picture.NeighbourMotionAt(H264ReferenceList.L0, bottomX, bottomY, bottomX + 16, bottomY + 2) != unavailable)
            {
                throw new InvalidDataException("MBAFF motion used a future macroblock pair.");
            }
        }

        private static H264Picture Create(MediaBufferAllocator allocator, bool field, bool neighbourField)
        {
            H264SequenceParameterSet sequence = new()
            {
                WidthInMbs = 3,
                HeightInMapUnits = 2,
                FrameMbsOnly = false,
                MbAdaptiveFrameField = true,
                Width = 48,
                Height = 64
            };
            H264Picture picture = new(sequence, allocator, H264SliceKind.B);
            for (int address = 0; address < 12; address += 2)
            {
                picture.Grid.SetFieldPair(address, address == 8 ? field : neighbourField);
            }
            for (int address = 0; address < 12; address++)
            {
                (int x, int y) = picture.Grid.Position(address);
                picture.SetMotion(H264ReferenceList.L0, x, y, 16, 16, 3, 100 + address, -11 - address * 2);
                picture.SetMotion(H264ReferenceList.L1, x, y, 16, 16, 4, 100 + address, -11 - address * 2);
            }
            return picture;
        }
    }
}
