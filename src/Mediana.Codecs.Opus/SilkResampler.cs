// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Opus
{
    internal class SilkResampler : IDisposable
    {
        private const int OutputSamplesPerMillisecond = 48;
        private const int FilterHistory = 8;
        private const int AllpassHistoryBytes = 6 * sizeof(int);
        private const int DelayCapacity = 16;
        private const int WorkspaceOffset = AllpassHistoryBytes + DelayCapacity * sizeof(short);
        private readonly WritableMediaBuffer storage;
        private int rateKhz, inputDelay, batchSamples, incrementQ16;

        public SilkResampler(int sampleRate, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            SilkFrameMode maximum = new(SilkFrameMode.MaximumSampleRate, 2);
            _ = new SilkFrameMode(sampleRate, 4);
            storage = allocator.Allocate(WorkspaceOffset + (2 * maximum.FrameSamples + FilterHistory) * sizeof(short));
            SetSampleRate(sampleRate);
        }

        public int SampleRate { get; private set; }

        public void SetSampleRate(int sampleRate)
        {
            SilkFrameMode mode = new(sampleRate, 4);
            SampleRate = sampleRate; rateKhz = mode.RateKhz; batchSamples = rateKhz * 10;
            inputDelay = rateKhz switch { 8 => 0, 12 => 4, 16 => 7, _ => throw new ArgumentOutOfRangeException(nameof(sampleRate)) };
            incrementQ16 = (int)(((long)sampleRate * 2 * 65536 + 47999) / 48000);
            Reset();
        }
        private Span<int> AllpassHistory => MemoryMarshal.Cast<byte, int>(storage.Span[..AllpassHistoryBytes]);
        private Span<short> Delay => MemoryMarshal.Cast<byte, short>(storage.Span.Slice(AllpassHistoryBytes, DelayCapacity * sizeof(short)));
        private Span<short> Workspace => MemoryMarshal.Cast<byte, short>(storage.Span[WorkspaceOffset..]);

        public int Process(ReadOnlySpan<short> input, Span<short> output)
        {
            if (input.Length < rateKhz || input.Length % rateKhz != 0)
            {
                throw new ArgumentException("SILK resampling requires one or more complete milliseconds of input.", nameof(input));
            }
            int count = checked(input.Length / rateKhz * OutputSamplesPerMillisecond);
            ArgumentOutOfRangeException.ThrowIfLessThan(output.Length, count, nameof(output));
            if (input.Overlaps(output[..count])) { throw new ArgumentException("SILK resampling input and output must not overlap."); }
            Span<short> delay = Delay;
            int first = rateKhz - inputDelay;
            input[..first].CopyTo(delay[inputDelay..]);
            int written = Filter(delay[..rateKhz], output);
            int consumed = first, remaining = input.Length - rateKhz;
            while (remaining > 0)
            {
                int batch = Math.Min(remaining, batchSamples);
                written += Filter(input.Slice(consumed, batch), output[written..]);
                consumed += batch; remaining -= batch;
            }
            input[^inputDelay..].CopyTo(delay);
            return written;
        }

        private int Filter(ReadOnlySpan<short> input, Span<short> output)
        {
            Span<short> workspace = Workspace;
            SilkResamplingFilter.Upsample2(input, workspace[FilterHistory..], AllpassHistory);
            int written = SilkResamplingFilter.Interpolate(workspace, input.Length, incrementQ16, output);
            workspace.Slice(2 * input.Length, FilterHistory).CopyTo(workspace);
            return written;
        }

        public void Reset() => storage.Span.Clear();
        public void CopyFrom(SilkResampler source)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (source.SampleRate != SampleRate) { throw new ArgumentException("SILK resampling states have different input rates.", nameof(source)); }
            source.storage.Span.CopyTo(storage.Span);
        }
        public void Dispose() => storage.Dispose();
    }
}
