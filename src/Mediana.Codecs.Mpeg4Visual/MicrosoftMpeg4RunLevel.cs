// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual
{
    internal class MicrosoftMpeg4RunLevel
    {
        private readonly Mpeg4CodeTable codes;
        private readonly byte[] runs;
        private readonly byte[] levels;
        private readonly byte[] maximumLevels;
        private readonly byte[] maximumRuns;

        internal MicrosoftMpeg4RunLevel(Mpeg4CodeTable codes, ReadOnlySpan<byte> runs, ReadOnlySpan<byte> levels)
        {
            this.codes = codes;
            this.runs = runs.ToArray();
            this.levels = levels.ToArray();
            maximumLevels = Mpeg4Codebooks.Limits(runs, levels, byRun: true);
            maximumRuns = Mpeg4Codebooks.Limits(runs, levels, byRun: false);
        }

        internal int Read(ref Mpeg4VisualBitReader bits, bool wmv2, bool intra, int quantizer,
            ref int escapeRunBits, ref int escapeLevelBits, out int run, out bool last)
        {
            int symbol = codes.Read(ref bits);
            int escape = 0;
            if (symbol == runs.Length)
            {
                escape = bits.Flag() ? 1 : bits.Flag() ? 2 : 3;
                if (escape == 3)
                {
                    last = bits.Flag();
                    if (!wmv2)
                    {
                        run = bits.Read(6);
                        return unchecked((sbyte)bits.Read(8));
                    }
                    if (escapeLevelBits == 0)
                    {
                        escapeLevelBits = ReadEscapeLevelBits(ref bits, quantizer);
                        escapeRunBits = bits.Read(2) + 3;
                    }
                    run = bits.Read(escapeRunBits);
                    bool negative = bits.Flag();
                    int level = bits.Read(escapeLevelBits);
                    return negative ? -level : level;
                }
                symbol = codes.Read(ref bits);
                if (symbol == runs.Length)
                {
                    throw new InvalidDataException("A Microsoft MPEG-4 coefficient escape contains another escape.");
                }
            }
            int runCode = runs[symbol];
            last = (runCode & 128) != 0;
            run = runCode & 127;
            int magnitude = levels[symbol];
            int half = last ? 64 : 0;
            if (escape == 1)
            {
                magnitude += maximumLevels[half + run];
            }
            else if (escape == 2)
            {
                run += maximumRuns[half + magnitude] + (wmv2 || !intra ? 1 : 0);
            }
            return bits.Flag() ? -magnitude : magnitude;
        }

        private static int ReadEscapeLevelBits(ref Mpeg4VisualBitReader bits, int quantizer)
        {
            if (quantizer < 8)
            {
                int length = bits.Read(3);
                return length != 0 ? length : 8 + bits.Read(1);
            }
            int current = 2;
            while (current < 8 && !bits.Flag())
            {
                current++;
            }
            return current;
        }
    }
}
