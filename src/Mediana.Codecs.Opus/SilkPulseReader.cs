// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class SilkPulseReader
    {
        public const int BlockSize = 16;
        public const int MaximumBlocks = 20;

        /// <summary>Decodes complete shell blocks, including the eight discarded samples of a 120-sample mediumband frame.</summary>
        public static void Read(ref OpusRangeDecoder decoder, scoped Span<short> pulses, int frameSamples,
            SilkSignalType signal, int quantizationOffset)
        {
            if (frameSamples is not (80 or 120 or 160 or 240 or 320)) { throw new ArgumentOutOfRangeException(nameof(frameSamples)); }
            ArgumentOutOfRangeException.ThrowIfNegative(quantizationOffset);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(quantizationOffset, 1);
            int blocks = (frameSamples + BlockSize - 1) / BlockSize;
            ArgumentOutOfRangeException.ThrowIfLessThan(pulses.Length, blocks * BlockSize, nameof(pulses));
            ReadOnlySpan<byte> rateModel = SilkPulseModels.Rates(signal);
            int rate = decoder.ReadInverseCdf(rateModel);
            Span<int> counts = stackalloc int[MaximumBlocks], shifts = stackalloc int[MaximumBlocks];
            for (int block = 0; block < blocks; block++)
            {
                int shift = 0, count = decoder.ReadInverseCdf(SilkPulseModels.Counts(rate, false));
                while (count == 17)
                {
                    shift++;
                    count = decoder.ReadInverseCdf(SilkPulseModels.Counts(9, shift == 10));
                }
                counts[block] = count; shifts[block] = shift;
            }
            for (int block = 0; block < blocks; block++)
            {
                Split(ref decoder, pulses.Slice(block * BlockSize, BlockSize), counts[block]);
            }
            for (int block = 0; block < blocks; block++)
            {
                int bits = shifts[block];
                if (bits == 0) { continue; }
                Span<short> values = pulses.Slice(block * BlockSize, BlockSize);
                for (int at = 0; at < BlockSize; at++)
                {
                    int magnitude = values[at];
                    for (int bit = 0; bit < bits; bit++) { magnitude = (magnitude << 1) + decoder.ReadInverseCdf([120, 0]); }
                    values[at] = checked((short)magnitude);
                }
            }
            Span<byte> signModel = stackalloc byte[2];
            signModel[1] = 0;
            for (int block = 0; block < blocks; block++)
            {
                if (counts[block] == 0 && shifts[block] == 0) { continue; }
                signModel[0] = SilkPulseModels.SignProbability(signal, quantizationOffset, counts[block]);
                Span<short> values = pulses.Slice(block * BlockSize, BlockSize);
                for (int at = 0; at < BlockSize; at++)
                {
                    if (values[at] > 0 && decoder.ReadInverseCdf(signModel) == 0) { values[at] = (short)-values[at]; }
                }
            }
        }

        private static void Split(ref OpusRangeDecoder decoder, scoped Span<short> values, int pulses)
        {
            if (pulses == 0) { values.Clear(); return; }
            if (values.Length == 1) { values[0] = (short)pulses; return; }
            int left = decoder.ReadInverseCdf(SilkPulseModels.Split(values.Length, pulses));
            int half = values.Length / 2;
            Split(ref decoder, values[..half], left);
            Split(ref decoder, values[half..], pulses - left);
        }
    }
}
