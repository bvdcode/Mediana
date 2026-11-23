// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal static class AacBandRate
    {
        // The channel already fits the syntax. Changing a band affects its spectrum, two scale differences
        // and the adjacent codebook runs within the same window group.
        public static int CountReplacement(ReadOnlySpan<AacBandQuantization> bands, int band, AacBandQuantization replacement,
            int channelBits, int bandsPerGroup, int lengthBits)
        {
            AacBandQuantization current = bands[band];
            if (current.Book == 0 && replacement.Book == 0) { return channelBits; }
            int previous = band - 1;
            while (previous >= 0 && bands[previous].Book == 0) { previous--; }
            int next = band + 1;
            while (next < bands.Length && bands[next].Book == 0) { next++; }
            int previousScale = previous < 0 ? -1 : bands[previous].Scale;
            int nextScale = next == bands.Length ? -1 : bands[next].Scale;
            int newBits = SpectralBits(replacement, previousScale, nextScale);
            if (newBits == int.MaxValue) { return int.MaxValue; }
            int total = channelBits + newBits - SpectralBits(current, previousScale, nextScale);
            if (current.Book == replacement.Book) { return total; }

            int begin = band - band % bandsPerGroup;
            int end = Math.Min(begin + bandsPerGroup, bands.Length);
            int left = band > begin ? bands[band - 1].Book : -1;
            int right = band + 1 < end ? bands[band + 1].Book : -1;
            int leftLength = 0;
            for (int i = band - 1; i >= begin && bands[i].Book == left; i--) { leftLength++; }
            int rightLength = 0;
            for (int i = band + 1; i < end && bands[i].Book == right; i++) { rightLength++; }
            int escape = (1 << lengthBits) - 1;
            return total + SectionDelta(replacement.Book, left, leftLength, right, rightLength, lengthBits, escape)
                - SectionDelta(current.Book, left, leftLength, right, rightLength, lengthBits, escape);
        }

        private static int SpectralBits(AacBandQuantization value, int previous, int next)
        {
            int bits = 0;
            if (value.Book != 0)
            {
                int difference = previous < 0 ? 0 : value.Scale - previous;
                if (difference is < -60 or > 60) { return int.MaxValue; }
                bits = value.Bits + AacHuffmanEncoder.CountSymbol(0, difference + 60);
                previous = value.Scale;
            }
            if (next < 0) { return bits; }
            int forward = previous < 0 ? 0 : next - previous;
            return forward is < -60 or > 60 ? int.MaxValue : bits + AacHuffmanEncoder.CountSymbol(0, forward + 60);
        }

        private static int SectionDelta(int book, int left, int leftLength, int right, int rightLength, int lengthBits, int escape)
        {
            int length = 1;
            int removed = 0;
            if (book == left)
            {
                length += leftLength;
                removed += AacChannelEncoder.SectionBits(leftLength, lengthBits, escape);
            }
            if (book == right)
            {
                length += rightLength;
                removed += AacChannelEncoder.SectionBits(rightLength, lengthBits, escape);
            }
            return AacChannelEncoder.SectionBits(length, lengthBits, escape) - removed;
        }
    }
}
