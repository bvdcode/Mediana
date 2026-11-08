// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Aac
{
    internal class AacSbrQmf : IDisposable
    {
        private const int AnalysisLength = 320;
        private static readonly AacFourierPlan AnalysisPlan = new(64);
        private static readonly AacFourierPlan SynthesisPlan = new(128);
        private static readonly Complex[] AnalysisPreRotation = CreateRotation(64, -Math.PI / 64, 0);
        private static readonly Complex[] AnalysisPostRotation = CreateRotation(32, -Math.PI / 128, -Math.PI / 256);
        private static readonly Complex[] SynthesisPreRotation = CreateRotation(64, -255 * Math.PI / 128, 0);
        private static readonly Complex[] SynthesisPostRotation = CreateRotation(128, Math.PI / 128, -127.5 * Math.PI / 128);
        private static readonly Complex[] SingleRatePostRotation = CreateRotation(64, Math.PI / 64, -127.5 * Math.PI / 128);
        private readonly WritableMediaBuffer storage;
        private readonly AacFourierPlan synthesisPlan;
        private readonly Complex[] synthesisPostRotation;

        public AacSbrQmf(MediaBufferAllocator allocator, int synthesisBands = 64)
        {
            (synthesisPlan, synthesisPostRotation) = synthesisBands switch
            {
                32 => (AnalysisPlan, SingleRatePostRotation),
                64 => (SynthesisPlan, SynthesisPostRotation),
                _ => throw new ArgumentOutOfRangeException(nameof(synthesisBands))
            };
            SynthesisBands = synthesisBands;
            storage = allocator.Allocate((AnalysisLength + 20 * synthesisBands) * sizeof(double));
            storage.Memory.Span.Clear();
        }

        public int SynthesisBands { get; }

        public void Analyze(ReadOnlySpan<double> input, Span<Complex> output)
        {
            if (input.Length != 32 || output.Length != 32)
            {
                throw new ArgumentException("SBR analysis requires 32 input samples and 32 output subbands.");
            }
            Span<double> history = MemoryMarshal.Cast<byte, double>(storage.Memory.Span)[..AnalysisLength];
            history[..288].CopyTo(history[32..]);
            for (int index = 0; index < 32; index++)
            {
                history[31 - index] = input[index] * 32768;
            }
            ReadOnlySpan<double> window = AacSbrTables.QmfWindow;
            Span<Complex> work = stackalloc Complex[64];
            for (int index = 0; index < 64; index++)
            {
                double sum = 0;
                for (int block = 0; block < 5; block++)
                {
                    int offset = index + block * 64;
                    sum += history[offset] * window[offset * 2];
                }
                work[AnalysisPlan.Reversed[index]] = sum * AnalysisPreRotation[index];
            }
            AacFourier.Transform(work, AnalysisPlan.Rotations, default);
            for (int band = 0; band < 32; band++)
            {
                output[band] = Complex.Conjugate(work[band]) * AnalysisPostRotation[band];
            }
        }

        public void Synthesize(ReadOnlySpan<Complex> input, Span<double> output)
        {
            int bands = SynthesisBands;
            if (input.Length != bands || output.Length != bands)
            {
                throw new ArgumentException("SBR synthesis requires matching configured subband and output sample counts.");
            }
            Span<double> history = MemoryMarshal.Cast<byte, double>(storage.Memory.Span)[AnalysisLength..];
            history[..(18 * bands)].CopyTo(history[(2 * bands)..]);
            Span<Complex> work = stackalloc Complex[128];
            work = work[..(2 * bands)];
            work.Clear();
            for (int band = 0; band < bands; band++)
            {
                work[synthesisPlan.Reversed[band]] = Complex.Conjugate(input[band] * SynthesisPreRotation[band]);
            }
            AacFourier.Transform(work, synthesisPlan.Rotations, default);
            for (int index = 0; index < work.Length; index++)
            {
                history[index] = (Complex.Conjugate(work[index]) * synthesisPostRotation[index]).Real / 32;
            }
            ReadOnlySpan<double> window = AacSbrTables.QmfWindow;
            int windowStep = 64 / bands;
            for (int index = 0; index < bands; index++)
            {
                double sum = 0;
                for (int block = 0; block < 5; block++)
                {
                    sum += history[block * 4 * bands + index] * window[block * 128 + index * windowStep];
                    sum += history[block * 4 * bands + index + 3 * bands] * window[block * 128 + index * windowStep + 64];
                }
                output[index] = sum / 32768;
            }
        }

        private static Complex[] CreateRotation(int length, double step, double phase)
        {
            Complex[] result = new Complex[length];
            for (int index = 0; index < length; index++)
            {
                (double sine, double cosine) = Math.SinCos(index * step + phase);
                result[index] = new Complex(cosine, sine);
            }
            return result;
        }

        public void Dispose() => storage.Dispose();
    }
}
