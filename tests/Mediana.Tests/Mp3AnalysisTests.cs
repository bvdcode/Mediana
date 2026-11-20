// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Mp3;

namespace Mediana.Tests
{
    public class Mp3AnalysisTests
    {
        [Fact]
        public void AnalysisAndSynthesisPreserveBroadbandPcm()
        {
            const int count = 576 * 20;
            float[] input = new float[count];
            Random random = new(123);
            for (int index = 0; index < count - 2304; index++)
            {
                input[index] = (float)(0.4 * Math.Sin(index * 0.13) + 0.1 * Math.Cos(index * 0.37) + 0.01 * (2 * random.NextDouble() - 1));
            }
            float[] output = new float[count];
            double[] history = new double[512];
            double[] previous = new double[576];
            double[] overlap = new double[576];
            double[] spectrum = new double[576];
            double[] subbands = new double[576];
            float[] synthesis = new float[1024];
            int position = 0;
            Mp3GranuleInfo info = new(0, 0, 210, 0, 0, false, 0, 0, 0, 0, 0, 0, 0, 0, false, false, 0);
            for (int block = 0; block < count; block += 576)
            {
                Mp3AnalysisFilter.Process(input.AsSpan(block, 576), 1, 0, history, previous, spectrum);
                Mp3HybridSynthesis.Process(spectrum, info, 48000, overlap, subbands);
                Mp3PolyphaseSynthesis.Process(subbands, synthesis, ref position, output.AsSpan(block, 576), 1, 0);
            }
            double error = 0;
            double energy = 0;
            const int delay = 1057;
            for (int index = 0; index < count - 2304; index++)
            {
                double difference = output[index + delay] - input[index];
                error += difference * difference;
                energy += input[index] * input[index];
            }
            Assert.True(10 * Math.Log10(energy / error) > 65, $"SNR: {10 * Math.Log10(energy / error)} dB");
        }
    }
}
