// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    internal static class H264VlcTestInput
    {
        public static void Verify()
        {
            string[] codes = new string[33];
            int[] symbols = new int[codes.Length];
            for (int index = 0; index < codes.Length - 1; index++) { codes[index] = new string('0', index) + "1"; }
            codes[^1] = new string('0', 32);
            for (int index = 0; index < symbols.Length; index++) { symbols[index] = 3 * index + 2; }
            H264Vlc table = new(codes, symbols);
            for (int alignment = 0; alignment < 8; alignment++)
            {
                for (int index = 0; index < codes.Length; index++)
                {
                    H264BitReader bits = new(H264TestBits.Rbsp(new string('1', alignment) + codes[index] + "10110101", false));
                    bits.ReadBits(alignment);
                    Require(table.Read(ref bits) == symbols[index]);
                    Require(bits.BitPosition == alignment + codes[index].Length);
                    Require(bits.ReadBits(8) == 181);
                }
            }
            List<int> selected = [];
            System.Text.StringBuilder sequence = new();
            for (int index = 0; index < 2048; index++)
            {
                int code = index * 17 % codes.Length;
                selected.Add(code);
                sequence.Append(codes[code]);
            }
            H264BitReader mixed = new(H264TestBits.Rbsp(sequence.ToString(), false));
            int position = 0;
            foreach (int code in selected)
            {
                Require(table.Read(ref mixed) == symbols[code]);
                position += codes[code].Length;
                Require(mixed.BitPosition == position);
            }
            H264BitReader last = new([1]);
            last.ReadBits(7);
            Require(table.Read(ref last) == symbols[0] && last.BitPosition == 8);
            VerifyRejection(new(["10", "11"]), [0], 1);
            VerifyRejection(new([new string('0', 33) + "1"]), H264TestBits.Rbsp(new string('0', 32), false), 32);
            VerifyAllocation(table, symbols[0]);
        }

        private static void VerifyAllocation(H264Vlc table, int symbol)
        {
            byte[] source = new byte[32];
            Array.Fill(source, byte.MaxValue);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int repeat = 0; repeat < 64; repeat++)
            {
                H264BitReader bits = new(source);
                for (int index = 0; index < source.Length * 8; index++) { Require(table.Read(ref bits) == symbol); }
            }
            Require(GC.GetAllocatedBytesForCurrentThread() == before);
        }

        private static void VerifyRejection(H264Vlc table, byte[] source, int position)
        {
            H264BitReader bits = new(source);
            bool rejected = false;
            try { table.Read(ref bits); }
            catch (InvalidDataException) { rejected = true; }
            Require(rejected && bits.BitPosition == position);
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("H.264 VLC reading changed symbols, consumption, rejection or allocation."); }
        }
    }
}
