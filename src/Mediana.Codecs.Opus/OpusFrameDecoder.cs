// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Opus
{
    internal class OpusFrameDecoder : IDisposable
    {
        private const int FadeSamples = 120;
        private readonly CeltFrameDecoder celt;
        private readonly CeltTransformPlan plan;
        private readonly MediaBufferAllocator allocator;
        private readonly WritableMediaBuffer transitionStorage;
        private SilkFrameDecoder? silk;
        private OpusCodingMode? previousMode;
        private bool previousRedundancy, disposed, faulted;

        public OpusFrameDecoder(CeltTransformPlan plan, CeltPulseCodebook codebook, MediaBufferAllocator allocator, int channels)
        {
            ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(allocator);
            this.plan = plan; this.allocator = allocator; Channels = channels;
            CeltFrameDecoder created = new(plan, codebook, allocator, channels, channels == 1);
            bool succeeded = false;
            try
            {
                transitionStorage = allocator.Allocate(2 * FadeSamples * channels * sizeof(float));
                celt = created; succeeded = true;
            }
            finally { if (!succeeded) { created.Dispose(); } }
        }

        public int Channels { get; }
        public uint FinalRange { get; private set; }

        public int Decode(ReadOnlySpan<byte> frame, OpusPacketConfiguration configuration, Span<float> output,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) { throw new InvalidOperationException("Reset the Opus frame decoder after a failed frame."); }
            if (frame.Length <= 1) { throw new NotSupportedException("An uncoded Opus frame requires packet-loss reconstruction."); }
            ArgumentOutOfRangeException.ThrowIfGreaterThan(frame.Length, 1275, nameof(frame));
            int samples = configuration.SamplesPerFrame;
            ArgumentOutOfRangeException.ThrowIfLessThan(output.Length, samples * Channels, nameof(output));
            cancellationToken.ThrowIfCancellationRequested();
            bool succeeded = false;
            try
            {
                DecodeCore(frame, configuration, output[..(samples * Channels)], cancellationToken);
                succeeded = true;
                return samples;
            }
            finally { if (!succeeded) { faulted = true; } }
        }

        private void DecodeCore(ReadOnlySpan<byte> frame, OpusPacketConfiguration configuration, Span<float> output, CancellationToken token)
        {
            OpusCodingMode mode = configuration.CodingMode;
            int endBand = configuration.Bandwidth switch
            {
                OpusBandwidth.Narrowband => 13,
                OpusBandwidth.Mediumband or OpusBandwidth.Wideband => 17,
                OpusBandwidth.SuperWideband => 19,
                OpusBandwidth.Fullband => 21,
                _ => throw new ArgumentOutOfRangeException(nameof(configuration))
            };
            OpusRangeDecoder range = new(frame);
            OpusTransitionInfo transition = default;
            if (mode != OpusCodingMode.Celt)
            {
                int rate = configuration.Bandwidth switch
                {
                    OpusBandwidth.Narrowband => 8000,
                    OpusBandwidth.Mediumband => 12000,
                    OpusBandwidth.Wideband or OpusBandwidth.SuperWideband or OpusBandwidth.Fullband => 16000,
                    _ => throw new ArgumentOutOfRangeException(nameof(configuration))
                };
                if (silk is null) { silk = new(rate, Channels, allocator); }
                else { silk.SetSampleRate(rate); }
                if (previousMode == OpusCodingMode.Celt) { silk.Reset(); }
                silk.DecodeLayer(ref range, configuration.CodedChannelCount, configuration.SamplesPerFrame, output, token);
                transition = OpusTransitionInfo.Read(ref range, mode == OpusCodingMode.Hybrid);
            }
            bool hasTransition = transition.Bytes != 0;
            bool changesCelt = previousMode.HasValue && previousMode != mode &&
                (previousMode == OpusCodingMode.Celt || mode == OpusCodingMode.Celt);
            if (changesCelt && !hasTransition && !previousRedundancy)
            {
                throw new NotSupportedException("An Opus mode switch without transition redundancy requires packet-loss reconstruction.");
            }
            Span<float> redundant = MemoryMarshal.Cast<byte, float>(transitionStorage.Span);
            uint redundantRange = 0;
            if (hasTransition && transition.AtStart)
            {
                celt.Decode(frame[^transition.Bytes..], new(configuration.CodedChannelCount, 1, 0, endBand), redundant, token);
                redundantRange = celt.FinalRange;
            }
            switch (mode)
            {
                case OpusCodingMode.Celt:
                    if (previousMode.HasValue && previousMode != mode && !previousRedundancy) { celt.Reset(); }
                    celt.DecodeLayer(ref range, CeltBandMode.FromConfiguration(configuration), output, false, token);
                    FinalRange = celt.FinalRange;
                    break;
                case OpusCodingMode.Hybrid:
                    if (previousMode.HasValue && previousMode != mode && !previousRedundancy) { celt.Reset(); }
                    int shift = configuration.SamplesPerFrame switch { 480 => 2, 960 => 3, _ => throw new InvalidDataException("An Opus hybrid frame must contain 10 or 20 milliseconds.") };
                    celt.DecodeLayer(ref range, new(configuration.CodedChannelCount, shift, 17, endBand), output, true, token);
                    FinalRange = celt.FinalRange;
                    break;
                case OpusCodingMode.Silk:
                    if (previousMode == OpusCodingMode.Hybrid && !(hasTransition && transition.AtStart && previousRedundancy))
                    {
                        celt.Decode([255, 255], new(configuration.CodedChannelCount, 0, 0, endBand), redundant, token);
                        for (int at = 0; at < FadeSamples * Channels; at++) { output[at] += redundant[at]; }
                    }
                    FinalRange = range.Range;
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(configuration));
            }
            if (hasTransition && !transition.AtStart)
            {
                celt.Reset();
                celt.Decode(frame[^transition.Bytes..], new(configuration.CodedChannelCount, 1, 0, endBand), redundant, token);
                redundantRange = celt.FinalRange;
                Span<float> tail = output[^(FadeSamples * Channels)..];
                Fade(tail, redundant[(FadeSamples * Channels)..], tail);
            }
            if (hasTransition && transition.AtStart && (previousMode != OpusCodingMode.Silk || previousRedundancy))
            {
                redundant[..(FadeSamples * Channels)].CopyTo(output);
                Span<float> beginning = output.Slice(FadeSamples * Channels, FadeSamples * Channels);
                Fade(redundant[(FadeSamples * Channels)..], beginning, beginning);
            }
            FinalRange ^= redundantRange;
            previousMode = mode; previousRedundancy = hasTransition && !transition.AtStart;
        }

        private void Fade(ReadOnlySpan<float> first, ReadOnlySpan<float> second, Span<float> output)
        {
            for (int sample = 0; sample < FadeSamples; sample++)
            {
                float weight = plan.Window[sample] * plan.Window[sample];
                for (int channel = 0; channel < Channels; channel++)
                {
                    int index = sample * Channels + channel;
                    output[index] = weight * second[index] + (1 - weight) * first[index];
                }
            }
        }

        public void Reset()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            celt.Reset(); silk?.Reset(); transitionStorage.Span.Clear();
            previousMode = null; previousRedundancy = false; FinalRange = 0; faulted = false;
        }

        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true; silk?.Dispose(); celt.Dispose(); transitionStorage.Dispose();
        }
    }
}
