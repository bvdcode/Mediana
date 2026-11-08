// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Aac
{
    internal class AacSbrAdjustment
    {
        private readonly bool[] previousHarmonics = new bool[64];
        private int noiseIndex;
        private int sinePhase;
        private int previousAttack = -1;
        private int historyIndex;
        private bool initialized;

        public void Process(Span<Complex> samples, AacSbrChannelData data, AacSbrHeader header,
            AacSbrFrequencyTables tables, AacSbrPatches patches, Span<double> gainHistory, Span<double> noiseHistory, bool reset)
        {
            if (reset)
            {
                initialized = false;
                noiseIndex = 0;
                Array.Clear(previousHarmonics);
            }
            Span<double> estimated = stackalloc double[64];
            Span<double> reference = stackalloc double[64];
            Span<double> gain = stackalloc double[64];
            Span<double> noise = stackalloc double[64];
            Span<double> sine = stackalloc double[64];
            Span<bool> harmonics = stackalloc bool[64];
            Span<int> limiter = stackalloc int[64];
            int limiterCount = patches.BuildLimiter(tables, header.LimiterBands, limiter);
            double limiterGain = header.LimiterGains switch
            {
                0 => 0.5011932025,
                1 => 1,
                2 => 1.9952692516,
                3 => 1e20,
                _ => throw new InvalidDataException("The SBR limiter gain setting is invalid.")
            };
            for (int envelope = 0; envelope < data.Grid.EnvelopeCount; envelope++)
            {
                int start = data.Grid.Borders[envelope] * 2;
                int end = data.Grid.Borders[envelope + 1] * 2;
                bool attack = envelope == data.Grid.AttackEnvelope || envelope == previousAttack;
                ReadOnlySpan<int> borders = data.Grid.HighResolution[envelope] ? tables.High : tables.Low;
                Estimate(samples, estimated, borders, start, end, header.InterpolateFrequency);
                harmonics.Clear();
                for (int band = 0; band < tables.HighCount; band++)
                {
                    if (data.Harmonics[band] && (envelope >= data.Grid.AttackEnvelope || previousHarmonics[band]))
                    {
                        harmonics[(tables.High[band] + tables.High[band + 1]) / 2] = true;
                    }
                }
                int noiseEnvelope = data.Grid.NoiseEnvelopeCount == 2 && start >= data.Grid.NoiseBorders[1] * 2 ? 1 : 0;
                int noiseBand = 0;
                for (int band = 0; band < borders.Length - 1; band++)
                {
                    bool sinePresent = harmonics[borders[band]..borders[band + 1]].Contains(true);
                    double energy = data.Energy[envelope * 64 + band];
                    for (int frequency = borders[band]; frequency < borders[band + 1]; frequency++)
                    {
                        while (frequency >= tables.Noise[noiseBand + 1])
                        {
                            noiseBand++;
                        }
                        double ratio = data.NoiseRatio[noiseEnvelope * 5 + noiseBand];
                        reference[frequency] = energy;
                        noise[frequency] = energy * ratio / (1 + ratio);
                        sine[frequency] = harmonics[frequency] ? energy / (1 + ratio) : 0;
                        gain[frequency] = sinePresent ? noise[frequency] / (1 + estimated[frequency]) :
                            energy / ((1 + estimated[frequency]) * (attack ? 1 : 1 + ratio));
                    }
                }
                Limit(estimated, reference, gain, noise, sine, limiter[..(limiterCount + 1)], limiterGain, attack);
                for (int band = tables.CrossoverBand; band < tables.StopBand; band++)
                {
                    gain[band] = Math.Sqrt(gain[band]);
                    noise[band] = Math.Sqrt(noise[band]);
                    sine[band] = Math.Sqrt(sine[band]);
                }
                if (!initialized)
                {
                    for (int index = 0; index < 5; index++)
                    {
                        gain.CopyTo(gainHistory.Slice(index * 64, 64));
                        noise.CopyTo(noiseHistory.Slice(index * 64, 64));
                    }
                    initialized = true;
                }
                for (int slot = start; slot < end; slot++)
                {
                    gain.CopyTo(gainHistory.Slice(historyIndex * 64, 64));
                    noise.CopyTo(noiseHistory.Slice(historyIndex * 64, 64));
                    Assemble(samples.Slice((slot + 2) * 64, 64), tables, gainHistory, noiseHistory, sine,
                        !header.SmoothingMode && !attack, attack);
                    historyIndex = (historyIndex + 1) % 5;
                }
            }
            data.Harmonics.CopyTo(previousHarmonics, 0);
            previousAttack = data.Grid.AttackEnvelope == data.Grid.EnvelopeCount ? 0 : -1;
        }

        private static void Estimate(ReadOnlySpan<Complex> samples, Span<double> energy, ReadOnlySpan<int> borders,
            int start, int end, bool interpolate)
        {
            for (int band = borders[0]; band < borders[^1]; band++)
            {
                double sum = 0;
                for (int slot = start; slot < end; slot++)
                {
                    Complex value = samples[(slot + 2) * 64 + band];
                    sum += value.Real * value.Real + value.Imaginary * value.Imaginary;
                }
                energy[band] = sum / (end - start);
            }
            if (!interpolate)
            {
                for (int band = 0; band < borders.Length - 1; band++)
                {
                    double sum = 0;
                    for (int frequency = borders[band]; frequency < borders[band + 1]; frequency++)
                    {
                        sum += energy[frequency];
                    }
                    energy[borders[band]..borders[band + 1]].Fill(sum / (borders[band + 1] - borders[band]));
                }
            }
        }

        private static void Limit(ReadOnlySpan<double> estimated, ReadOnlySpan<double> reference, Span<double> gain,
            Span<double> noise, Span<double> sine, ReadOnlySpan<int> borders, double limiterGain, bool attack)
        {
            for (int band = 0; band < borders.Length - 1; band++)
            {
                double sumReference = 1;
                double sumEstimated = 1;
                for (int frequency = borders[band]; frequency < borders[band + 1]; frequency++)
                {
                    sumReference += reference[frequency];
                    sumEstimated += estimated[frequency];
                }
                double maximumGain = Math.Min(1e10, limiterGain * sumReference / sumEstimated);
                double sumAdjusted = 0;
                for (int frequency = borders[band]; frequency < borders[band + 1]; frequency++)
                {
                    if (gain[frequency] > maximumGain)
                    {
                        noise[frequency] *= maximumGain / gain[frequency];
                        gain[frequency] = maximumGain;
                    }
                    sumAdjusted += gain[frequency] * estimated[frequency] + sine[frequency];
                    if (!attack && sine[frequency] == 0)
                    {
                        sumAdjusted += noise[frequency];
                    }
                }
                double boost = Math.Min(2.51188643, sumReference / Math.Max(sumAdjusted, 1e-12));
                for (int frequency = borders[band]; frequency < borders[band + 1]; frequency++)
                {
                    gain[frequency] *= boost;
                    noise[frequency] *= boost;
                    sine[frequency] *= boost;
                }
            }
        }

        private void Assemble(Span<Complex> samples, AacSbrFrequencyTables tables, ReadOnlySpan<double> gainHistory,
            ReadOnlySpan<double> noiseHistory, ReadOnlySpan<double> sine, bool smooth, bool attack)
        {
            ReadOnlySpan<double> weights = [0.33333333333333, 0.30150283239582, 0.21816949906249, 0.11516383427084, 0.03183050093751];
            ReadOnlySpan<double> random = AacSbrTables.Noise;
            for (int band = tables.CrossoverBand; band < tables.StopBand; band++)
            {
                double gain = gainHistory[historyIndex * 64 + band];
                double noise = noiseHistory[historyIndex * 64 + band];
                if (smooth)
                {
                    gain = noise = 0;
                    for (int index = 0; index < 5; index++)
                    {
                        int offset = ((historyIndex + 5 - index) % 5) * 64 + band;
                        gain += weights[index] * gainHistory[offset];
                        noise += weights[index] * noiseHistory[offset];
                    }
                }
                Complex value = samples[band] * gain;
                noiseIndex = (noiseIndex + 1) & 511;
                if (sine[band] != 0)
                {
                    double amplitude = sine[band];
                    double imaginarySign = (band & 1) == 0 ? 1 : -1;
                    value += sinePhase switch
                    {
                        0 => new Complex(amplitude, 0),
                        1 => new Complex(0, imaginarySign * amplitude),
                        2 => new Complex(-amplitude, 0),
                        3 => new Complex(0, -imaginarySign * amplitude),
                        _ => throw new InvalidOperationException("The SBR sinusoid phase is invalid.")
                    };
                }
                else if (!attack)
                {
                    value += new Complex(random[noiseIndex * 2], random[noiseIndex * 2 + 1]) * noise;
                }
                samples[band] = value;
            }
            sinePhase = (sinePhase + 1) & 3;
        }
    }
}
