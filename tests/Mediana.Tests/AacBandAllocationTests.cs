// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class AacBandAllocationTests
    {
        [Theory]
        [InlineData(0, 300)]
        [InlineData(3, 1000)]
        [InlineData(4, 2400)]
        [InlineData(7, 1600)]
        [InlineData(11, 6000)]
        public void AllocatedBandScalesFitTheExactSyntaxAndRoundTripEveryCoefficient(int frequency, int budget)
        {
            double[] spectrum = Spectrum();
            int[] quantized = new int[1024];
            byte[] books = new byte[64];
            byte[] scales = new byte[64];
            int gain = AacChannelEncoder.Quantize(spectrum, new double[1024], quantized, books, scales, frequency, budget, default);
            VerifySyntax(frequency, gain, quantized, books, scales, budget);
            Assert.True(scales.Take(AacBandTables.Get(frequency, false).Length - 1).Distinct().Count() > 1);
        }

        [Theory]
        [InlineData(31)]
        [InlineData(1000)]
        public void SilenceUsesNoScaleSymbolsAndCountsTheSectionEscapeExactly(int budget)
        {
            int[] quantized = new int[1024];
            byte[] books = new byte[64];
            byte[] scales = new byte[64];
            int gain = AacChannelEncoder.Quantize(new double[1024], new double[1024], quantized, books, scales, 3, budget, default);
            VerifySyntax(3, gain, quantized, books, scales, budget);
            Assert.All(books, book => Assert.Equal(0, book));
        }

        [Fact]
        public void ScaleCostSkipsZeroBandsAndRejectsAnUnrepresentableDifference()
        {
            AacBandQuantization[] bands = [new(10, 1, 1, 0), new(255, 0, 0, 0), new(70, 1, 1, 0)];
            Assert.Equal(11 + 2 + AacHuffmanEncoder.CountSymbol(0, 60) + AacHuffmanEncoder.CountSymbol(0, 120) + 27,
                AacChannelEncoder.Count(bands));
            bands[2] = new(71, 1, 1, 0);
            Assert.Equal(int.MaxValue, AacChannelEncoder.Count(bands));
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
                if (AacChannelEncoder.Count(state) <= Budget)
                {
                    initial = state.Sum(band => band.Error);
                    break;
                }
            }
            byte[] books = new byte[64];
            byte[] scales = new byte[64];
            _ = AacChannelEncoder.Quantize(spectrum, powered, scratch, books, scales, Frequency, Budget, default);
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

        private static void VerifySyntax(int frequency, int gain, int[] quantized, byte[] books, byte[] scales, int budget)
        {
            ReadOnlySpan<int> boundaries = AacBandTables.Get(frequency, false);
            AacBandQuantization[] state = new AacBandQuantization[boundaries.Length - 1];
            for (int band = 0; band < state.Length; band++)
            {
                int bits = books[band] == 0 ? 0 : AacHuffmanEncoder.Count(books[band], quantized.AsSpan(boundaries[band], boundaries[band + 1] - boundaries[band]));
                state[band] = new(scales[band], books[band], bits, 0);
            }
            byte[] encoded = new byte[2048];
            AacBitWriter writer = new(encoded);
            AacChannelEncoder.Write(ref writer, gain, quantized, books, scales, frequency, true);
            Assert.Equal(AacChannelEncoder.Count(state), writer.Position);
            Assert.InRange(writer.Position, 1, budget);
            writer.Write(21, 5);
            MediaBufferAllocator allocator = new(32768);
            using AacChannelState channel = new(allocator);
            AacBitReader reader = new(encoded);
            AacChannelParser.Read(ref reader, channel, frequency, new(frequency, 0, 0, state.Length, [1]), false, default);
            Assert.Equal(quantized, channel.Quantized.ToArray());
            for (int band = 0; band < state.Length; band++)
            {
                if (books[band] != 0)
                {
                    Assert.Equal(scales[band], channel.Scales[band]);
                }
            }
            Assert.Equal(21, reader.Read(5));
            channel.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
