// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class AacStereoNoiseTestInput
    {
        public static void Verify(bool noiseLeft, int maskMode)
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            using (AacChannelState left = Channel(allocator, noiseLeft))
            using (AacChannelState right = Channel(allocator, !noiseLeft))
            {
                AacChannelState noiseChannel = noiseLeft ? left : right;
                AacChannelState spectralChannel = noiseLeft ? right : left;
                spectralChannel.Quantized[0] = 1;
                byte[] mask = new byte[512];
                mask[0] = 1;
                uint state = 1;
                AacSpectralTools.Reconstruct(left, right, true, maskMode, mask, ref state, default);
                double energy = 0;
                for (int index = 0; index < noiseChannel.Spectrum.Length; index++)
                {
                    double sample = noiseChannel.Spectrum[index];
                    if (!double.IsFinite(sample))
                    {
                        throw new InvalidDataException("One-channel PNS produced nonfinite coefficients.");
                    }
                    energy += sample * sample;
                    double expected = 0;
                    if (index == 0)
                    {
                        expected = 1;
                    }
                    if (spectralChannel.Spectrum[index] != expected)
                    {
                        throw new InvalidDataException("An ignored M/S flag changed the independently coded channel.");
                    }
                }
                if (Math.Abs(energy - 4) > 1e-12)
                {
                    throw new InvalidDataException("One-channel PNS changed its declared band energy.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("The one-channel PNS check retained storage.");
            }
        }

        private static AacChannelState Channel(MediaBufferAllocator allocator, bool noise)
        {
            AacChannelState channel = new(allocator) { Info = new AacWindowInfo(3, 0, 0, 1, [1]) };
            if (noise)
            {
                channel.Books[0] = 13;
                channel.Scales[0] = 4;
            }
            else
            {
                channel.Books[0] = 1;
                channel.Scales[0] = 100;
            }
            return channel;
        }
    }
}
