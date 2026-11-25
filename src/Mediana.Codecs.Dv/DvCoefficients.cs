// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Dv
{
    internal static class DvCoefficients
    {
        private const int PrefixLength = 16;
        private const int BlockCoefficients = 64;
        private const int QuantizerCount = 22;
        private const int FractionBits = 14;
        private const int EndOfBlockRun = 127;
        private const int NegativeFlag = 32;
        private static readonly ushort[] Prefixes = BuildPrefixes();
        private static readonly int[] Factors = BuildFactors();

        private static ReadOnlySpan<byte> ClassOffset => [6, 3, 0, 1];

        internal static void ReadBlock(scoped ref DvBitReader bits, int quantizer, scoped Span<int> values,
            out DvBlockState state)
        {
            int dc = bits.Read(9);
            if (dc >= 256)
            {
                dc -= 512;
            }
            values[0] = 1024 + 4 * dc;
            bool field = bits.Read(1) == 1;
            int coefficientClass = bits.Read(2);
            int factorGroup = (field ? 1 : 0) + (coefficientClass == 3 ? 2 : 0);
            state = new()
            {
                IsFieldTransform = field,
                FactorOffset = (factorGroup * QuantizerCount + quantizer + ClassOffset[coefficientClass]) * BlockCoefficients
            };
            ReadAc(ref bits, ref state, values);
        }

        internal static void ReadAc(scoped ref DvBitReader bits, ref DvBlockState state, scoped Span<int> values)
        {
            ReadOnlySpan<byte> scan = state.IsFieldTransform ? DvTables.FieldScan : DvTables.FrameScan;
            ReadOnlySpan<int> factors = Factors.AsSpan(state.FactorOffset, BlockCoefficients);
            while (bits.Remaining > 0)
            {
                int available = Math.Min(bits.Remaining, PrefixLength - state.PartialLength);
                int candidate = (state.PartialCode << available) | bits.Peek(available);
                int total = state.PartialLength + available;
                int entry = Prefixes[candidate << (PrefixLength - total)];
                int codeLength = entry & 31;
                if (codeLength > total)
                {
                    state.PartialCode = candidate;
                    state.PartialLength = total;
                    bits.Skip(available);
                    return;
                }
                bits.Skip(codeLength - state.PartialLength);
                state.PartialLength = 0;
                state.PartialCode = 0;

                int symbol = entry >> 6;
                int run = DvTables.Runs[symbol];
                if (run == EndOfBlockRun)
                {
                    state.Complete = true;
                    return;
                }
                state.Coefficient += run + 1;
                if (state.Coefficient >= BlockCoefficients)
                {
                    throw new InvalidDataException("A DV coefficient run exceeds its transform block.");
                }
                int level = DvTables.Levels[symbol];
                if ((entry & NegativeFlag) != 0)
                {
                    level = -level;
                }
                values[scan[state.Coefficient]] = (factors[state.Coefficient] * level + (1 << (FractionBits - 1))) >> FractionBits;
            }
        }

        private static ushort[] BuildPrefixes()
        {
            ushort[] table = new ushort[1 << PrefixLength];
            int next = 0;
            ReadOnlySpan<byte> lengths = DvTables.CodeLengths;
            ReadOnlySpan<byte> levels = DvTables.Levels;
            for (int symbol = 0; symbol < lengths.Length; symbol++)
            {
                bool signed = levels[symbol] != 0;
                int length = lengths[symbol] + (signed ? 1 : 0);
                int copies = 1 << (PrefixLength - length);
                int entry = (symbol << 6) | length;
                table.AsSpan(next, copies).Fill((ushort)entry);
                next += copies;
                if (signed)
                {
                    table.AsSpan(next, copies).Fill((ushort)(entry | NegativeFlag));
                    next += copies;
                }
            }
            if (next != table.Length)
            {
                throw new InvalidOperationException("The DV coefficient codebook must cover all prefixes.");
            }
            return table;
        }

        private static int[] BuildFactors()
        {
            int[] result = new int[4 * QuantizerCount * BlockCoefficients];
            for (int group = 0; group < 4; group++)
            {
                ReadOnlySpan<ushort> weights = (group & 1) == 0 ? DvTables.FrameWeights : DvTables.FieldWeights;
                for (int quantizer = 0; quantizer < QuantizerCount; quantizer++)
                {
                    int offset = (group * QuantizerCount + quantizer) * BlockCoefficients;
                    for (int position = 0; position < BlockCoefficients; position++)
                    {
                        int area = position switch { < 6 => 0, < 21 => 1, < 43 => 2, _ => 3 };
                        int shift = DvTables.QuantizerShifts[4 * quantizer + area] + 1 + (group >> 1);
                        result[offset + position] = weights[position] << shift;
                    }
                }
            }
            return result;
        }
    }
}
