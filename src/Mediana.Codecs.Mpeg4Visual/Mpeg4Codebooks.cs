// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual
{
    internal static class Mpeg4Codebooks
    {
        internal static readonly Mpeg4CodeTable IntraMacro = new([1, 1, 2, 3, 1, 1, 2, 3, 1], [1, 3, 3, 3, 4, 6, 6, 6, 9]);
        internal static readonly Mpeg4CodeTable InterMacro = new([1, 3, 2, 5, 3, 4, 3, 3, 3, 7, 6, 5, 4, 4, 3, 2, 2, 5, 4, 5, 1, 0, 0, 0, 2, 12, 14, 15], [1, 4, 4, 6, 5, 8, 8, 7, 3, 7, 7, 9, 6, 9, 9, 9, 3, 7, 7, 8, 9, 0, 0, 0, 11, 13, 13, 13]);
        internal static readonly Mpeg4CodeTable Cbpy = new([3, 5, 4, 9, 3, 7, 2, 11, 2, 3, 5, 10, 4, 8, 6, 3], [4, 5, 5, 4, 5, 4, 6, 4, 5, 6, 4, 4, 4, 4, 4, 2]);
        internal static readonly Mpeg4CodeTable Motion = new([1, 1, 1, 1, 3, 5, 4, 3, 11, 10, 9, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 7, 6, 5, 4, 3, 2, 3, 2], [1, 2, 3, 4, 6, 7, 7, 7, 9, 9, 9, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 11, 11, 11, 11, 11, 11, 12, 12]);
        internal static readonly Mpeg4CodeTable DcLuma = new([3, 3, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1], [3, 2, 2, 3, 3, 4, 5, 6, 7, 8, 9, 10, 11]);
        internal static readonly Mpeg4CodeTable DcChroma = new([3, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1], [2, 2, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
        internal static readonly Mpeg4CodeTable IntraCoefficients = new([2, 6, 15, 13, 12, 21, 19, 18, 23, 31, 30, 29, 37, 36, 35, 33, 33, 32, 15, 14, 7, 6, 32, 33, 80, 81, 82, 14, 20, 22, 28, 32, 31, 13, 34, 83, 85, 11, 21, 30, 12, 86, 17, 27, 29, 11, 16, 34, 10, 13, 28, 8, 18, 27, 84, 20, 26, 87, 25, 9, 24, 35, 23, 25, 24, 7, 88, 7, 12, 22, 23, 6, 5, 4, 89, 15, 22, 5, 14, 4, 17, 36, 16, 37, 19, 90, 21, 91, 20, 19, 26, 21, 20, 19, 18, 17, 38, 39, 92, 93, 94, 95, 3], [2, 3, 4, 5, 5, 6, 6, 6, 7, 8, 8, 8, 9, 9, 9, 9, 10, 10, 10, 10, 11, 11, 11, 11, 12, 12, 12, 4, 6, 7, 8, 9, 9, 10, 11, 12, 12, 5, 7, 9, 10, 12, 6, 8, 9, 10, 6, 9, 10, 6, 9, 10, 7, 9, 12, 7, 9, 12, 8, 10, 8, 11, 8, 9, 9, 10, 12, 4, 6, 8, 9, 10, 11, 11, 12, 6, 9, 10, 6, 10, 7, 11, 7, 11, 7, 12, 8, 12, 8, 8, 8, 9, 9, 9, 9, 9, 11, 11, 12, 12, 12, 12, 7]);
        internal static readonly Mpeg4CodeTable InterCoefficients = new([2, 15, 21, 23, 31, 37, 36, 33, 32, 7, 6, 32, 6, 20, 30, 15, 33, 80, 14, 29, 14, 81, 13, 35, 13, 12, 34, 82, 11, 12, 83, 19, 11, 84, 18, 10, 17, 9, 16, 8, 22, 85, 21, 20, 28, 27, 33, 32, 31, 30, 29, 28, 27, 26, 34, 35, 86, 87, 7, 25, 5, 15, 4, 14, 13, 12, 19, 18, 17, 16, 26, 25, 24, 23, 22, 21, 20, 19, 24, 23, 22, 21, 20, 19, 18, 17, 7, 6, 5, 4, 36, 37, 38, 39, 88, 89, 90, 91, 92, 93, 94, 95, 3], [2, 4, 6, 7, 8, 9, 9, 10, 10, 11, 11, 11, 3, 6, 8, 10, 11, 12, 4, 8, 10, 12, 5, 9, 10, 5, 9, 12, 5, 10, 12, 6, 10, 12, 6, 10, 6, 10, 6, 10, 7, 12, 7, 7, 8, 8, 9, 9, 9, 9, 9, 9, 9, 9, 11, 11, 12, 12, 4, 9, 11, 6, 11, 6, 6, 6, 7, 7, 7, 7, 8, 8, 8, 8, 8, 8, 8, 8, 9, 9, 9, 9, 9, 9, 9, 9, 10, 10, 10, 10, 11, 11, 11, 11, 12, 12, 12, 12, 12, 12, 12, 12, 7]);
        private static ReadOnlySpan<byte> IntraRun => [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 5, 5, 5, 6, 6, 6, 7, 7, 7, 8, 8, 9, 9, 10, 11, 12, 13, 14, 128, 128, 128, 128, 128, 128, 128, 128, 129, 129, 129, 130, 130, 131, 131, 132, 132, 133, 133, 134, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148];
        private static ReadOnlySpan<byte> IntraLevel => [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 1, 2, 3, 4, 5, 1, 2, 3, 4, 1, 2, 3, 1, 2, 3, 1, 2, 3, 1, 2, 3, 1, 2, 1, 2, 1, 1, 1, 1, 1, 1, 2, 3, 4, 5, 6, 7, 8, 1, 2, 3, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1];
        private static ReadOnlySpan<byte> InterRun => [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 6, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 128, 128, 128, 129, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 165, 166, 167, 168];
        private static ReadOnlySpan<byte> InterLevel => [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 1, 2, 3, 4, 5, 6, 1, 2, 3, 4, 1, 2, 3, 1, 2, 3, 1, 2, 3, 1, 2, 3, 1, 2, 1, 2, 1, 2, 1, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 3, 1, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1];
        private static readonly byte[] IntraMaximumLevel = Limits(IntraRun, IntraLevel, true);
        private static readonly byte[] InterMaximumLevel = Limits(InterRun, InterLevel, true);
        private static readonly byte[] IntraMaximumRun = Limits(IntraRun, IntraLevel, false);
        private static readonly byte[] InterMaximumRun = Limits(InterRun, InterLevel, false);
        private static byte[] Limits(ReadOnlySpan<byte> runs, ReadOnlySpan<byte> levels, bool byRun)
        {
            byte[] result = new byte[128];
            for (int i = 0; i < runs.Length; i++)
            {
                int last = runs[i] >> 7, run = runs[i] & 127, level = levels[i];
                int index = last * 64 + (byRun ? run : level), value = byRun ? level : run;
                result[index] = Math.Max(result[index], (byte)value);
            }
            return result;
        }
        internal static int Coefficient(ref Mpeg4VisualBitReader bits, bool intra)
        {
            Mpeg4CodeTable table = intra ? IntraCoefficients : InterCoefficients;
            ReadOnlySpan<byte> runs = intra ? IntraRun : InterRun, levels = intra ? IntraLevel : InterLevel;
            int symbol = table.Read(ref bits), escape = 0;
            if (symbol == 102)
            {
                if (!bits.Flag()) { escape = 1; }
                else if (!bits.Flag()) { escape = 2; }
                else
                {
                    int last = bits.Read(1), run = bits.Read(6); bits.Marker();
                    int level = bits.Read(12); if ((level & 2048) != 0) { level -= 4096; }
                    bits.Marker();
                    if (level is 0 or -2048) { throw new InvalidDataException("The MPEG-4 Visual fixed escape level is forbidden."); }
                    return (last << 24) | (run << 16) | (level & 65535);
                }
                symbol = table.Read(ref bits);
                if (symbol == 102) { throw new InvalidDataException("An MPEG-4 Visual escape cannot contain another escape."); }
            }
            int runCode = runs[symbol], magnitude = levels[symbol];
            if (escape == 1)
            {
                magnitude += (intra ? IntraMaximumLevel : InterMaximumLevel)[(runCode >> 7) * 64 + (runCode & 127)];
            }
            else if (escape == 2)
            { runCode += (intra ? IntraMaximumRun : InterMaximumRun)[(runCode >> 7) * 64 + magnitude] + 1; }
            int signed = bits.Flag() ? -magnitude : magnitude;
            return ((runCode >> 7) << 24) | ((runCode & 127) << 16) | (signed & 65535);
        }
    }
}
