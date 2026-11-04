// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264UniformContextTestInput
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
            Span<byte> result = stackalloc byte[16];
            using (H264Picture reference = new(sequence, allocator, H264SliceKind.I))
            {
                H264ReferencePicture sample = new(reference, 0, new H264PictureOrder(0, 0));
                foreach (H264SliceKind kind in new[] { H264SliceKind.P, H264SliceKind.B })
                {
                    using H264Picture picture = new(sequence, allocator, kind);
                    picture.CaptureReferences(0, [sample, null], []);
                    picture.Modes.Fill(H264Picture.InterMode);
                    picture.ReferenceIndices(H264ReferenceList.L0).Clear();
                    H264DeblockingContext context = new(picture, null, null);
                    context.BeginMacroblock(4);
                    Compare(ref context, picture, 4, false);
                    picture.ReferenceIndices(H264ReferenceList.L0).Fill(1);
                    Compare(ref context, picture, 4, true);
                    picture.ReferenceIndices(H264ReferenceList.L0).Fill(byte.MaxValue);
                    Compare(ref context, picture, 4, true);
                    picture.Modes.Fill(2);
                    Compare(ref context, picture, 4, false);
                    picture.Modes.Fill(H264Picture.InterMode);
                    picture.Counts(0).Fill(1);
                    Compare(ref context, picture, 4, false);
                    picture.Counts(0).Clear();
                    picture.ReferenceIndices(H264ReferenceList.L0).Clear();
                    Compare(ref context, picture, 4, false);
                    picture.MotionX(H264ReferenceList.L0)[picture.BlockIndex(0, 20, 16)] = 4;
                    Compare(ref context, picture, 4, false);
                    picture.MotionX(H264ReferenceList.L0)[picture.BlockIndex(0, 20, 16)] = 0;
                    if (picture.HasBidirectionalMotion)
                    {
                        picture.MotionX(H264ReferenceList.L1).Fill(int.MaxValue);
                        picture.MotionY(H264ReferenceList.L1).Fill(int.MinValue);
                        Compare(ref context, picture, 4, false);
                    }
                    context.BeginMacroblock(8);
                    Compare(ref context, picture, 8, false);
                    for (int warm = 0; warm < 100; warm++)
                    {
                        context.FillStrengths(32, 32, true, 4, 4, result);
                    }
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int iteration = 0; iteration < 10000; iteration++)
                    {
                        context.FillStrengths(32, 32, true, 4, 4, result);
                    }
                    if (GC.GetAllocatedBytesForCurrentThread() != before || result.ContainsAnyExcept((byte)0))
                    {
                        throw new InvalidOperationException("Repeated uniform classification allocated or changed strength.");
                    }
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Mutable context verification retained media storage.");
            }
            Console.WriteLine("PASS mutable context, internal-only missing references and zero allocation.");
        }

        private static void Compare(ref H264DeblockingContext context, H264Picture picture, int address, bool invalid)
        {
            Span<byte> output = stackalloc byte[18];
            output.Fill(165);
            int x = address % picture.Sequence.WidthInMbs * 16;
            int y = address / picture.Sequence.WidthInMbs * 16;
            bool rejected = false;
            try
            {
                context.FillStrengths(x, y, true, 4, 4, output.Slice(1, 16));
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            if (rejected != invalid || output[0] != 165 || output[17] != 165)
            {
                throw new InvalidOperationException("Mutable context changed error priority or guards.");
            }
            if (invalid)
            {
                return;
            }
            H264DeblockingContext scalar = new(picture, null, null);
            scalar.BeginMacroblock(address);
            for (int edge = 4; edge < 16; edge += 4)
            {
                for (int segment = 0; segment < 4; segment++)
                {
                    if (output[1 + edge + segment] != scalar.Strength(x + edge, y + segment * 4, true, false))
                    {
                        throw new InvalidOperationException("Mutable context reused stale prediction metadata.");
                    }
                }
            }
        }
    }
}
