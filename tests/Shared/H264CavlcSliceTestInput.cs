// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    internal static class H264CavlcSliceTestInput
    {
        public static void Verify()
        {
            MediaBufferAllocator allocator = new(4096);
            foreach (H264SliceKind kind in new[] { H264SliceKind.P, H264SliceKind.B })
            {
                VerifyPattern(allocator, kind, true, new bool[12], [true, false, true, false, true, false]);
                VerifyPattern(allocator, kind, true, Enumerable.Repeat(true, 12).ToArray(), new bool[6]);
                VerifyPattern(allocator, kind, true, Enumerable.Range(0, 12).Select(index => index % 2 == 0).ToArray(),
                    [true, false, true, false, true, false]);
                VerifyPattern(allocator, kind, true, Enumerable.Range(0, 12).Select(index => index % 2 != 0).ToArray(),
                    [true, false, true, false, true, false]);
                VerifyPattern(allocator, kind, true,
                    [false, false, true, true, false, false, true, true, true, true, false, false],
                    [true, true, false, true, true, false]);
                VerifyPattern(allocator, kind, true,
                    [false, false, false, false, false, false, false, false, true, true, false, false],
                    [true, true, false, false, false, false]);
                VerifyPattern(allocator, kind, true,
                    [true, true, true, true, true, true, true, true, true, true, false, true],
                    [false, false, false, false, false, false], 6);
                VerifyPattern(allocator, kind, false,
                    [false, true, true, false, true, false, false, true, true, true, true, false], new bool[6]);
            }
            VerifyPattern(allocator, H264SliceKind.I, true, new bool[12], [true, false, true, false, true, false]);
            VerifyPattern(allocator, H264SliceKind.I, false, new bool[12], new bool[6]);
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("CAVLC slice validation retained bitstream storage.");
            }
        }

        private static void VerifyPattern(MediaBufferAllocator allocator, H264SliceKind kind, bool adaptive,
            bool[] skipped, bool[] fieldPairs, int first = 0)
        {
            using H264BitWriter writer = new(allocator, 512, 0x41);
            int encoded = first;
            while (encoded < skipped.Length)
            {
                int run = 0;
                if (kind != H264SliceKind.I)
                {
                    while (encoded + run < skipped.Length && skipped[encoded + run])
                    {
                        run++;
                    }
                    writer.WriteUnsigned((uint)run);
                    encoded += run;
                    if (encoded == skipped.Length)
                    {
                        break;
                    }
                }
                if (adaptive && ((encoded & 1) == 0 || run > 0))
                {
                    writer.WriteFlag(fieldPairs[encoded / 2]);
                }
                writer.WriteUnsigned((uint)encoded);
                encoded++;
            }
            using MediaBuffer nal = writer.Finish();
            H264BitReader bits = new(nal.Memory.Span[1..]);
            H264SequenceParameterSet sequence = new()
            {
                WidthInMbs = 3,
                HeightInMapUnits = adaptive ? 2 : 4,
                FrameMbsOnly = !adaptive,
                MbAdaptiveFrameField = adaptive
            };
            H264PictureLayout layout = new(sequence, H264PictureStructure.Frame);
            H264MacroblockGrid grid = new(layout, 3, new byte[adaptive ? 6 : 0]);
            if (first > 0)
            {
                for (int pair = 0; pair < first; pair += 2)
                {
                    grid.SetFieldPair(pair, true);
                }
            }
            H264CavlcSliceCursor cursor = new(layout, kind, first, skipped.Length);
            int expected = first;
            while (cursor.MoveNext(ref bits, grid, out int address, out bool skip))
            {
                if (address != expected || skip != skipped[address] ||
                    adaptive && grid.IsField(address) != fieldPairs[address / 2])
                {
                    throw new InvalidDataException($"CAVLC slice control differs at macroblock {address}.");
                }
                if (!skip && bits.ReadUe(skipped.Length) != address)
                {
                    throw new InvalidDataException("CAVLC control consumed macroblock payload bits.");
                }
                expected++;
            }
            if (expected != skipped.Length)
            {
                throw new InvalidDataException("CAVLC slice control omitted macroblocks.");
            }
            bits.ReadTrailingBits();
        }
    }
}
