// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264CavlcWriter
    {
        public static int Write(H264BitWriter bits, ReadOnlySpan<int> coefficients, int neighborCount)
        {
            int maximum = coefficients.Length;
            if (maximum is not (4 or 15 or 16) || neighborCount < -1 || (neighborCount == -1 && maximum != 4))
            {
                throw new ArgumentException("The H.264 residual block configuration is invalid.");
            }
            Span<int> levels = stackalloc int[16];
            Span<int> positions = stackalloc int[16];
            int total = 0;
            for (int index = maximum - 1; index >= 0; index--)
            {
                int level = coefficients[index];
                if (level != 0)
                {
                    if (level is < -32768 or > 32767)
                    {
                        throw new ArgumentOutOfRangeException(nameof(coefficients));
                    }
                    levels[total] = level;
                    positions[total++] = index;
                }
            }
            int trailing = 0;
            while (trailing < Math.Min(3, total) && Math.Abs(levels[trailing]) == 1)
            {
                trailing++;
            }
            WriteToken(bits, neighborCount, total, trailing);
            if (total == 0)
            {
                return 0;
            }
            for (int index = 0; index < trailing; index++)
            {
                bits.WriteFlag(levels[index] < 0);
            }
            int suffixLength = total > 10 && trailing < 3 ? 1 : 0;
            for (int index = trailing; index < total; index++)
            {
                int level = levels[index];
                int code = level > 0 ? level * 2 - 2 : -level * 2 - 1;
                if (index == trailing && trailing < 3)
                {
                    code -= 2;
                }
                WriteLevel(bits, code, suffixLength);
                if (suffixLength == 0)
                {
                    suffixLength = 1;
                }
                if (suffixLength < 6 && Math.Abs(level) > (3 << (suffixLength - 1)))
                {
                    suffixLength++;
                }
            }
            int zeros = positions[0] + 1 - total;
            if (total < maximum)
            {
                H264Vlc table = maximum == 4 ? H264CavlcTables.ChromaZeros[total - 1] : H264CavlcTables.TotalZeros[total - 1];
                table.Write(bits, zeros);
            }
            for (int index = 0; index < total - 1 && zeros != 0; index++)
            {
                int run = positions[index] - positions[index + 1] - 1;
                H264CavlcTables.RunBefore[Math.Min(zeros, 7) - 1].Write(bits, run);
                zeros -= run;
            }
            return total;
        }

        private static void WriteToken(H264BitWriter bits, int neighborCount, int total, int trailing)
        {
            if (neighborCount >= 8)
            {
                bits.WriteBits((uint)(total == 0 ? 3 : ((total - 1) << 2) | trailing), 6);
                return;
            }
            int table = neighborCount switch { -1 => 3, < 2 => 0, < 4 => 1, _ => 2 };
            H264CavlcTables.CoefficientToken[table].Write(bits, (total << 2) | trailing);
        }

        private static void WriteLevel(H264BitWriter bits, int code, int suffixLength)
        {
            for (int prefix = 0; prefix <= 19; prefix++)
            {
                int suffixBits = prefix switch { 14 when suffixLength == 0 => 4, >= 15 => prefix - 3, _ => suffixLength };
                int basis = Math.Min(15, prefix) << suffixLength;
                if (prefix >= 15 && suffixLength == 0)
                {
                    basis += 15;
                }
                if (prefix >= 16)
                {
                    basis += (1 << (prefix - 3)) - 4096;
                }
                int suffix = code - basis;
                if (suffix >= 0 && suffix < (1 << suffixBits))
                {
                    bits.WriteBits(0, prefix);
                    bits.WriteFlag(true);
                    bits.WriteBits((uint)suffix, suffixBits);
                    return;
                }
            }
            throw new ArgumentOutOfRangeException(nameof(code));
        }
    }
}
