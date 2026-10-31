// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class AacBandAllocationTestInput
    {
        public static void VerifySharedBudget(int frequency, int budget, int activeChannel, double quietGain)
        {
            double[] spectrum = new double[2048];
            for (int bin = 0; bin < 1024; bin++)
            {
                double value = (((uint)bin * 747796405u + 2891336453u) & 65535) - 32768d;
                spectrum[activeChannel * 1024 + bin] = value * 256;
                spectrum[(1 - activeChannel) * 1024 + bin] = value * 256 * quietGain;
            }
            double independentError = 0;
            for (int channel = 0; channel < 2; channel++)
            {
                int[] values = new int[1024];
                byte[] localBooks = new byte[64];
                byte[] localScales = new byte[64];
                AacSpectrumQuantizer.Quantize(spectrum.AsSpan(channel * 1024, 1024), new double[1024], values,
                    localBooks, localScales, new int[1], frequency, budget / 2, default);
                independentError += Error(spectrum.AsSpan(channel * 1024, 1024), values, localScales, frequency);
            }
            int[] quantized = new int[2048];
            byte[] books = new byte[128];
            byte[] scales = new byte[128];
            int[] gains = new int[2];
            AacSpectrumQuantizer.Quantize(spectrum, new double[2048], quantized, books, scales, gains, frequency, budget, default);
            int total = 0;
            double error = 0;
            for (int channel = 0; channel < 2; channel++)
            {
                int[] values = quantized.AsSpan(channel * 1024, 1024).ToArray();
                byte[] localBooks = books.AsSpan(channel * 64, 64).ToArray();
                byte[] localScales = scales.AsSpan(channel * 64, 64).ToArray();
                int bits = VerifySyntax(frequency, gains[channel], values, localBooks, localScales, budget);
                total += bits;
                error += Error(spectrum.AsSpan(channel * 1024, 1024), values, localScales, frequency);
                if (channel == activeChannel && bits <= budget / 2)
                {
                    throw new InvalidDataException("The active AAC channel did not use the available shared budget.");
                }
                if (channel != activeChannel && quietGain == 0 && values.Any(value => value != 0))
                {
                    throw new InvalidDataException("Shared AAC allocation introduced signal into a silent channel.");
                }
            }
            if (total > budget || error >= independentError * 0.98)
            {
                throw new InvalidDataException($"Shared AAC allocation did not reduce distortion within the complete channel-pair budget: {error} versus {independentError}, {total}/{budget} bits.");
            }
        }

        private static double Error(ReadOnlySpan<double> spectrum, ReadOnlySpan<int> quantized, ReadOnlySpan<byte> scales, int frequency)
        {
            ReadOnlySpan<int> boundaries = AacBandTables.Get(frequency, false);
            double total = 0;
            for (int band = 0; band < boundaries.Length - 1; band++)
            {
                for (int bin = boundaries[band]; bin < boundaries[band + 1]; bin++)
                {
                    double actual = Math.Sign(quantized[bin]) * AacQuantizer.Reconstruct(Math.Abs(quantized[bin]), scales[band]);
                    double error = spectrum[bin] - actual;
                    total += error * error;
                }
            }
            return total;
        }

        public static int VerifySyntax(int frequency, int gain, int[] quantized, byte[] books, byte[] scales, int budget)
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
            if (AacChannelEncoder.Count(state) != writer.Position || writer.Position < 1 || writer.Position > budget)
            {
                throw new InvalidDataException("AAC scale, section and spectral costs differ from the complete channel syntax.");
            }
            int count = writer.Position;
            writer.Write(21, 5);
            MediaBufferAllocator allocator = new(32768);
            using AacChannelState channel = new(allocator);
            AacBitReader reader = new(encoded);
            AacChannelParser.Read(ref reader, channel, frequency, new(frequency, 0, 0, state.Length, [1]), false, default);
            if (!quantized.AsSpan().SequenceEqual(channel.Quantized))
            {
                throw new InvalidDataException("AAC allocation changed a quantized spectral coefficient during syntax decoding.");
            }
            for (int band = 0; band < state.Length; band++)
            {
                if (books[band] != 0)
                {
                    if (scales[band] != channel.Scales[band])
                    {
                        throw new InvalidDataException("AAC allocation changed a band scale during syntax decoding.");
                    }
                }
            }
            if (reader.Read(5) != 21)
            {
                throw new InvalidDataException("AAC channel syntax consumed the following payload bits.");
            }
            channel.Dispose();
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("AAC channel verification retained media storage.");
            }
            return count;
        }
    }
}
