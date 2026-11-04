// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264UniformPredictionTestInput
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
            int checks = 0;
            using (H264Picture first = new(sequence, allocator, H264SliceKind.I))
            using (H264Picture second = new(sequence, allocator, H264SliceKind.I))
            {
                H264ReferencePicture a = new(first, 0, new H264PictureOrder(0, 0));
                H264ReferencePicture b = new(second, 1, new H264PictureOrder(2, 2));
                foreach (H264SliceKind kind in new[] { H264SliceKind.P, H264SliceKind.B })
                {
                    using H264Picture picture = new(sequence, allocator, kind);
                    for (int address = 0; address < 9; address++)
                    {
                        picture.CaptureReferences(address, address == 4 ? [b, a, a, null] : [a, b, a, null],
                            kind == H264SliceKind.B ? [a, b, a, null] : []);
                    }
                    foreach (int reference in new[] { -1, 0, 1, 2, 3, 31 })
                        foreach (int value in new[] { 0, -4, 3, 4, int.MinValue, int.MaxValue })
                            for (int transform = 0; transform < 2; transform++)
                            {
                                Initialize(picture, reference, value, transform);
                                Compare(picture);
                                checks++;
                                for (int cell = 0; cell < 16; cell++)
                                    for (int field = 0; field < (picture.HasBidirectionalMotion ? 6 : 3); field++)
                                    {
                                        Initialize(picture, reference, value, transform);
                                        int position = (4 + cell / 4) * 12 + 4 + cell % 4;
                                        H264ReferenceList list = field < 3 ? H264ReferenceList.L0 : H264ReferenceList.L1;
                                        switch (field % 3)
                                        {
                                            case 0:
                                                picture.ReferenceIndices(list)[position] = reference == 0 ? (byte)1 : (byte)0;
                                                break;
                                            case 1:
                                                picture.MotionX(list)[position] = value == int.MaxValue ? int.MinValue : value + 1;
                                                break;
                                            case 2:
                                                picture.MotionY(list)[position] = value == int.MinValue ? int.MaxValue : value - 1;
                                                break;
                                        }
                                        Compare(picture);
                                        picture.Counts(0)[position] = 1;
                                        Compare(picture);
                                        picture.Modes[position] = 2;
                                        Compare(picture);
                                        checks += 3;
                                    }
                            }
                }
            }
            if (allocator.AllocatedBytes != 0 || checks != 31248)
            {
                throw new InvalidOperationException($"Incomplete uniform prediction checks: {checks}.");
            }
            Console.WriteLine($"PASS {checks} uniform prediction and every-cell mutation patterns.");
        }

        private static void Initialize(H264Picture picture, int reference, int value, int transform)
        {
            picture.Modes.Fill(H264Picture.InterMode);
            picture.Counts(0).Clear();
            picture.Transforms8x8.Fill((byte)transform);
            picture.ReferenceIndices(H264ReferenceList.L0).Fill(reference < 0 ? byte.MaxValue : (byte)reference);
            picture.MotionX(H264ReferenceList.L0).Fill(value);
            picture.MotionY(H264ReferenceList.L0).Fill(value);
            if (picture.HasBidirectionalMotion)
            {
                picture.ReferenceIndices(H264ReferenceList.L1).Fill(reference < 0 ? (byte)0 : byte.MaxValue);
                picture.MotionX(H264ReferenceList.L1).Fill(value);
                picture.MotionY(H264ReferenceList.L1).Fill(value);
            }
        }

        private static void Fill(H264Picture picture, scoped Span<byte> result)
        {
            H264DeblockingContext context = new(picture, null, null);
            context.BeginMacroblock(4);
            int step = picture.Transforms8x8[4] == 0 ? 4 : 8;
            context.FillStrengths(16, 16, true, 0, step, result[..16]);
            context.FillStrengths(16, 16, false, 0, step, result[16..]);
        }

        private static void Compare(H264Picture picture)
        {
            Span<byte> expected = stackalloc byte[32];
            Span<byte> actual = stackalloc byte[36];
            int step = picture.Transforms8x8[4] == 0 ? 4 : 8;
            H264DeblockingContext scalar = new(picture, null, null);
            scalar.BeginMacroblock(4);
            expected.Clear();
            bool invalid = false;
            try
            {
                for (int direction = 0; direction < 2; direction++)
                    for (int edge = 0; edge < 16; edge += step)
                        for (int segment = 0; segment < 4; segment++)
                        {
                            bool vertical = direction == 0;
                            expected[direction * 16 + edge + segment] = (byte)scalar.Strength(
                                16 + (vertical ? edge : segment * 4), 16 + (vertical ? segment * 4 : edge), vertical, edge == 0);
                        }
            }
            catch (InvalidDataException)
            {
                invalid = true;
            }
            actual.Fill(165);
            bool rejected = false;
            try
            {
                Fill(picture, actual.Slice(2, 32));
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            if (invalid != rejected || (!invalid && !actual.Slice(2, 32).SequenceEqual(expected))
                || actual[0] != 165 || actual[1] != 165 || actual[34] != 165 || actual[35] != 165)
            {
                throw new InvalidOperationException("Uniform prediction changed strength, error priority or output guards.");
            }
        }
    }
}
