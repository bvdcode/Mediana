// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264DeblockingMotionTestInput
    {
        public static void Verify()
        {
            MediaBufferAllocator allocator = new(65536);
            H264SequenceParameterSet sequence = new()
            {
                WidthInMbs = 2,
                HeightInMapUnits = 2,
                FrameMbsOnly = true,
                Width = 32,
                Height = 32
            };
            using (H264Picture picture = new(sequence, allocator, H264SliceKind.B))
            using (H264Picture first = new(sequence, allocator, H264SliceKind.I))
            using (H264Picture second = new(sequence, allocator, H264SliceKind.I))
            {
                H264ReferencePicture a = new(first, 0, new H264PictureOrder(0, 0));
                H264ReferencePicture b = new(second, 1, new H264PictureOrder(2, 2));
                IReadOnlyList<H264ReferencePicture?> l0 = [a, b, a, null];
                IReadOnlyList<H264ReferencePicture?> l1 = [b, a, null];
                IReadOnlyList<H264ReferencePicture?> alternateL0 = [b, a, b, null];
                IReadOnlyList<H264ReferencePicture?> alternateL1 = [a, b, null];
                picture.CaptureReferences(0, l0, l1);
                picture.CaptureReferences(1, alternateL0, alternateL1);
                picture.CaptureReferences(2, l0, l1);
                picture.CaptureReferences(3, [null], [null]);
                int[] indices = [-1, 0, 1, 2, 3, 31];
                (int X0, int Y0, int X1, int Y1)[] vectors =
                [
                    (0, 0, 0, 0), (3, -3, -3, 3), (4, 0, 0, 0), (0, -4, 0, 0),
                    (0, 0, -4, 0), (0, 0, 0, 4), (16, 0, 0, 0),
                    (int.MinValue, 0, int.MaxValue, 0), (int.MaxValue, int.MinValue, 0, int.MaxValue)
                ];
                int checks = 0;
                for (int captured = 0; captured < 2; captured++)
                {
                    for (int orientation = 0; orientation < 2; orientation++)
                    {
                        bool vertical = orientation == 0;
                        for (int boundary = 0; boundary < 2; boundary++)
                        {
                            int x = vertical && boundary == 1 ? 16 : 4;
                            int y = !vertical && boundary == 1 ? 16 : 4;
                            int address = y / 16 * 2 + x / 16;
                            int current = picture.BlockIndex(0, x, y);
                            int previous = current - (vertical ? 1 : 8);
                            H264DeblockingContext context = new(picture, captured == 0 ? l0 : null, captured == 0 ? l1 : null);
                            context.BeginMacroblock(address);
                            foreach (int p0 in indices)
                                foreach (int p1 in indices)
                                    foreach (int q0 in indices)
                                        foreach (int q1 in indices)
                                            foreach ((int X0, int Y0, int X1, int Y1) vector in vectors)
                                            {
                                                Set(picture, previous, p0, p1, 0, 0, 16, 0);
                                                Set(picture, current, q0, q1, vector.X0, vector.Y0, vector.X1, vector.Y1);
                                                IReadOnlyList<H264ReferencePicture?> qL0 = captured == 1 && address == 1 ? alternateL0 : l0;
                                                IReadOnlyList<H264ReferencePicture?> qL1 = captured == 1 && address == 1 ? alternateL1 : l1;
                                                IReadOnlyList<H264ReferencePicture?> pL0 = boundary == 1 ? l0 : qL0;
                                                IReadOnlyList<H264ReferencePicture?> pL1 = boundary == 1 ? l1 : qL1;
                                                int expected = Expected(p0, p1, q0, q1, pL0, pL1, qL0, qL1, vector);
                                                int actual;
                                                try
                                                {
                                                    actual = context.Strength(x, y, vertical, boundary == 1);
                                                }
                                                catch (InvalidDataException)
                                                {
                                                    actual = -1;
                                                }
                                                if (actual != expected)
                                                {
                                                    throw new InvalidDataException($"Boundary strength mismatch: {captured}/{orientation}/{boundary}, {p0}/{p1}/{q0}/{q1}, {vector}: {actual} != {expected}.");
                                                }
                                                checks++;
                                            }
                        }
                    }
                }
                int internalPosition = picture.BlockIndex(0, 4, 4);
                Set(picture, internalPosition - 1, 0, -1, 0, 0, 0, 0);
                Set(picture, internalPosition, 0, -1, 0, 0, 0, 0);
                H264DeblockingContext reusable = new(picture, null, null);
                reusable.BeginMacroblock(0);
                if (reusable.Strength(4, 4, true, false) != 0)
                {
                    throw new InvalidDataException("A shared internal reference was not recognized.");
                }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int iteration = 0; iteration < 10000; iteration++)
                {
                    if (reusable.Strength(4, 4, true, false) != 0)
                    {
                        throw new InvalidDataException("Repeated internal prediction changed strength.");
                    }
                }
                if (GC.GetAllocatedBytesForCurrentThread() != before)
                {
                    throw new InvalidOperationException("Internal prediction allocated managed storage.");
                }
                int invalidPosition = picture.BlockIndex(0, 20, 20);
                Set(picture, invalidPosition - 1, 0, -1, 0, 0, 0, 0);
                Set(picture, invalidPosition, 0, -1, 0, 0, 0, 0);
                reusable.BeginMacroblock(3);
                bool rejected = false;
                try
                {
                    _ = reusable.Strength(20, 20, true, false);
                }
                catch (InvalidDataException)
                {
                    rejected = true;
                }
                if (!rejected)
                {
                    throw new InvalidDataException("A validation from the preceding macroblock masked a missing reference.");
                }
                if (checks != 93312)
                {
                    throw new InvalidDataException($"Incomplete edge matrix: {checks}.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Boundary strength verification retained picture storage.");
            }
        }

        private static void Set(H264Picture picture, int position, int l0, int l1, int x0, int y0, int x1, int y1)
        {
            picture.Modes[position] = H264Picture.InterMode;
            picture.ReferenceIndices(H264ReferenceList.L0)[position] = l0 < 0 ? byte.MaxValue : (byte)l0;
            picture.ReferenceIndices(H264ReferenceList.L1)[position] = l1 < 0 ? byte.MaxValue : (byte)l1;
            picture.MotionX(H264ReferenceList.L0)[position] = x0;
            picture.MotionY(H264ReferenceList.L0)[position] = y0;
            picture.MotionX(H264ReferenceList.L1)[position] = x1;
            picture.MotionY(H264ReferenceList.L1)[position] = y1;
        }

        private static int Expected(int p0, int p1, int q0, int q1,
            IReadOnlyList<H264ReferencePicture?> pL0, IReadOnlyList<H264ReferencePicture?> pL1,
            IReadOnlyList<H264ReferencePicture?> qL0, IReadOnlyList<H264ReferencePicture?> qL1,
            (int X0, int Y0, int X1, int Y1) vector)
        {
            if (!Valid(p0, pL0) || !Valid(p1, pL1) || !Valid(q0, qL0) || !Valid(q1, qL1)
                || (p0 < 0 && p1 < 0) || (q0 < 0 && q1 < 0))
            {
                return -1;
            }
            (H264Picture? Picture, int X, int Y) pA = Sample(p0, pL0, 0, 0);
            (H264Picture? Picture, int X, int Y) pB = Sample(p1, pL1, 16, 0);
            (H264Picture? Picture, int X, int Y) qA = Sample(q0, qL0, vector.X0, vector.Y0);
            (H264Picture? Picture, int X, int Y) qB = Sample(q1, qL1, vector.X1, vector.Y1);
            bool same = Close(pA, qA) && Close(pB, qB);
            bool swapped = Close(pA, qB) && Close(pB, qA);
            return same || swapped ? 0 : 1;
        }

        private static bool Valid(int index, IReadOnlyList<H264ReferencePicture?> list) =>
            index < 0 || (index < list.Count && list[index] is not null);

        private static (H264Picture? Picture, int X, int Y) Sample(int index, IReadOnlyList<H264ReferencePicture?> list, int x, int y) =>
            index < 0 ? (null, 0, 0) : (list[index]!.Picture, x, y);

        private static bool Close((H264Picture? Picture, int X, int Y) first, (H264Picture? Picture, int X, int Y) second) =>
            ReferenceEquals(first.Picture, second.Picture) && (long)first.X - second.X is > -4 and < 4
                && (long)first.Y - second.Y is > -4 and < 4;
    }
}
