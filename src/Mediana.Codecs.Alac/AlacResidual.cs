// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Alac
{
    internal static class AlacResidual
    {
        public static void Read(ref AlacBitReader bits, Span<int> samples, AlacConfiguration configuration,
            int precision, int factor, CancellationToken token)
        {
            uint history = (uint)configuration.InitialHistory;
            uint multiplier = (uint)(configuration.HistoryMultiplier * factor / 4);
            uint signModifier = 0;
            for (int index = 0; index < samples.Length; index++)
            {
                if ((index & 255) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                int k = Math.Min(BitOperations.Log2((history >> 9) + 3), configuration.MaximumRiceParameter);
                uint raw = ReadValue(ref bits, k, precision, (1u << k) - 1);
                uint value = unchecked(raw + signModifier);
                samples[index] = unchecked((int)(value >> 1) ^ -(int)(value & 1));
                history = raw > 65535 ? 65535 : unchecked(history + value * multiplier - ((history * multiplier) >> 9));
                signModifier = 0;
                if (history < 128 && index + 1 < samples.Length)
                {
                    int runK = BitOperations.LeadingZeroCount(history) - 24 + (int)((history + 16) >> 6);
                    uint run = ReadValue(ref bits, runK, 16, ((1u << runK) - 1) & ((1u << configuration.MaximumRiceParameter) - 1));
                    if (run > samples.Length - index - 1)
                    {
                        throw new InvalidDataException("The ALAC zero run exceeds the frame.");
                    }
                    samples.Slice(index + 1, (int)run).Clear();
                    index += (int)run;
                    signModifier = run < 65535 ? 1u : 0;
                    history = 0;
                }
            }
        }

        private static uint ReadValue(ref AlacBitReader bits, int k, int escapeBits, uint divisor)
        {
            int prefix = 0;
            while (prefix < 9 && bits.Read(1) != 0)
            {
                prefix++;
            }
            if (prefix == 9)
            {
                return bits.Read(escapeBits);
            }
            if (k == 1)
            {
                return (uint)prefix;
            }
            uint suffix = bits.Peek(k);
            bits.Skip(suffix < 2 ? k - 1 : k);
            return (uint)prefix * divisor + (suffix < 2 ? 0 : suffix - 1);
        }
    }
}
