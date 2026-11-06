// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltEnergyReader
    {
        public static void ReadCoarse(ref OpusRangeDecoder decoder, scoped Span<float> energies,
            int channels, int startBand, int endBand, int frameShift, bool intra)
        {
            Validate(energies, channels, startBand, endBand);
            ReadOnlySpan<byte> model = CeltEnergyModels.GetProbabilities(frameShift, intra);
            float time = CeltEnergyModels.GetTimePrediction(frameShift);
            float beta = CeltEnergyModels.GetFrequencyPrediction(frameShift);
            if (intra)
            {
                time = 0;
                beta = 4915f / 32768;
            }
            Span<float> frequency = stackalloc float[2];
            frequency.Clear();
            for (int band = startBand; band < endBand; band++)
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    int available = decoder.FrameBitCount - decoder.BitsUsed;
                    int residual;
                    if (available >= 15)
                    {
                        residual = decoder.ReadLaplace(model[2 * band] << 7, model[2 * band + 1] << 6);
                    }
                    else if (available >= 2)
                    {
                        residual = decoder.ReadInverseCdf([2, 1, 0], 2) switch
                        {
                            0 => 0,
                            1 => -1,
                            2 => 1,
                            _ => throw new InvalidDataException("A CELT energy residual is outside its model.")
                        };
                    }
                    else if (available >= 1)
                    {
                        residual = 0;
                        if (decoder.ReadBit(1)) { residual = -1; }
                    }
                    else { residual = -1; }
                    int at = channel * CeltEnergyModels.Bands + band;
                    energies[at] = time * Math.Max(-9f, energies[at]) + frequency[channel] + residual;
                    frequency[channel] = frequency[channel] + residual - beta * residual;
                }
            }
        }

        public static void ReadFine(ref OpusRangeDecoder decoder, scoped Span<float> energies,
            scoped ReadOnlySpan<int> fineBits, int channels, int startBand, int endBand)
        {
            Validate(energies, channels, startBand, endBand);
            ValidateFine(fineBits, startBand, endBand);
            for (int band = startBand; band < endBand; band++)
            {
                int bits = fineBits[band];
                if (bits == 0 || decoder.FrameBitCount - decoder.BitsUsed < channels * bits) { continue; }
                float step = MathF.ScaleB(1, -bits);
                for (int channel = 0; channel < channels; channel++)
                {
                    uint value = decoder.ReadRawBits(bits);
                    energies[channel * CeltEnergyModels.Bands + band] += (value + .5f) * step - .5f;
                }
            }
        }

        public static void ReadFinal(ref OpusRangeDecoder decoder, scoped Span<float> energies,
            scoped ReadOnlySpan<int> fineBits, scoped ReadOnlySpan<byte> priorities, int channels, int startBand, int endBand)
        {
            Validate(energies, channels, startBand, endBand);
            ValidateFine(fineBits, startBand, endBand);
            ArgumentOutOfRangeException.ThrowIfLessThan(priorities.Length, endBand, nameof(priorities));
            foreach (byte priority in priorities[startBand..endBand])
            {
                ArgumentOutOfRangeException.ThrowIfGreaterThan(priority, (byte)1, nameof(priorities));
            }
            for (int priority = 0; priority < 2; priority++)
            {
                for (int band = startBand; band < endBand; band++)
                {
                    if (decoder.FrameBitCount - decoder.BitsUsed < channels) { return; }
                    if (fineBits[band] == CeltEnergyModels.MaximumFineBits || priorities[band] != priority) { continue; }
                    float step = MathF.ScaleB(1, -fineBits[band] - 1);
                    for (int channel = 0; channel < channels; channel++)
                    {
                        uint value = decoder.ReadRawBits(1);
                        energies[channel * CeltEnergyModels.Bands + band] += (value - .5f) * step;
                    }
                }
            }
        }

        private static void Validate(Span<float> energies, int channels, int startBand, int endBand)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(channels, 2);
            ArgumentOutOfRangeException.ThrowIfNegative(startBand);
            ArgumentOutOfRangeException.ThrowIfLessThan(endBand, startBand);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(endBand, CeltEnergyModels.Bands);
            ArgumentOutOfRangeException.ThrowIfLessThan(energies.Length, channels * CeltEnergyModels.Bands, nameof(energies));
        }

        private static void ValidateFine(ReadOnlySpan<int> fineBits, int startBand, int endBand)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(fineBits.Length, endBand, nameof(fineBits));
            foreach (int bits in fineBits[startBand..endBand])
            {
                ArgumentOutOfRangeException.ThrowIfNegative(bits, nameof(fineBits));
                ArgumentOutOfRangeException.ThrowIfGreaterThan(bits, CeltEnergyModels.MaximumFineBits, nameof(fineBits));
            }
        }
    }
}
