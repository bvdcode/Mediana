// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Mp3;

namespace Mediana.Tests
{
    public class Mp3RequantizationTests
    {
        private static readonly Mp3GranuleInfo DefaultInfo = new(0, 0, 210, 0, 0, false,
            0, 0, 0, 0, 0, 0, 0, 0, false, false, 0);

        [Theory]
        [InlineData(48000)]
        [InlineData(44100)]
        [InlineData(32000)]
        [InlineData(24000)]
        [InlineData(22050)]
        [InlineData(16000)]
        [InlineData(12000)]
        [InlineData(11025)]
        [InlineData(8000)]
        public void EveryLayoutCoversEachCoefficientAndMatchesRequantizationEquation(int rate)
        {
            Mp3SpectralBand[] storage = new Mp3SpectralBand[39];
            int[] quantized = Enumerable.Range(0, 576).Select(index => index % 2 == 0 ? index * 13 : -index * 7).ToArray();
            byte[] scales = Enumerable.Range(0, 39).Select(index => (byte)(index % 16)).ToArray();
            double[] reconstructed = new double[576];
            double[] reordered = new double[576];
            int[] pretab = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 3, 3, 3, 2, 0];
            for (int shape = 0; shape < 5; shape++)
            {
                Mp3GranuleInfo info = DefaultInfo with
                {
                    BlockType = shape == 4 ? 2 : shape,
                    MixedBlock = shape == 4,
                    SubblockGain0 = 1,
                    SubblockGain1 = 4,
                    SubblockGain2 = 7,
                    Preflag = true
                };
                int count = Mp3SpectralLayout.Write(rate, info, storage);
                ReadOnlySpan<Mp3SpectralBand> bands = storage.AsSpan(0, count);
                Assert.Equal(576, bands[^1].Offset + bands[^1].Width);
                bool[] destinations = new bool[576];
                for (int gain = 0; gain <= 255; gain += 17)
                {
                    foreach (bool scale in new[] { false, true })
                    {
                        info = info with { GlobalGain = gain, ScaleFactorScale = scale };
                        Mp3Requantization.Apply(quantized, scales, info, bands, reconstructed);
                        for (int index = 0; index < count; index++)
                        {
                            Mp3SpectralBand band = bands[index];
                            double exponent = (gain - 210) / 4.0;
                            int subgain = band.Window switch { -1 => 0, 0 => 1, 1 => 4, 2 => 7, _ => throw new InvalidOperationException() };
                            exponent -= 2 * subgain;
                            exponent -= (scale ? 1.0 : 0.5) * (scales[index] + (band.Window < 0 ? pretab[band.Band] : 0));
                            for (int offset = band.Offset; offset < band.Offset + band.Width; offset++)
                            {
                                int value = quantized[offset];
                                double expected = Math.Sign(value) * Math.Pow(Math.Abs(value), 4.0 / 3.0) * Math.Pow(2, exponent);
                                Assert.InRange(Math.Abs(reconstructed[offset] - expected), 0, Math.Abs(expected) * 1e-14);
                            }
                        }
                    }
                }
                for (int index = 0; index < 576; index++)
                {
                    reconstructed[index] = index + 1;
                }
                Mp3SpectralLayout.Reorder(reconstructed, bands, reordered);
                foreach (Mp3SpectralBand band in bands)
                {
                    for (int index = 0; index < band.Width; index++)
                    {
                        int frequency = band.FrequencyStart + index;
                        int destination = band.Window < 0 ? band.Offset + index : 18 * (frequency / 6) + 6 * band.Window + frequency % 6;
                        Assert.False(destinations[destination]);
                        destinations[destination] = true;
                        Assert.Equal(band.Offset + index + 1, reordered[destination]);
                    }
                }
                Assert.All(destinations, Assert.True);
            }
        }

        [Fact]
        public void EveryMagnitudeIncludingEscapeMaximumMatchesPowerLaw()
        {
            int[] input = new int[576];
            double[] output = new double[576];
            byte[] scales = new byte[39];
            Mp3SpectralBand[] bands = new Mp3SpectralBand[39];
            int count = Mp3SpectralLayout.Write(44100, DefaultInfo, bands);
            for (int start = 0; start <= 8206; start += 576)
            {
                for (int index = 0; index < 576; index++)
                {
                    int value = Math.Min(8206, start + index);
                    input[index] = index % 2 == 0 ? value : -value;
                }
                Mp3Requantization.Apply(input, scales, DefaultInfo, bands.AsSpan(0, count), output);
                for (int index = 0; index < 576; index++)
                {
                    Assert.Equal(Math.Sign(input[index]) * Math.Pow(Math.Abs(input[index]), 4.0 / 3.0), output[index]);
                }
            }
            input[0] = int.MinValue;
            Assert.Throws<InvalidDataException>(() => Mp3Requantization.Apply(input, scales, DefaultInfo, bands.AsSpan(0, count), output));
        }
    }
}
