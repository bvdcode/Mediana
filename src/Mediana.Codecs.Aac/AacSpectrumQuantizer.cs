// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal static class AacSpectrumQuantizer
    {
        private const int ChannelBins = 1024;
        internal const int ChannelBands = 128;

        public static void Quantize(ReadOnlySpan<double> spectrum, Span<double> powered, Span<int> quantized,
            Span<byte> books, Span<byte> scales, Span<int> gains, scoped AacEncodingWindow window, int budget, CancellationToken token)
        {
            int channels = gains.Length;
            if (channels is not (1 or 2) || spectrum.Length != channels * ChannelBins || powered.Length < spectrum.Length ||
                quantized.Length != spectrum.Length || books.Length != channels * ChannelBands || scales.Length != books.Length)
            {
                throw new ArgumentException("AAC spectral allocation requires complete mono or stereo channel workspaces.");
            }
            double maximum = 0;
            for (int bin = 0; bin < spectrum.Length; bin++)
            {
                powered[bin] = Math.Pow(Math.Abs(spectrum[bin]), 0.75);
                maximum = Math.Max(maximum, powered[bin]);
            }
            double required = maximum == 0 ? 0 : Math.Ceiling(100 + 16.0 / 3 * Math.Log2(maximum / AacQuantizer.MaximumMagnitude));
            if (!double.IsFinite(required) || required > AacQuantizer.MaximumScale)
            {
                throw new NotSupportedException("The PCM magnitude exceeds the AAC quantizer range.");
            }
            ReadOnlySpan<int> boundaries = window.Boundaries;
            int channelBands = boundaries.Length - 1;
            Span<AacBandQuantization> bands = stackalloc AacBandQuantization[channels * channelBands];
            Span<AacBandQuantization> candidates = stackalloc AacBandQuantization[bands.Length * 2];
            AacBandAllocator allocation = new(spectrum, powered, window, quantized, bands, candidates);
            allocation.Allocate(Math.Max(0, (int)required), budget, token);
            for (int channel = 0; channel < channels; channel++)
            {
                int gain = 0;
                bool first = true;
                for (int band = 0; band < channelBands; band++)
                {
                    token.ThrowIfCancellationRequested();
                    AacBandQuantization selected = bands[channel * channelBands + band];
                    int start = channel * ChannelBins + boundaries[band];
                    int length = boundaries[band + 1] - boundaries[band];
                    _ = AacQuantizer.Quantize(spectrum.Slice(start, length), powered.Slice(start, length), quantized.Slice(start, length), selected.Scale);
                    books[channel * ChannelBands + band] = selected.Book;
                    scales[channel * ChannelBands + band] = (byte)selected.Scale;
                    if (first && selected.Book != 0)
                    {
                        gain = selected.Scale;
                        first = false;
                    }
                }
                gains[channel] = gain;
            }
        }
    }
}
