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
        private const int SynthesisLength = 1280;
        private static readonly double[] AnalysisModulation = CreateAnalysisModulation();
        private static readonly double[] SynthesisModulation = CreateSynthesisModulation();
        private readonly WritableMediaBuffer storage;

        public AacSbrQmf(MediaBufferAllocator allocator)
        {
            storage = allocator.Allocate((AnalysisLength + SynthesisLength) * sizeof(double));
            storage.Memory.Span.Clear();
        }

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
            Span<double> polyphase = stackalloc double[64];
            for (int index = 0; index < 64; index++)
            {
                double sum = 0;
                for (int block = 0; block < 5; block++)
                {
                    int offset = index + block * 64;
                    sum += history[offset] * window[offset * 2];
                }
                polyphase[index] = sum;
            }
            for (int band = 0; band < 32; band++)
            {
                output[band] = new Complex(Dot(polyphase, AnalysisModulation.AsSpan(band * 128, 64)),
                    Dot(polyphase, AnalysisModulation.AsSpan(band * 128 + 64, 64)));
            }
        }

        public void Synthesize(ReadOnlySpan<Complex> input, Span<double> output)
        {
            if (input.Length != 64 || output.Length != 64)
            {
                throw new ArgumentException("SBR synthesis requires 64 input subbands and 64 output samples.");
            }
            Span<double> history = MemoryMarshal.Cast<byte, double>(storage.Memory.Span)[AnalysisLength..];
            history[..1152].CopyTo(history[128..]);
            ReadOnlySpan<double> components = MemoryMarshal.Cast<Complex, double>(input);
            for (int index = 0; index < 128; index++)
            {
                history[index] = Dot(components, SynthesisModulation.AsSpan(index * 128, 128));
            }
            ReadOnlySpan<double> window = AacSbrTables.QmfWindow;
            for (int index = 0; index < 64; index++)
            {
                double sum = 0;
                for (int block = 0; block < 5; block++)
                {
                    sum += history[block * 256 + index] * window[block * 128 + index];
                    sum += history[block * 256 + index + 192] * window[block * 128 + index + 64];
                }
                output[index] = sum / 32768;
            }
        }

        private static double Dot(ReadOnlySpan<double> first, ReadOnlySpan<double> second)
        {
            Vector<double> sum = Vector<double>.Zero;
            int index = 0;
            if (Vector.IsHardwareAccelerated)
            {
                for (; index <= first.Length - Vector<double>.Count; index += Vector<double>.Count)
                {
                    sum += new Vector<double>(first.Slice(index, Vector<double>.Count)) *
                        new Vector<double>(second.Slice(index, Vector<double>.Count));
                }
            }
            double result = Vector.Sum(sum);
            for (; index < first.Length; index++)
            {
                result += first[index] * second[index];
            }
            return result;
        }

        private static double[] CreateAnalysisModulation()
        {
            double[] result = new double[32 * 128];
            for (int band = 0; band < 32; band++)
            {
                for (int index = 0; index < 64; index++)
                {
                    double angle = Math.PI / 64 * (band + 0.5) * (2 * index - 0.5);
                    result[band * 128 + index] = Math.Cos(angle);
                    result[band * 128 + index + 64] = Math.Sin(angle);
                }
            }
            return result;
        }

        private static double[] CreateSynthesisModulation()
        {
            double[] result = new double[128 * 128];
            for (int index = 0; index < 128; index++)
            {
                for (int band = 0; band < 64; band++)
                {
                    double angle = Math.PI / 128 * (band + 0.5) * (2 * index - 255);
                    result[index * 128 + band * 2] = Math.Cos(angle) / 32;
                    result[index * 128 + band * 2 + 1] = -Math.Sin(angle) / 32;
                }
            }
            return result;
        }

        public void Dispose() => storage.Dispose();
    }
}
