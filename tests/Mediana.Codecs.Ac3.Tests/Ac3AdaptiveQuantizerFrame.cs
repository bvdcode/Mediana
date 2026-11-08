// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Ac3.Tests
{
    internal static class Ac3AdaptiveQuantizerFrame
    {
        public static byte[] CreateSequence()
        {
            ReadOnlySpan<(int Coarse, int Fine, int Mode)> settings =
                [(7, 8, 0), (8, 0, 0), (9, 8, 0), (9, 0, 0), (12, 8, 1), (15, 8, 3), (19, 8, 2),
                 (23, 8, 3), (27, 8, 3), (31, 8, 0), (12, 8, 0), (15, 8, 1), (12, 8, 2), (12, 8, 3)];
            byte[] result = new byte[2048 * settings.Length];
            for (int frame = 0; frame < settings.Length; frame++)
            {
                (int coarse, int fine, int mode) = settings[frame];
                Create(coarse, fine, mode, frame).CopyTo(result, frame * 2048);
            }
            return result;
        }

        private static byte[] Create(int coarse, int fine, int mode, int frame)
        {
            byte[] exponents = new byte[49], bap = new byte[49];
            exponents.AsSpan().Fill(8);
            Ac3BitAllocation.Compute(exponents, bap, 0, 49, 0, 2, 1, 1, 2, 7,
                (((coarse - 15) << 4) + fine) << 2, 4, 0, 0, new int[50], true);
            Assert.DoesNotContain((byte)0, bap);
            byte[] gains = new byte[49];
            int end = mode < 2 ? 12 : 17;
            for (int bin = 0; bin < bap.Length; bin++)
            {
                if (mode != 0 && bap[bin] >= 8 && bap[bin] < end)
                { gains[bin] = (byte)(mode == 3 ? bin % 3 : bin % 2 * mode); }
            }
            Ac3TestBitWriter writer = new(2048);
            writer.Write(0xb77, 16); writer.Write(0, 2); writer.Write(0, 3); writer.Write(1023, 11);
            writer.Write(0, 2); writer.Write(3, 2); writer.Write(1, 3); writer.Write(0, 1); writer.Write(16, 5);
            writer.Write(31, 5); writer.Write(0, 4);
            writer.Write(1, 1); writer.Write(1, 1); writer.Write(0, 2); writer.Write(0, 1);
            writer.Write(0, 1); writer.Write(1, 1); writer.Write(0, 5);
            for (int block = 0; block < 6; block++) { writer.Write(block == 0 ? 1 : 0, 2); }
            writer.Write(0, 5); writer.Write(1, 1); writer.Write(coarse, 6); writer.Write(fine, 4); writer.Write(0, 1);
            for (int block = 0; block < 6; block++)
            {
                writer.Write(0, 2);
                if (block == 0)
                {
                    Ac3SpectralFrame.WriteStrategy(writer, false);
                    writer.Write(31, 5); writer.Write(0, 2);
                    for (int band = 0; band < 3; band++) { writer.Write(15, 4); writer.Write(0, 2); }
                    writer.Write(8, 4);
                    for (int group = 0; group < 16; group++) { writer.Write(62, 7); }
                    writer.Write(0, 2);
                }
                else { writer.Write(0, 2); }
                writer.Write(0, 1);
                if (block == 0) { WriteMantissas(writer, bap, gains, mode, frame); }
            }
            return writer.Finish(true);
        }

        private static void WriteMantissas(Ac3TestBitWriter writer, byte[] allocation, byte[] gains, int mode, int frame)
        {
            writer.Write(mode, 2);
            int end = mode < 2 ? 12 : 17;
            int[] active = Enumerable.Range(0, allocation.Length).Where(bin => allocation[bin] >= 8 && allocation[bin] < end)
                .Select(bin => (int)gains[bin]).ToArray();
            if (mode is 1 or 2) { foreach (int gain in active) { writer.Write(gain / mode, 1); } }
            else if (mode == 3)
            {
                for (int index = 0; index < active.Length; index += 3)
                {
                    int group = active[index] * 9;
                    if (index + 1 < active.Length) { group += active[index + 1] * 3; }
                    if (index + 2 < active.Length) { group += active[index + 2]; }
                    writer.Write(group, 5);
                }
            }
            for (int bin = 0; bin < allocation.Length; bin++)
            {
                int width = Ac3AhtQuantizer.Bits(allocation[bin]);
                if (allocation[bin] < 8) { writer.Write((bin * 13 + frame * 7) & ((1 << width) - 1), width); continue; }
                int gain = gains[bin], small = width - gain;
                for (int item = 0; item < 6; item++)
                {
                    if (gain == 0)
                    {
                        int value = item switch { 0 => 0, 1 => 1, 2 => -1, 3 => (1 << (width - 1)) - 1, 4 => -(1 << (width - 1)) + 1, 5 => 2, _ => throw new InvalidOperationException() };
                        writer.Write(value, width);
                    }
                    else if (item < 3)
                    { writer.Write(item switch { 0 => 0, 1 => (1 << (small - 1)) - 1, 2 => -(1 << (small - 1)) + 1, _ => throw new InvalidOperationException() }, small); }
                    else
                    {
                        writer.Write(-(1 << (small - 1)), small);
                        int large = gain == 1 ? width - 1 : width;
                        writer.Write(item switch { 3 => 0, 4 => (1 << (large - 1)) - 1, 5 => -(1 << (large - 1)), _ => throw new InvalidOperationException() }, large);
                    }
                }
            }
        }
    }
}
