// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;

namespace Mediana.Tests
{
    public class AacBandAllocationTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(10)]
        [InlineData(11)]
        public void RefinesAQuietBandAcrossAZeroQuantizationPlateau(int frequency)
        {
            AacBandAllocationTestInput.VerifyZeroBandRecovery(frequency, false, 1);
            AacBandAllocationTestInput.VerifyZeroBandRecovery(frequency, false, 2);
            AacBandAllocationTestInput.VerifyZeroBandRecovery(frequency, true, 1);
            AacBandAllocationTestInput.VerifyZeroBandRecovery(frequency, true, 2);
        }

        [Theory]
        [InlineData(3, 2400, 0, 0)]
        [InlineData(4, 3200, 1, 0)]
        [InlineData(3, 2400, 0, 0.05)]
        [InlineData(11, 12000, 1, 0)]
        public void ChannelPairSharesItsExactBudgetAndPreservesIndependentScaleSyntax(int frequency, int budget,
            int activeChannel, double quietGain) =>
            AacBandAllocationTestInput.VerifySharedBudget(frequency, budget, activeChannel, quietGain);

        [Theory]
        [InlineData(0, 300)]
        [InlineData(3, 1000)]
        [InlineData(4, 2400)]
        [InlineData(7, 1600)]
        [InlineData(11, 6000)]
        public void AllocatedBandScalesFitTheExactSyntaxAndRoundTripEveryCoefficient(int frequency, int budget)
        {
            AacEncodingWindow window = new(frequency, 0, 0, [1], new int[AacSpectrumQuantizer.ChannelBands + 1]);
            double[] spectrum = Spectrum();
            int[] quantized = new int[1024];
            byte[] books = new byte[AacSpectrumQuantizer.ChannelBands];
            byte[] scales = new byte[AacSpectrumQuantizer.ChannelBands];
            int[] gains = new int[1];
            AacSpectrumQuantizer.Quantize(spectrum, new double[1024], quantized, books, scales, gains, window, budget, default);
            int gain = gains[0];
            AacBandAllocationTestInput.VerifySyntax(frequency, window, gain, quantized, books, scales, budget, true);
            Assert.True(scales.Take(AacBandTables.Get(frequency, false).Length - 1).Distinct().Count() > 1);
        }

        [Theory]
        [InlineData(31)]
        [InlineData(1000)]
        public void SilenceUsesNoScaleSymbolsAndCountsTheSectionEscapeExactly(int budget)
        {
            AacEncodingWindow window = new(3, 0, 0, [1], new int[AacSpectrumQuantizer.ChannelBands + 1]);
            int[] quantized = new int[1024];
            byte[] books = new byte[AacSpectrumQuantizer.ChannelBands];
            byte[] scales = new byte[AacSpectrumQuantizer.ChannelBands];
            int[] gains = new int[1];
            AacSpectrumQuantizer.Quantize(new double[1024], new double[1024], quantized, books, scales, gains, window, budget, default);
            int gain = gains[0];
            AacBandAllocationTestInput.VerifySyntax(3, window, gain, quantized, books, scales, budget, true);
            Assert.All(books, book => Assert.Equal(0, book));
        }

        [Fact]
        public void ScaleCostSkipsZeroBandsAndRejectsAnUnrepresentableDifference()
        {
            AacBandQuantization[] bands = [new(10, 1, 1, 0), new(255, 0, 0, 0), new(70, 1, 1, 0)];
            Assert.Equal(11 + 2 + AacHuffmanEncoder.CountSymbol(0, 60) + AacHuffmanEncoder.CountSymbol(0, 120) + 27,
                AacChannelEncoder.Count(bands, bands.Length, 5));
            bands[2] = new(71, 1, 1, 0);
            Assert.Equal(int.MaxValue, AacChannelEncoder.Count(bands, bands.Length, 5));
        }

        [Fact]
        public void BoundedAllocationReducesSpectralErrorFromTheUniformSeed()
        {
            const int Frequency = 3;
            const int Budget = 1600;
            double[] spectrum = Spectrum();
            double[] powered = spectrum.Select(value => Math.Pow(Math.Abs(value), 0.75)).ToArray();
            ReadOnlySpan<int> boundaries = AacBandTables.Get(Frequency, false);
            AacBandQuantization[] state = new AacBandQuantization[boundaries.Length - 1];
            int[] scratch = new int[1024];
            double initial = double.PositiveInfinity;
            for (int gain = 0; gain <= 255; gain++)
            {
                for (int band = 0; band < state.Length; band++)
                {
                    int start = boundaries[band];
                    int count = boundaries[band + 1] - start;
                    state[band] = AacBandQuantization.Measure(spectrum.AsSpan(start, count), powered.AsSpan(start, count), scratch.AsSpan(0, count), gain);
                }
                if (AacChannelEncoder.Count(state, state.Length, 5) <= Budget)
                {
                    initial = state.Sum(band => band.Error);
                    break;
                }
            }
            byte[] books = new byte[AacSpectrumQuantizer.ChannelBands];
            byte[] scales = new byte[AacSpectrumQuantizer.ChannelBands];
            AacEncodingWindow window = new(Frequency, 0, 0, [1], new int[AacSpectrumQuantizer.ChannelBands + 1]);
            AacSpectrumQuantizer.Quantize(spectrum, powered, scratch, books, scales, new int[1], window, Budget, default);
            double error = 0;
            for (int band = 0; band < state.Length; band++)
            {
                for (int bin = boundaries[band]; bin < boundaries[band + 1]; bin++)
                {
                    double actual = Math.Sign(scratch[bin]) * AacQuantizer.Reconstruct(Math.Abs(scratch[bin]), scales[band]);
                    error += Math.Pow(spectrum[bin] - actual, 2);
                }
            }
            Assert.True(error < initial * 0.9, $"Allocated error {error}; uniform seed {initial}.");
        }

        private static double[] Spectrum() => Enumerable.Range(0, 1024).Select(bin =>
            Math.Sin(bin * 0.941) * (bin < 64 ? 10000000 : bin < 384 ? 300000 : 1000)).ToArray();

    }
}
