// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal static class AacBandRate
    {
        // The channel already fits the syntax. Changing a band affects its spectrum, two scale differences
        // and the adjacent codebook runs within the same window group.
        public static int ReplacementDelta(ReadOnlySpan<AacBandQuantization> bands, int band, AacBandQuantization replacement,
            int bandsPerGroup, int lengthBits)
        {
            AacBandQuantization current = bands[band];
            if (current.Book == 0 && replacement.Book == 0) { return 0; }
            int previous = band - 1;
            while (previous >= 0 && bands[previous].Book == 0) { previous--; }
            int next = band + 1;
            while (next < bands.Length && bands[next].Book == 0) { next++; }
            int previousScale = previous < 0 ? -1 : bands[previous].Scale;
            int nextScale = next == bands.Length ? -1 : bands[next].Scale;
            int newBits = SpectralBits(replacement, previousScale, nextScale);
            if (newBits == int.MaxValue) { return int.MaxValue; }
            int delta = newBits - SpectralBits(current, previousScale, nextScale);
            if (current.Book == replacement.Book) { return delta; }

            int begin = band - band % bandsPerGroup;
            int end = Math.Min(begin + bandsPerGroup, bands.Length);
            int left = band > begin ? bands[band - 1].Book : -1;
            int right = band + 1 < end ? bands[band + 1].Book : -1;
            int leftLength = 0;
            for (int i = band - 1; i >= begin && bands[i].Book == left; i--) { leftLength++; }
            int rightLength = 0;
            for (int i = band + 1; i < end && bands[i].Book == right; i++) { rightLength++; }
            int escape = (1 << lengthBits) - 1;
            return delta + SectionDelta(replacement.Book, left, leftLength, right, rightLength, lengthBits, escape)
                - SectionDelta(current.Book, left, leftLength, right, rightLength, lengthBits, escape);
        }

        // The current band is already installed. Include the adjoining nonzero scale factors
        // and the borders of both codebook runs that can change in this window group.
        public static (int Start, int Length) ChangedRange(ReadOnlySpan<AacBandQuantization> bands, int band,
            AacBandQuantization previous, int bandsPerGroup)
        {
            AacBandQuantization current = bands[band];
            int first = band;
            int last = band;
            if ((previous.Book != 0 || current.Book != 0)
                && (previous.Book == 0 || current.Book == 0 || previous.Scale != current.Scale))
            {
                while (first > 0 && bands[--first].Book == 0) { }
                while (last + 1 < bands.Length && bands[++last].Book == 0) { }
            }
            if (previous.Book != current.Book)
            {
                int groupStart = band - band % bandsPerGroup;
                int groupEnd = Math.Min(groupStart + bandsPerGroup, bands.Length);
                (int oldLeft, int oldRight) = RunBorder(bands, band, groupStart, groupEnd, previous.Book);
                (int newLeft, int newRight) = RunBorder(bands, band, groupStart, groupEnd, current.Book);
                first = Math.Min(first, Math.Min(oldLeft, newLeft));
                last = Math.Max(last, Math.Max(oldRight, newRight));
            }
            return (first, last - first + 1);
        }

        private static (int Left, int Right) RunBorder(ReadOnlySpan<AacBandQuantization> bands, int band,
            int groupStart, int groupEnd, byte book)
        {
            int left = band;
            int right = band;
            while (left > groupStart && bands[left - 1].Book == book) { left--; }
            while (right + 1 < groupEnd && bands[right + 1].Book == book) { right++; }
            return (Math.Max(groupStart, left - 1), Math.Min(groupEnd - 1, right + 1));
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
