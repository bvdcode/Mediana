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
        private const int History = CeltPitchSearch.HistorySamples;
        private const int SignalStride = History + MaximumSamples + CeltTransformPlan.Overlap;
        private readonly CeltTransformPlan plan;
        private readonly CeltPulseCodebook codebook;
        private readonly WritableMediaBuffer storage;
        private readonly bool disableInversion;
        private readonly CeltRecoveryState recovery;
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
            bool succeeded = false;
            try { recovery = new(allocator); Reset(); succeeded = true; }
            finally { if (!succeeded) { storage.Dispose(); } }
        }

        public int OutputChannels { get; }
        public uint FinalRange => seed;
        private Span<float> Values => MemoryMarshal.Cast<byte, float>(storage.Span);

        public int Decode(ReadOnlySpan<byte> frame, CeltBandMode mode, Span<float> output, CancellationToken cancellationToken = default)
        {
            if (mode.FirstBand != 0) { throw new NotSupportedException("A standalone CELT frame must start at band zero."); }
            if (frame.Length <= 1) { throw new NotSupportedException("A missing CELT frame requires packet-loss reconstruction."); }
            ArgumentOutOfRangeException.ThrowIfGreaterThan(frame.Length, 1275, nameof(frame));
            OpusRangeDecoder range = new(frame);
            return DecodeLayer(ref range, mode, output, false, cancellationToken);
        }

        public int DecodeLayer(ref OpusRangeDecoder range, CeltBandMode mode, Span<float> output, bool accumulate,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) { throw new InvalidOperationException("Reset the CELT decoder after a failed frame."); }
            if (mode.Channels is not (1 or 2)) { throw new ArgumentException("The CELT band mode is uninitialized.", nameof(mode)); }
            int samples = CeltBandTables.ShortTransformSize << mode.FrameShift;
            ArgumentOutOfRangeException.ThrowIfLessThan(output.Length, samples * OutputChannels, nameof(output));
            cancellationToken.ThrowIfCancellationRequested();
            bool succeeded = false;
            try
            {
                if (recovery.LostDuration == 0) { recovery.SkipPitch = false; }
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
                CeltFrameSyntax syntax = CeltFrameSyntaxReader.Read(ref range, mode, energies, resolutions, boosts, new(shape, fine, priorities),
                    new(Values.Slice(EnergyCount, EnergyCount), Values.Slice(2 * EnergyCount, EnergyCount), recovery.LostDuration));
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
                if (recovery.FoldOverlap) { CeltConcealment.Fold(signal, OutputChannels, SignalStride, currentFilter, plan.Window); }
                CeltSynthesis.Apply(plan, mode, syntax.Flags.IsTransient, OutputChannels, scratch,
                    signal[History..], SignalStride, Values.Slice(TransformOffset, MaximumSamples));
                Filter(signal, samples, syntax.Flags.PostFilter);
                CeltDeemphasis.Apply(signal[History..], samples, OutputChannels, SignalStride,
                    output, Values.Slice(DeemphasisOffset, 2), accumulate);
                UpdateEnergyHistory(mode, syntax.Flags.IsTransient);
                recovery.Receive(mode, energies);
                for (int channel = 0; channel < OutputChannels; channel++)
                {
                    Span<float> values = signal.Slice(channel * SignalStride, SignalStride);
                    values.Slice(samples, History + CeltTransformPlan.Overlap / 2).CopyTo(values);
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

        public int Conceal(CeltBandMode mode, Span<float> output, bool accumulate = false, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) { throw new InvalidOperationException("Reset the CELT decoder after a failed frame."); }
            if (mode.Channels is not (1 or 2)) { throw new ArgumentException("The CELT band mode is uninitialized.", nameof(mode)); }
            int samples = CeltBandTables.ShortTransformSize << mode.FrameShift;
            ArgumentOutOfRangeException.ThrowIfLessThan(output.Length, samples * OutputChannels, nameof(output));
            cancellationToken.ThrowIfCancellationRequested();
            bool succeeded = false;
            try
            {
                Span<float> signal = Values[SignalOffset..];
                if (recovery.LostDuration >= 40 || mode.FirstBand != 0 || recovery.SkipPitch)
                {
                    CeltBandMode noiseMode = new(OutputChannels, mode.FrameShift, mode.FirstBand, mode.EndBand);
                    Span<float> coefficients = Values.Slice(SpectrumOffset, samples * OutputChannels);
                    Span<float> frequency = Values.Slice(ScratchOffset, 2 * MaximumSamples);
                    CeltConcealment.Noise(recovery, noiseMode, Values[..EnergyCount], coefficients, ref seed);
                    if (recovery.FoldOverlap) { CeltConcealment.Fold(signal, OutputChannels, SignalStride, currentFilter, plan.Window); }
                    CeltBandDenormalizer.Apply(noiseMode, coefficients, Values[..EnergyCount], frequency, false);
                    CeltSynthesis.Apply(plan, noiseMode, false, OutputChannels, frequency,
                        signal[History..], SignalStride, Values.Slice(TransformOffset, MaximumSamples));
                    Filter(signal, samples, currentFilter); oldFilter = currentFilter; recovery.FoldOverlap = false;
                }
                else { CeltConcealment.Periodic(recovery, signal, OutputChannels, SignalStride, samples, plan.Window); }
                CeltDeemphasis.Apply(signal[History..], samples, OutputChannels, SignalStride,
                    output, Values.Slice(DeemphasisOffset, 2), accumulate);
                for (int channel = 0; channel < OutputChannels; channel++)
                {
                    Span<float> plane = signal.Slice(channel * SignalStride, SignalStride);
                    plane.Slice(samples, History + CeltTransformPlan.Overlap).CopyTo(plane);
                }
                recovery.LostDuration = Math.Min(10000, recovery.LostDuration + (1 << mode.FrameShift));
                succeeded = true; return samples;
            }
            finally { if (!succeeded) { faulted = true; } }
        }

        private void Filter(Span<float> signal, int samples, CeltPostFilterParameters next)
        {
            for (int channel = 0; channel < OutputChannels; channel++)
            {
                Span<float> values = signal.Slice(channel * SignalStride, SignalStride);
                CeltPitchPostfilter.Apply(values, History, CeltBandTables.ShortTransformSize,
                    oldFilter, currentFilter, plan.Window);
                if (samples > CeltBandTables.ShortTransformSize)
                {
                    CeltPitchPostfilter.Apply(values, History + CeltBandTables.ShortTransformSize,
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
                energies.Slice(end, mode.FirstBand).Clear();
                previous.Slice(end, mode.FirstBand).Fill(-28);
                previous2.Slice(end, mode.FirstBand).Fill(-28);
                energies.Slice(end + mode.EndBand, CeltBandTables.BandCount - mode.EndBand).Clear();
                previous.Slice(end + mode.EndBand, CeltBandTables.BandCount - mode.EndBand).Fill(-28);
                previous2.Slice(end + mode.EndBand, CeltBandTables.BandCount - mode.EndBand).Fill(-28);
            }
        }

        public void Reset()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Values.Clear(); recovery.Reset();
            Values.Slice(EnergyCount, 2 * EnergyCount).Fill(-28);
            seed = 0; oldFilter = default; currentFilter = default; faulted = false;
        }

        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            recovery.Dispose(); storage.Dispose();
        }
    }
}
