// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Aac
{
    internal class AacPsState : IDisposable
    {
        private const int HybridHistory = 6 * 5;
        private const int Coefficients = HybridHistory + AacPsDecorrelator.StateSamples;
        private const int PhaseHistory = Coefficients + AacPsMixing.CoefficientCount;
        private const int ComplexSamples = PhaseHistory + AacPsMixing.CoefficientCount;
        private readonly WritableMediaBuffer storage;
        private readonly AacSbrQmf right;
        private int previousBands = 20;
        private int clock;
        private bool started;

        public AacPsState(MediaBufferAllocator allocator, int synthesisBands)
        {
            storage = allocator.Allocate(ComplexSamples * 16 + (3 * AacPsMixing.MaximumBands + AacPsMixing.CoefficientCount) * sizeof(double));
            bool succeeded = false;
            try
            {
                storage.Memory.Span.Clear();
                Span<Complex> state = MemoryMarshal.Cast<byte, Complex>(storage.Memory.Span[..(ComplexSamples * 16)]);
                state.Slice(PhaseHistory, AacPsMixing.CoefficientCount).Fill(Complex.One);
                state.Slice(Coefficients, 2 * AacPsMixing.MaximumBands).Fill(Complex.One);
                MemoryMarshal.Cast<byte, double>(storage.Memory.Span[(ComplexSamples * 16)..]).Slice(3 * AacPsMixing.MaximumBands,
                    2 * AacPsMixing.MaximumBands).Fill(1);
                right = new AacSbrQmf(allocator, synthesisBands);
                succeeded = true;
            }
            finally
            {
                if (!succeeded)
                {
                    storage.Dispose();
                }
            }
        }

        public void Process(ReadOnlySpan<Complex> input, AacPsData? data, bool hasData, int stopBand,
            AacSbrQmf left, Span<float> output, CancellationToken cancellationToken)
        {
            Span<Complex> state = MemoryMarshal.Cast<byte, Complex>(storage.Memory.Span[..(ComplexSamples * 16)]);
            Span<Complex> history = state[..HybridHistory];
            Span<Complex> decorrelation = state.Slice(HybridHistory, AacPsDecorrelator.StateSamples);
            Span<Complex> previous = state.Slice(Coefficients, AacPsMixing.CoefficientCount);
            Span<Complex> phases = state.Slice(PhaseHistory, AacPsMixing.CoefficientCount);
            Span<double> realState = MemoryMarshal.Cast<byte, double>(storage.Memory.Span[(ComplexSamples * 16)..]);
            Span<double> transients = realState[..(3 * AacPsMixing.MaximumBands)];
            Span<double> realCoefficients = realState[(3 * AacPsMixing.MaximumBands)..];
            if (hasData && data?.Independent == true)
            {
                started = true;
            }
            int bands = data?.StereoBands ?? 20;
            Span<Complex> target = stackalloc Complex[AacPsMixing.CoefficientCount];
            if (bands != previousBands)
            {
                Span<double> mapped = stackalloc double[AacPsMixing.MaximumBands];
                for (int coefficient = 0; coefficient < 4; coefficient++)
                {
                    Span<Complex> current = previous.Slice(coefficient * AacPsMixing.MaximumBands, AacPsMixing.MaximumBands);
                    Span<double> real = realCoefficients.Slice(coefficient * AacPsMixing.MaximumBands, AacPsMixing.MaximumBands);
                    AacPsBandMapping.Map<double>(real, previousBands, mapped, bands);
                    mapped.CopyTo(real);
                    for (int band = 0; band < bands; band++)
                    {
                        current[band] = real[band];
                    }
                }
                phases.Fill(Complex.One);
                decorrelation.Clear();
                transients.Clear();
                clock = 0;
                previousBands = bands;
            }
            AacPsDecorrelator.ClearUpperBands(decorrelation, stopBand + (bands == 20 ? 7 : 27), bands);
            Span<Complex> mono = stackalloc Complex[91];
            Span<Complex> diffuse = stackalloc Complex[91];
            Span<Complex> leftHybrid = stackalloc Complex[91];
            Span<Complex> rightHybrid = stackalloc Complex[91];
            Span<Complex> qmf = stackalloc Complex[64];
            Span<double> pcm = stackalloc double[64];
            ReadOnlySpan<sbyte> map = AacPsBandMapping.Parameters(bands);
            int envelopeIndex = 0;
            int start = 0;
            int end = NextEnvelope(data, hasData, envelopeIndex, started, previous, phases, target, realCoefficients);
            if (hasData && data?.TransmittedEnvelopeCount == 0)
            {
                target.CopyTo(previous);
            }
            for (int slot = 0; slot < 32; slot++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (slot == end)
                {
                    target.CopyTo(previous);
                    start = end;
                    end = NextEnvelope(data, hasData, ++envelopeIndex, started, previous, phases, target, realCoefficients);
                }
                AacPsHybrid.Analyze(input, history, slot, bands, mono);
                AacPsDecorrelator.Process(mono, diffuse, decorrelation, transients, clock, bands);
                clock = (clock + 1) % 420;
                double fraction = (double)(slot - start + 1) / (end - start);
                for (int band = 0; band < map.Length; band++)
                {
                    int parameter = AacPsDecorrelator.ParameterIndex(map[band]);
                    Complex h11 = Interpolate(previous, target, parameter, fraction, map[band] < 0);
                    Complex h12 = Interpolate(previous, target, AacPsMixing.MaximumBands + parameter, fraction, map[band] < 0);
                    Complex h21 = Interpolate(previous, target, 2 * AacPsMixing.MaximumBands + parameter, fraction, map[band] < 0);
                    Complex h22 = Interpolate(previous, target, 3 * AacPsMixing.MaximumBands + parameter, fraction, map[band] < 0);
                    leftHybrid[band] = h11 * mono[band] + h21 * diffuse[band];
                    rightHybrid[band] = h12 * mono[band] + h22 * diffuse[band];
                }
                AacPsHybrid.Synthesize(leftHybrid, bands, qmf);
                left.Synthesize(qmf[..left.SynthesisBands], pcm[..left.SynthesisBands]);
                Write(pcm[..left.SynthesisBands], output, slot, 0);
                AacPsHybrid.Synthesize(rightHybrid, bands, qmf);
                right.Synthesize(qmf[..right.SynthesisBands], pcm[..right.SynthesisBands]);
                Write(pcm[..right.SynthesisBands], output, slot, 1);
            }
            target.CopyTo(previous);
            for (int slot = 0; slot < 6; slot++)
            {
                input.Slice((26 + slot) * 64, 5).CopyTo(history.Slice(slot * 5, 5));
            }
        }

        public void Dispose()
        {
            right.Dispose();
            storage.Dispose();
        }

        private static int NextEnvelope(AacPsData? data, bool hasData, int index, bool started,
            ReadOnlySpan<Complex> previous, Span<Complex> phases, Span<Complex> target, Span<double> realCoefficients)
        {
            if (!started || !hasData || data is null || index >= data.EnvelopeCount)
            {
                previous.CopyTo(target);
                return 32;
            }
            AacPsEnvelope envelope = data.Envelopes[index];
            if (data.TransmittedEnvelopeCount == 0)
            {
                if (data.Header?.IntensityMode is not null && data.Header.CoherenceMode is not null)
                {
                    for (int coefficient = 0; coefficient < target.Length; coefficient++)
                    {
                        target[coefficient] = data.PhaseEnabled ? previous[coefficient] : realCoefficients[coefficient];
                    }
                    return 32;
                }
                Span<Complex> heldPhase = stackalloc Complex[2 * AacPsMixing.MaximumBands];
                for (int band = 0; band < data.StereoBands; band++)
                {
                    for (int channel = 0; channel < 2; channel++)
                    {
                        int first = channel * AacPsMixing.MaximumBands + band;
                        int second = first + 2 * AacPsMixing.MaximumBands;
                        int coefficient = Math.Abs(realCoefficients[first]) >= Math.Abs(realCoefficients[second]) ? first : second;
                        heldPhase[first] = data.PhaseEnabled && realCoefficients[coefficient] != 0 ?
                            previous[coefficient] / realCoefficients[coefficient] : Complex.One;
                    }
                }
                AacPsMixing.Calculate(data, envelope, target, phases, realCoefficients, false);
                for (int coefficient = 0; coefficient < 4; coefficient++)
                {
                    int channel = coefficient % 2;
                    for (int band = 0; band < data.StereoBands; band++)
                    {
                        target[coefficient * AacPsMixing.MaximumBands + band] *= heldPhase[channel * AacPsMixing.MaximumBands + band];
                    }
                }
            }
            else
            {
                AacPsMixing.Calculate(data, envelope, target, phases, realCoefficients);
            }
            return envelope.EndSlot;
        }

        private static Complex Interpolate(ReadOnlySpan<Complex> previous, ReadOnlySpan<Complex> target,
            int index, double fraction, bool conjugate)
        {
            Complex value = previous[index] + (target[index] - previous[index]) * fraction;
            return conjugate ? Complex.Conjugate(value) : value;
        }

        private static void Write(ReadOnlySpan<double> samples, Span<float> output, int slot, int channel)
        {
            for (int index = 0; index < samples.Length; index++)
            {
                float value = (float)samples[index];
                if (!float.IsFinite(value))
                {
                    throw new InvalidDataException("PS synthesis produced a nonfinite PCM sample.");
                }
                if (!output.IsEmpty)
                {
                    output[(slot * samples.Length + index) * 2 + channel] = value;
                }
            }
        }
    }
}
