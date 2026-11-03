// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264UniformRangeTestInput
    {
        public static void Verify()
        {
            MediaBufferAllocator allocator = new(65536);
            H264SequenceParameterSet sequence = new()
            {
                WidthInMbs = 3,
                HeightInMapUnits = 3,
                FrameMbsOnly = true,
                Width = 48,
                Height = 48
            };
            int cases = 0;
            using (H264Picture reference = new(sequence, allocator, H264SliceKind.I))
            {
                H264ReferencePicture sample = new(reference, 0, new H264PictureOrder(0, 0));
                foreach (H264SliceKind kind in new[] { H264SliceKind.P, H264SliceKind.B })
                {
                    using H264Picture picture = new(sequence, allocator, kind);
                    picture.Modes.Fill(H264Picture.InterMode);
                    picture.MotionX(H264ReferenceList.L0).Fill(int.MinValue);
                    picture.MotionY(H264ReferenceList.L0).Fill(int.MaxValue);
                    picture.CaptureReferences(0, [sample, null], []);
                    H264DeblockingContext context = new(picture, null, null);
                    context.BeginMacroblock(4);
                    for (int transform = 0; transform < 2; transform++)
                    {
                        picture.Transforms8x8.Fill((byte)transform);
                        foreach (int mask in new[] { 0, 1, 0x33, 0x3333, 0xcccc, 0xffff, 0x7fff, 0x9ace })
                        {
                            picture.Counts(0).Fill(1);
                            for (int cell = 0; cell < 16; cell++)
                            {
                                picture.Counts(0)[picture.BlockIndex(0, 16 + cell % 4 * 4, 16 + cell / 4 * 4)] =
                                    (mask & (1 << cell)) == 0 ? (byte)0 : (byte)255;
                            }
                            for (int invalid = 0; invalid < 2; invalid++)
                            {
                                picture.ReferenceIndices(H264ReferenceList.L0).Fill((byte)invalid);
                                foreach (bool vertical in new[] { false, true })
                                {
                                    foreach (int step in new[] { 4, 8, 16 })
                                    {
                                        for (int first = 0; first <= 16; first += 4)
                                        {
                                            Compare(picture, ref context, vertical, first, step);
                                            cases++;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            if (allocator.AllocatedBytes != 0 || cases != 1920)
            {
                throw new InvalidOperationException("Uniform internal-edge range coverage or media release is incomplete.");
            }
            Console.WriteLine($"PASS {cases} uniform range, partial-write and missing-reference cases.");
        }

        private static void Compare(H264Picture picture, ref H264DeblockingContext context, bool vertical, int first, int step)
        {
            Span<byte> actual = stackalloc byte[18];
            Span<byte> expected = stackalloc byte[16];
            actual.Fill(165);
            expected.Clear();
            H264DeblockingContext scalar = new(picture, null, null);
            scalar.BeginMacroblock(4);
            string? expectedError = null;
            try
            {
                for (int edge = first; edge < 16; edge += step)
                {
                    for (int lane = 0; lane < 4; lane++)
                    {
                        expected[edge + lane] = (byte)scalar.Strength(16 + (vertical ? edge : lane * 4),
                            16 + (vertical ? lane * 4 : edge), vertical, edge == 0);
                    }
                }
            }
            catch (InvalidDataException exception)
            {
                expectedError = exception.Message;
            }
            string? actualError = null;
            try
            {
                context.FillStrengths(16, 16, vertical, first, step, actual.Slice(1, 16));
            }
            catch (InvalidDataException exception)
            {
                actualError = exception.Message;
            }
            if (expectedError != actualError || !actual.Slice(1, 16).SequenceEqual(expected)
                || actual[0] != 165 || actual[17] != 165)
            {
                throw new InvalidOperationException("Uniform internal edges changed values, error priority, partial writes or guards.");
            }
        }
    }
}
