// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264Cavlc
    {
        public static int Read(ref H264BitReader bits, scoped Span<int> coefficients, int neighborCount,
            int maximumPrefix = 19)
        {
            int maximum = coefficients.Length;
            if (maximum is not (4 or 15 or 16) || neighborCount < -1 ||
                (neighborCount == -1 && maximum != 4))
            {
                throw new ArgumentException("The H.264 residual block configuration is invalid.");
            }
            coefficients.Clear();
            int token = ReadToken(ref bits, neighborCount);
            int total = token >> 2;
            int trailing = token & 3;
            if (total > maximum || trailing > total)
            {
                throw new InvalidDataException("The H.264 coefficient count exceeds the residual block size.");
            }
            if (total == 0)
            {
                return 0;
            }
            Span<int> levels = stackalloc int[16];
            Span<int> runs = stackalloc int[16];
            for (int index = 0; index < trailing; index++)
            {
                levels[index] = bits.ReadFlag() ? -1 : 1;
            }
            int suffixLength = total > 10 && trailing < 3 ? 1 : 0;
            for (int index = trailing; index < total; index++)
            {
                int prefix = 0;
                while (bits.ReadBit() == 0)
                {
                    if (++prefix > maximumPrefix)
                    {
                        throw new InvalidDataException("The H.264 coefficient level prefix exceeds the permitted range.");
                    }
                }
                int suffixSize = prefix switch
                {
                    14 when suffixLength == 0 => 4,
                    >= 15 => prefix - 3,
                    _ => suffixLength
                };
                int levelCode = (Math.Min(15, prefix) << suffixLength) + (int)bits.ReadBits(suffixSize);
                if (prefix >= 15 && suffixLength == 0)
                {
                    levelCode += 15;
                }
                if (prefix >= 16)
                {
                    levelCode += (1 << (prefix - 3)) - 4096;
                }
                if (index == trailing && trailing < 3)
                {
                    levelCode += 2;
                }
                int level = (levelCode & 1) == 0 ? (levelCode + 2) >> 1 : (-levelCode - 1) >> 1;
                levels[index] = level;
                if (suffixLength == 0)
                {
                    suffixLength = 1;
                }
                if (suffixLength < 6 && Math.Abs(level) > (3 << (suffixLength - 1)))
                {
                    suffixLength++;
                }
            }
            int zeros = 0;
            if (total < maximum)
            {
                zeros = maximum == 4
                    ? H264CavlcTables.ChromaZeros[total - 1].Read(ref bits)
                    : H264CavlcTables.TotalZeros[total - 1].Read(ref bits);
                if (zeros > maximum - total)
                {
                    throw new InvalidDataException("The H.264 zero run exceeds the residual block size.");
                }
            }
            for (int index = 0; index < total - 1; index++)
            {
                int run = zeros == 0 ? 0 : H264CavlcTables.RunBefore[Math.Min(zeros, 7) - 1].Read(ref bits);
                if (run > zeros)
                {
                    throw new InvalidDataException("The H.264 coefficient run exceeds the remaining zeros.");
                }
                runs[index] = run;
                zeros -= run;
            }
            runs[total - 1] = zeros;
            int position = -1;
            for (int index = total - 1; index >= 0; index--)
            {
                position += runs[index] + 1;
                coefficients[position] = levels[index];
            }
            return total;
        }

        private static int ReadToken(ref H264BitReader bits, int neighborCount)
        {
            if (neighborCount >= 8)
            {
                int code = (int)bits.ReadBits(6);
                if (code == 3)
                {
                    return 0;
                }
                int total = (code >> 2) + 1;
                int trailing = code & 3;
                return (total << 2) | trailing;
            }
            int table = neighborCount switch
            {
                -1 => 3,
                < 2 => 0,
                < 4 => 1,
                _ => 2
            };
            return H264CavlcTables.CoefficientToken[table].Read(ref bits);
        }
    }
}
