// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Aac
{
    internal class AacSbrChannelState : IDisposable
    {
        private const int LowLength = 40 * 32;
        private const int HighLength = 40 * 64;
        private const int HistoryLength = 5 * 64;
        private readonly WritableMediaBuffer storage;
        private readonly AacSbrQmf qmf;
        private readonly AacSbrAdjustment adjustment = new();
        private readonly double[] chirp = new double[5];
        private readonly int[] previousFiltering = new int[5];
        private int previousCrossover = 32;
        private int previousStop = 32;
        private AacPsState? parametricStereo;
        private readonly MediaBufferAllocator allocator;

        public AacSbrChannelState(MediaBufferAllocator allocator, int synthesisBands = 64)
        {
            this.allocator = allocator;
            storage = allocator.Allocate((LowLength + HighLength) * 2 * sizeof(double) + HistoryLength * 2 * sizeof(double));
            bool succeeded = false;
            try
            {
                storage.Memory.Span.Clear();
                qmf = new AacSbrQmf(allocator, synthesisBands);
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

        public void Process(ReadOnlySpan<double> core, Span<float> output, int channelIndex, int channelCount,
            AacSbrElement element, AacSbrChannelData data, AacSbrPatches patches, bool stereoReconstruction, CancellationToken cancellationToken)
        {
            Span<byte> bytes = storage.Memory.Span;
            Span<Complex> low = MemoryMarshal.Cast<byte, Complex>(bytes[..(LowLength * 16)]);
            Span<Complex> high = MemoryMarshal.Cast<byte, Complex>(bytes.Slice(LowLength * 16, HighLength * 16));
            Span<double> history = MemoryMarshal.Cast<byte, double>(bytes[((LowLength + HighLength) * 16)..]);
            for (int slot = 0; slot < 32; slot++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                qmf.Analyze(core.Slice(slot * 32, 32), low.Slice((slot + 8) * 32, 32));
            }
            AacSbrHighFrequency.Generate(low, high, data, element.Tables, patches, chirp, previousFiltering);
            AacSbrHeader header = element.Header ?? throw new InvalidOperationException("The SBR header is missing.");
            adjustment.Process(high, data, header, element.Tables, patches, history[..HistoryLength], history[HistoryLength..], element.FrequencyReset);
            Span<Complex> subbands = stackalloc Complex[64];
            Span<double> samples = stackalloc double[64];
            Span<Complex> psInput = stackalloc Complex[stereoReconstruction ? 38 * 64 : 0];
            for (int slot = 0; slot < 32; slot++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int crossover = slot < data.Grid.Borders[0] * 2 ? previousCrossover : element.Tables.CrossoverBand;
                int stop = slot < data.Grid.Borders[0] * 2 ? previousStop : element.Tables.StopBand;
                subbands.Clear();
                low.Slice((slot + 2) * 32, crossover).CopyTo(subbands);
                high.Slice((slot + 2) * 64 + crossover, stop - crossover).CopyTo(subbands[crossover..]);
                if (stereoReconstruction)
                {
                    subbands.CopyTo(psInput.Slice(slot * 64, 64));
                    continue;
                }
                int bands = qmf.SynthesisBands;
                qmf.Synthesize(subbands[..bands], samples[..bands]);
                for (int index = 0; index < bands; index++)
                {
                    float value = (float)samples[index];
                    if (!float.IsFinite(value))
                    {
                        throw new InvalidDataException("SBR synthesis produced a nonfinite PCM sample.");
                    }
                    if (!output.IsEmpty)
                    {
                        output[(slot * bands + index) * channelCount + channelIndex] = value;
                    }
                }
            }
            if (stereoReconstruction)
            {
                psInput[(32 * 64)..].Clear();
                for (int slot = 32; slot < 38; slot++)
                {
                    low.Slice((slot + 2) * 32, 5).CopyTo(psInput.Slice(slot * 64, 5));
                }
                parametricStereo ??= new AacPsState(allocator, qmf.SynthesisBands);
                parametricStereo.Process(psInput, element.ParametricStereo, element.HasParametricStereoData,
                    element.Tables.StopBand, qmf, output, cancellationToken);
            }
            previousCrossover = element.Tables.CrossoverBand;
            previousStop = element.Tables.StopBand;
            low.Slice(32 * 32, 8 * 32).CopyTo(low);
            high.Slice(32 * 64, 8 * 64).CopyTo(high);
        }

        public void Dispose()
        {
            parametricStereo?.Dispose();
            qmf.Dispose();
            storage.Dispose();
        }
    }
}
