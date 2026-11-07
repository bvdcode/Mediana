// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Vorbis
{
    internal class VorbisFrameDecoder : IDisposable
    {
        private readonly VorbisIdentification identification;
        private readonly VorbisSetupCapture setup;
        private readonly VorbisTransformPlan smallPlan, largePlan;
        private readonly WritableMediaBuffer storage;
        private readonly int capacity, floatCount, maximumDimension;
        private int previousBlockSize;
        private bool faulted, disposed;

        public VorbisFrameDecoder(VorbisIdentification identification, ReadOnlySpan<byte> setupPacket,
            MediaBufferAllocator allocator, CancellationToken token = default)
        {
            this.identification = identification;
            setup = new(identification, allocator);
            VorbisTransformPlan? small = null, large = null;
            WritableMediaBuffer? work = null;
            bool success = false;
            try
            {
                VorbisSetupInfo.ReadForDecoding(identification, setupPacket, setup, token);
                small = VorbisTransformPlan.Create(identification.SmallBlockSize, allocator, token);
                large = identification.LargeBlockSize == identification.SmallBlockSize ? small :
                    VorbisTransformPlan.Create(identification.LargeBlockSize, allocator, token);
                maximumDimension = setup.Books.Max(book => book.Dimensions);
                capacity = checked(MaximumFrameSamples * identification.ChannelCount);
                floatCount = checked(3 * capacity + 3 * MaximumFrameSamples + maximumDimension);
                work = allocator.Allocate(checked(floatCount * sizeof(float) + capacity));
                smallPlan = small; largePlan = large; storage = work;
                success = true;
            }
            finally
            {
                if (!success)
                {
                    work?.Dispose(); large?.Dispose();
                    if (!ReferenceEquals(small, large)) { small?.Dispose(); }
                    setup.Dispose();
                }
            }
        }

        public int MaximumFrameSamples => identification.LargeBlockSize / 2;

        public int GetOutputSamples(ReadOnlySpan<byte> packet)
        {
            EnsureUsable();
            if (!packet.IsEmpty && (packet[0] & 1) != 0) { return 0; }
            VorbisBitReader reader = new(packet);
            VorbisMode mode = ReadMode(ref reader);
            int block = mode.LongBlock ? identification.LargeBlockSize : identification.SmallBlockSize;
            return previousBlockSize == 0 ? 0 : (previousBlockSize + block) / 4;
        }

        public int Decode(ReadOnlySpan<byte> packet, Span<float> output, CancellationToken token = default)
        {
            EnsureUsable(); token.ThrowIfCancellationRequested();
            bool success = false;
            try
            {
                if (!packet.IsEmpty && (packet[0] & 1) != 0) { success = true; return 0; }
                VorbisBitReader reader = new(packet);
                VorbisMode mode = ReadMode(ref reader);
                VorbisTransformPlan plan = mode.LongBlock ? largePlan : smallPlan;
                int block = plan.BlockSize, bins = block / 2;
                int samples = previousBlockSize == 0 ? 0 : (previousBlockSize + block) / 4;
                if (output.Length < samples * identification.ChannelCount) { throw new ArgumentException("The Vorbis PCM destination is too short.", nameof(output)); }
                Span<float> work = MemoryMarshal.Cast<byte, float>(storage.Span[..(floatCount * sizeof(float))]);
                Span<float> spectrum = work[..capacity], floor = work.Slice(capacity, capacity), tail = work.Slice(2 * capacity, capacity);
                Span<float> time = work.Slice(3 * capacity, 2 * MaximumFrameSamples);
                Span<float> fourier = work.Slice(3 * capacity + 2 * MaximumFrameSamples, MaximumFrameSamples);
                Span<float> vector = work.Slice(3 * capacity + 3 * MaximumFrameSamples, maximumDimension);
                Span<byte> classes = storage.Span[(floatCount * sizeof(float))..];
                VorbisSpectrum.Decode(ref reader, setup, mode, bins, spectrum, floor, vector, classes, token);
                for (int channel = 0; channel < identification.ChannelCount; channel++)
                {
                    VorbisImdct.Transform(plan, spectrum.Slice(channel * bins, bins), time, fourier, token);
                    Span<float> previous = tail.Slice(channel * MaximumFrameSamples, MaximumFrameSamples);
                    if (samples != 0) { Overlap(previous, time, previousBlockSize, plan, output, channel); }
                    time.Slice(bins, bins).CopyTo(previous);
                }
                token.ThrowIfCancellationRequested();
                previousBlockSize = block;
                success = true;
                return samples;
            }
            finally { if (!success) { Reset(); faulted = true; } }
        }

        private VorbisMode ReadMode(ref VorbisBitReader reader)
        {
            if (reader.Read(1) != 0) { throw new InvalidDataException("The Vorbis packet is not audio."); }
            int index = (int)reader.Read(VorbisBitReader.ILog((uint)setup.Modes.Count - 1));
            if (index >= setup.Modes.Count) { throw new InvalidDataException("The Vorbis packet refers to an absent mode."); }
            VorbisMode mode = setup.Modes[index];
            if (mode.LongBlock) { reader.Read(2); }
            return mode;
        }

        private void Overlap(ReadOnlySpan<float> previous, ReadOnlySpan<float> current, int previousSize,
            VorbisTransformPlan plan, Span<float> output, int channel)
        {
            int block = plan.BlockSize, overlap = Math.Min(previousSize, block) / 2, samples = (previousSize + block) / 4;
            int leading = Math.Max(0, (previousSize - block) / 4), currentOffset = Math.Max(0, (block - previousSize) / 4);
            ReadOnlySpan<float> window = previousSize < block ? smallPlan.Window : plan.Window;
            int at = 0;
            for (; at < leading; at++) { Store(output, at, channel, previous[at]); }
            for (int index = 0; index < overlap; index++, at++)
            {
                float value = previous[at] * window[overlap - 1 - index] + current[currentOffset + index] * window[index];
                Store(output, at, channel, value);
            }
            for (; at < samples; at++) { Store(output, at, channel, current[currentOffset + at]); }
        }

        private void Store(Span<float> output, int sample, int channel, float value)
        {
            if (!float.IsFinite(value)) { throw new InvalidDataException("The Vorbis packet reconstructs nonfinite PCM."); }
            output[sample * identification.ChannelCount + channel] = value;
        }

        public void Reset()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            storage.Span.Clear(); previousBlockSize = 0; faulted = false;
        }

        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) { throw new InvalidOperationException("The Vorbis decoder has failed and requires reset or disposal."); }
        }

        public void Dispose()
        {
            if (disposed) { return; }
            storage.Dispose(); largePlan.Dispose();
            if (!ReferenceEquals(smallPlan, largePlan)) { smallPlan.Dispose(); }
            setup.Dispose(); disposed = true;
        }
    }
}
