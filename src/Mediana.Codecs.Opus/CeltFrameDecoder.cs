// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Opus
{
    /// <summary>Sequential CELT frame synthesis. Prepared transform and pulse tables are borrowed for this decoder's lifetime.</summary>
    internal class CeltFrameDecoder : IDisposable
    {
        private const int MaximumSamples = CeltBandTables.ShortTransformSize << 3;
        private const int EnergyCount = 2 * CeltBandTables.BandCount;
        private const int DeemphasisOffset = 3 * EnergyCount;
        private const int SpectrumOffset = DeemphasisOffset + 2;
        private const int ScratchOffset = SpectrumOffset + 2 * MaximumSamples;
        private const int TransformOffset = ScratchOffset + 2 * MaximumSamples;
        private const int SignalOffset = TransformOffset + MaximumSamples;
        private const int SignalStride = CeltPitchPostfilter.HistorySamples + MaximumSamples + CeltTransformPlan.Overlap / 2;
        private readonly CeltTransformPlan plan;
        private readonly CeltPulseCodebook codebook;
        private readonly WritableMediaBuffer storage;
        private readonly bool disableInversion;
        private CeltPostFilterParameters oldFilter, currentFilter;
        private uint seed;
        private bool disposed, faulted;

        public CeltFrameDecoder(CeltTransformPlan plan, CeltPulseCodebook codebook, MediaBufferAllocator allocator,
            int outputChannels, bool disableInversion)
        {
            ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(codebook); ArgumentNullException.ThrowIfNull(allocator);
            if (outputChannels is not (1 or 2)) { throw new ArgumentOutOfRangeException(nameof(outputChannels)); }
            if (codebook.MaximumDimensions < CeltBandTables.MaximumBandDimensions || codebook.MaximumPulses < 128)
            {
                throw new ArgumentException("The CELT pulse table must cover all standard bands and pulse counts.", nameof(codebook));
            }
            this.plan = plan; this.codebook = codebook; this.disableInversion = disableInversion;
            OutputChannels = outputChannels;
            storage = allocator.Allocate((SignalOffset + SignalStride * outputChannels) * sizeof(float));
            Reset();
        }

        public int OutputChannels { get; }
        public uint FinalRange => seed;
        private Span<float> Values => MemoryMarshal.Cast<byte, float>(storage.Span);

        public int Decode(ReadOnlySpan<byte> frame, CeltBandMode mode, Span<float> output, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) { throw new InvalidOperationException("Reset the CELT decoder after a failed frame."); }
            if (mode.Channels is not (1 or 2)) { throw new ArgumentException("The CELT band mode is uninitialized.", nameof(mode)); }
            if (mode.FirstBand != 0) { throw new NotSupportedException("A hybrid CELT layer requires the shared SILK entropy state."); }
            if (frame.Length <= 1) { throw new NotSupportedException("A missing CELT frame requires packet-loss reconstruction."); }
            ArgumentOutOfRangeException.ThrowIfGreaterThan(frame.Length, 1275, nameof(frame));
            int samples = CeltBandTables.ShortTransformSize << mode.FrameShift;
            ArgumentOutOfRangeException.ThrowIfLessThan(output.Length, samples * OutputChannels, nameof(output));
            cancellationToken.ThrowIfCancellationRequested();
            bool succeeded = false;
            try
            {
                Span<float> energies = Values[..EnergyCount];
                if (mode.Channels == 1)
                {
                    for (int band = 0; band < CeltBandTables.BandCount; band++)
                    {
                        energies[band] = Math.Max(energies[band], energies[CeltBandTables.BandCount + band]);
                    }
                }
                Span<int> resolutions = stackalloc int[21], boosts = stackalloc int[21], shape = stackalloc int[21], fine = stackalloc int[21];
                Span<byte> priorities = stackalloc byte[21], masks = stackalloc byte[42];
                resolutions.Clear(); boosts.Clear(); shape.Clear(); fine.Clear(); priorities.Clear();
                OpusRangeDecoder range = new(frame);
                CeltFrameSyntax syntax = CeltFrameSyntaxReader.Read(ref range, mode, energies, resolutions, boosts, new(shape, fine, priorities));
                Span<float> coefficients = Values.Slice(SpectrumOffset, samples * mode.Channels);
                Span<float> scratch = Values.Slice(ScratchOffset, 2 * MaximumSamples);
                CeltSpectralBuffers buffers = new(coefficients, scratch, masks);
                CeltSpectralFrameReader.Read(ref range, codebook, mode, syntax, resolutions, shape, buffers, ref seed, disableInversion);
                bool antiCollapse = false;
                if (syntax.Controls.AntiCollapseEighthBits > 0) { antiCollapse = range.ReadRawBits(1) != 0; }
                CeltEnergyReader.ReadFinal(ref range, energies, fine, priorities, mode.Channels, mode.FirstBand, mode.EndBand);
                if (range.BitsUsed > range.FrameBitCount) { throw new InvalidDataException("The CELT frame exceeds its entropy budget."); }
                if (antiCollapse)
                {
                    CeltAntiCollapse.Apply(mode, buffers, energies, Values.Slice(EnergyCount, EnergyCount),
                        Values.Slice(2 * EnergyCount, EnergyCount), shape, seed);
                }
                if (syntax.Flags.IsSilent) { energies[..(mode.Channels * CeltBandTables.BandCount)].Fill(-28); }
                cancellationToken.ThrowIfCancellationRequested();
                CeltBandDenormalizer.Apply(mode, coefficients, energies, scratch, syntax.Flags.IsSilent);
                Span<float> signal = Values[SignalOffset..];
                CeltSynthesis.Apply(plan, mode, syntax.Flags.IsTransient, OutputChannels, scratch,
                    signal[CeltPitchPostfilter.HistorySamples..], SignalStride, Values.Slice(TransformOffset, MaximumSamples));
                Filter(signal, samples, syntax.Flags.PostFilter);
                CeltDeemphasis.Apply(signal[CeltPitchPostfilter.HistorySamples..], samples, OutputChannels, SignalStride,
                    output, Values.Slice(DeemphasisOffset, 2));
                UpdateEnergyHistory(mode, syntax.Flags.IsTransient);
                for (int channel = 0; channel < OutputChannels; channel++)
                {
                    Span<float> values = signal.Slice(channel * SignalStride, SignalStride);
                    values.Slice(samples, CeltPitchPostfilter.HistorySamples + CeltTransformPlan.Overlap / 2).CopyTo(values);
                }
                seed = range.Range;
                succeeded = true;
                return samples;
            }
            finally
            {
                if (!succeeded) { faulted = true; }
            }
        }

        private void Filter(Span<float> signal, int samples, CeltPostFilterParameters next)
        {
            for (int channel = 0; channel < OutputChannels; channel++)
            {
                Span<float> values = signal.Slice(channel * SignalStride, SignalStride);
                CeltPitchPostfilter.Apply(values, CeltPitchPostfilter.HistorySamples, CeltBandTables.ShortTransformSize,
                    oldFilter, currentFilter, plan.Window);
                if (samples > CeltBandTables.ShortTransformSize)
                {
                    CeltPitchPostfilter.Apply(values, CeltPitchPostfilter.HistorySamples + CeltBandTables.ShortTransformSize,
                        samples - CeltBandTables.ShortTransformSize, currentFilter, next, plan.Window);
                }
            }
            oldFilter = currentFilter; currentFilter = next;
            if (samples > CeltBandTables.ShortTransformSize) { oldFilter = next; }
        }

        private void UpdateEnergyHistory(CeltBandMode mode, bool transient)
        {
            Span<float> energies = Values[..EnergyCount], previous = Values.Slice(EnergyCount, EnergyCount);
            Span<float> previous2 = Values.Slice(2 * EnergyCount, EnergyCount);
            if (mode.Channels == 1) { energies[..CeltBandTables.BandCount].CopyTo(energies[CeltBandTables.BandCount..]); }
            if (transient)
            {
                for (int at = 0; at < EnergyCount; at++) { previous[at] = Math.Min(previous[at], energies[at]); }
            }
            else { previous.CopyTo(previous2); energies.CopyTo(previous); }
            for (int channel = 0; channel < 2; channel++)
            {
                int end = channel * CeltBandTables.BandCount;
                energies.Slice(end + mode.EndBand, CeltBandTables.BandCount - mode.EndBand).Clear();
                previous.Slice(end + mode.EndBand, CeltBandTables.BandCount - mode.EndBand).Fill(-28);
                previous2.Slice(end + mode.EndBand, CeltBandTables.BandCount - mode.EndBand).Fill(-28);
            }
        }

        public void Reset()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Values.Clear();
            Values.Slice(EnergyCount, 2 * EnergyCount).Fill(-28);
            seed = 0; oldFilter = default; currentFilter = default; faulted = false;
        }

        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            storage.Dispose();
        }
    }
}
