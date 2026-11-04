// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    internal static partial class H264DeblockingMotionTestInput
    {
        private static void VerifyMixed(H264Picture picture,
            IReadOnlyList<H264ReferencePicture?> l0, IReadOnlyList<H264ReferencePicture?> l1,
            IReadOnlyList<H264ReferencePicture?> alternateL0, IReadOnlyList<H264ReferencePicture?> alternateL1,
            (int X0, int Y0, int X1, int Y1)[] vectors)
        {
            Random random = new(9241);
            int[] indices = [-1, 0, 1, 2, 3, 31];
            int[] p0 = new int[4];
            int[] p1 = new int[4];
            int[] q0 = new int[4];
            int[] q1 = new int[4];
            (int X0, int Y0, int X1, int Y1)[] motion = new (int, int, int, int)[4];
            Span<int> expected = stackalloc int[4];
            Span<byte> actual = stackalloc byte[8];
            int observedMasks = 0;
            int cases = 0;
            for (int captured = 0; captured < 2; captured++)
                for (int orientation = 0; orientation < 2; orientation++)
                    for (int boundary = 0; boundary < 2; boundary++)
                        for (int mask = 0; mask < 16; mask++)
                            for (int transform = 0; transform < 4; transform++)
                            {
                                bool vertical = orientation == 0;
                                int x = vertical ? boundary == 1 ? 16 : 4 : 0;
                                int y = vertical ? 0 : boundary == 1 ? 16 : 4;
                                int address = y / 16 * 2 + x / 16;
                                int previousAddress = boundary == 1 ? address - (vertical ? 1 : 2) : address;
                                picture.Modes.Fill(H264Picture.InterMode);
                                picture.Counts(0).Clear();
                                picture.Transforms8x8.Clear();
                                picture.Transforms8x8[address] = (byte)(transform & 1);
                                picture.Transforms8x8[previousAddress] |= (byte)((transform >> 1) & 1);
                                for (int lane = 0; lane < 4; lane++)
                                {
                                    int q = picture.BlockIndex(0, x + (vertical ? 0 : lane * 4), y + (vertical ? lane * 4 : 0));
                                    p0[lane] = indices[random.Next(indices.Length)];
                                    p1[lane] = picture.HasBidirectionalMotion ? indices[random.Next(indices.Length)] : -1;
                                    q0[lane] = indices[random.Next(indices.Length)];
                                    q1[lane] = picture.HasBidirectionalMotion ? indices[random.Next(indices.Length)] : -1;
                                    motion[lane] = vectors[random.Next(vectors.Length)];
                                    Set(picture, q - (vertical ? 1 : 8), p0[lane], p1[lane], 0, 0, 16, 0);
                                    Set(picture, q, q0[lane], q1[lane], motion[lane].X0, motion[lane].Y0, motion[lane].X1, motion[lane].Y1);
                                    if ((mask & (1 << lane)) == 0)
                                    {
                                        if (((lane + transform) & 1) == 0)
                                        {
                                            picture.Modes[q] = 2;
                                        }
                                        else
                                        {
                                            picture.Counts(0)[q] = 1;
                                        }
                                    }
                                }
                                IReadOnlyList<H264ReferencePicture?> currentL0 = captured == 1 && address == 1 ? alternateL0 : l0;
                                IReadOnlyList<H264ReferencePicture?> currentL1 = captured == 1 && address == 1 ? alternateL1 : l1;
                                IReadOnlyList<H264ReferencePicture?> previousL0 = boundary == 1 ? l0 : currentL0;
                                IReadOnlyList<H264ReferencePicture?> previousL1 = boundary == 1 ? l1 : currentL1;
                                bool invalid = false;
                                int needed = 0;
                                for (int lane = 0; lane < 4; lane++)
                                {
                                    int sx = x + (vertical ? 0 : lane * 4);
                                    int sy = y + (vertical ? lane * 4 : 0);
                                    int q = picture.BlockIndex(0, sx, sy);
                                    int p = q - (vertical ? 1 : 8);
                                    if (picture.Modes[p] != H264Picture.InterMode || picture.Modes[q] != H264Picture.InterMode)
                                    {
                                        expected[lane] = boundary == 1 ? 4 : 3;
                                    }
                                    else if (Coefficients(picture, sx - (vertical ? 1 : 0), sy - (vertical ? 0 : 1))
                                        || Coefficients(picture, sx, sy))
                                    {
                                        expected[lane] = 2;
                                    }
                                    else
                                    {
                                        needed |= 1 << lane;
                                        expected[lane] = Expected(p0[lane], p1[lane], q0[lane], q1[lane],
                                            previousL0, previousL1, currentL0, currentL1, motion[lane]);
                                        invalid |= expected[lane] == -1;
                                    }
                                }
                                observedMasks |= 1 << needed;
                                byte[] originalModes = picture.Modes.ToArray();
                                byte[] originalCounts = picture.Counts(0).ToArray();
                                byte[] originalL0 = picture.ReferenceIndices(H264ReferenceList.L0).ToArray();
                                int[] originalX0 = picture.MotionX(H264ReferenceList.L0).ToArray();
                                int[] originalY0 = picture.MotionY(H264ReferenceList.L0).ToArray();
                                byte[] originalTransforms = picture.Transforms8x8.ToArray();
                                byte[] originalL1 = picture.HasBidirectionalMotion ? picture.ReferenceIndices(H264ReferenceList.L1).ToArray() : [];
                                int[] originalX1 = picture.HasBidirectionalMotion ? picture.MotionX(H264ReferenceList.L1).ToArray() : [];
                                int[] originalY1 = picture.HasBidirectionalMotion ? picture.MotionY(H264ReferenceList.L1).ToArray() : [];
                                H264DeblockingContext context = new(picture, captured == 0 ? l0 : null, captured == 0 ? l1 : null);
                                context.BeginMacroblock(address);
                                actual.Fill(165);
                                int firstInvalid = expected.IndexOf(-1);
                                string? expectedError = null;
                                if (firstInvalid >= 0)
                                {
                                    bool unavailable = !Valid(p0[firstInvalid], previousL0) || !Valid(q0[firstInvalid], currentL0)
                                        || !Valid(p1[firstInvalid], previousL1) || !Valid(q1[firstInvalid], currentL1);
                                    expectedError = unavailable ? "The H.264 edge uses an unavailable reference picture."
                                        : "The H.264 inter edge has no prediction reference.";
                                }
                                string? actualError = null;
                                try
                                {
                                    FillEdge(ref context, x, y, vertical, boundary == 1, actual.Slice(2, 4));
                                }
                                catch (InvalidDataException exception)
                                {
                                    actualError = exception.Message;
                                }
                                if ((actualError is not null) != invalid || actualError != expectedError || actual[0] != 165 || actual[1] != 165 || actual[6] != 165 || actual[7] != 165)
                                {
                                    throw new InvalidOperationException("Mixed edge rejection or guard mismatch.");
                                }
                                for (int lane = 0; lane < 4; lane++)
                                {
                                    int expectedValue = firstInvalid < 0 || lane < firstInvalid ? expected[lane] : 0;
                                    if (actual[2 + lane] != expectedValue)
                                    {
                                        throw new InvalidOperationException($"Mixed strength or partial-write mismatch: {captured}/{orientation}/{boundary}/{mask}/{transform}/{lane}.");
                                    }
                                }
                                if (!picture.Transforms8x8.SequenceEqual(originalTransforms)
                                    || (picture.HasBidirectionalMotion && (!picture.ReferenceIndices(H264ReferenceList.L1).SequenceEqual(originalL1)
                                        || !picture.MotionX(H264ReferenceList.L1).SequenceEqual(originalX1)
                                        || !picture.MotionY(H264ReferenceList.L1).SequenceEqual(originalY1)))
                                    || !picture.Modes.SequenceEqual(originalModes) || !picture.Counts(0).SequenceEqual(originalCounts)
                                    || !picture.ReferenceIndices(H264ReferenceList.L0).SequenceEqual(originalL0)
                                    || !picture.MotionX(H264ReferenceList.L0).SequenceEqual(originalX0)
                                    || !picture.MotionY(H264ReferenceList.L0).SequenceEqual(originalY0))
                                {
                                    throw new InvalidOperationException("Boundary classification changed source metadata.");
                                }
                                cases++;
                            }
            if (cases != 512 || observedMasks != ushort.MaxValue)
            {
                throw new InvalidOperationException($"Incomplete mixed matrix: {cases}/{observedMasks}.");
            }
            picture.Modes.Fill(H264Picture.InterMode);
            picture.Counts(0).Clear();
            picture.Transforms8x8.Clear();
            picture.ReferenceIndices(H264ReferenceList.L0).Fill(0);
            picture.MotionX(H264ReferenceList.L0).Clear();
            picture.MotionY(H264ReferenceList.L0).Clear();
            if (picture.HasBidirectionalMotion)
            {
                picture.ReferenceIndices(H264ReferenceList.L1).Fill(byte.MaxValue);
            }
            H264DeblockingContext reusable = new(picture, null, null);
            reusable.BeginMacroblock(0);
            FillEdge(ref reusable, 4, 0, true, false, actual.Slice(2, 4));
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 10000; iteration++)
            {
                FillEdge(ref reusable, 4, 0, true, false, actual.Slice(2, 4));
            }
            if (GC.GetAllocatedBytesForCurrentThread() != before || actual.Slice(2, 4).ContainsAnyExcept((byte)0))
            {
                throw new InvalidOperationException("Repeated batch allocation or prediction mismatch.");
            }
        }

        private static bool Coefficients(H264Picture picture, int x, int y)
        {
            int address = y / 16 * 2 + x / 16;
            int size = picture.Transforms8x8[address] == 0 ? 4 : 8;
            int firstX = x / size * size;
            int firstY = y / size * size;
            for (int sy = firstY; sy < firstY + size; sy += 4)
            {
                for (int sx = firstX; sx < firstX + size; sx += 4)
                {
                    if (picture.Counts(0)[picture.BlockIndex(0, sx, sy)] != 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void FillEdge(ref H264DeblockingContext context, int x, int y, bool vertical,
            bool macroblockEdge, scoped Span<byte> result)
        {
            int edge = (vertical ? x : y) & 15;
            if (macroblockEdge != (edge == 0))
            {
                throw new ArgumentException("The boundary fixture has inconsistent edge coordinates.");
            }
            Span<byte> values = stackalloc byte[16];
            try
            {
                context.FillStrengths(x / 16 * 16, y / 16 * 16, vertical, edge, 16, values);
            }
            finally
            {
                values.Slice(edge, 4).CopyTo(result);
            }
        }
    }
}
