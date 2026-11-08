// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Codecs.Ac3
{
    /// <summary>ETSI TS 102 366 E.2.7 PCM history, time scaling and immutable output.</summary>
    internal class Ac3TransientProcessing(MediaBufferAllocator allocator, AudioFormat format, bool originalBeginning) : IDisposable
    {
        internal const int HoldbackSamples = 5120;
        internal const int HistorySamples = 8192;
        internal const int InitialUnknownSamples = 4092;
        internal const int InitialPrerollPackets = 26;
        private const int MaximumUnits = 40, MaximumEvents = 18, MaximumChannels = 16;
        private readonly Ac3BufferedAccessUnit[] units = new Ac3BufferedAccessUnit[MaximumUnits];
        private readonly Ac3TransientEvent[] events = new Ac3TransientEvent[MaximumChannels * MaximumEvents];
        private readonly int[] eventHead = new int[MaximumChannels], eventCount = new int[MaximumChannels];
        private static readonly double[] FadeIn = CreateWindow(256), FadeBack = CreateWindow(128);
        private WritableMediaBuffer? taint;
        private int head, count;
        private long decodedSamples;
        private bool completed;
        public bool HasOutput => FindOutput() >= 0;

        public void BeginAccessUnit(int codedSamples, int presentationSamples, MediaTimestamp? timestamp)
        {
            ReleaseHistory();
            if (count == MaximumUnits) { throw new InvalidOperationException("The AC-3 history exceeded its proven storage bound."); }
            WritableMediaBuffer writable = allocator.Allocate(format.GetBufferSize(codedSamples));
            int index = (head + count) % MaximumUnits;
            units[index] = new()
            {
                Writable = writable,
                Start = decodedSamples,
                CodedSamples = codedSamples,
                PresentationSamples = presentationSamples,
                Timestamp = timestamp,
                Delivered = presentationSamples == 0
            };
            count++;
            if (!originalBeginning) { taint ??= allocator.Allocate(HistorySamples * sizeof(ushort)); }
        }

        public Span<float> GetFrameStorage(int samples)
        {
            ref Ac3BufferedAccessUnit unit = ref units[(head + count - 1) % MaximumUnits];
            return unit.MutableSamples.Slice(checked((int)(decodedSamples - unit.Start)) * format.Channels.Count, samples * format.Channels.Count);
        }

        public void AppendFrame(int samples, ReadOnlySpan<int> locations, ReadOnlySpan<int> lengths)
        {
            int channels = format.Channels.Count;
            if (taint is not null)
            {
                ushort mask = (ushort)((1 << channels) - 1);
                for (int sample = 0; sample < samples; sample++)
                { Taint[(int)((decodedSamples + sample) & (HistorySamples - 1))] = decodedSamples + sample < InitialUnknownSamples ? mask : (ushort)0; }
            }
            for (int channel = 0; channel < locations.Length; channel++)
            {
                if (locations[channel] < 0) { continue; }
                if (eventCount[channel] == MaximumEvents) { throw new InvalidDataException("The E-AC-3 transient queue exceeds its timing bound."); }
                int index = channel * MaximumEvents + (eventHead[channel] + eventCount[channel]) % MaximumEvents;
                events[index] = new(checked(decodedSamples + locations[channel]), lengths[channel]);
                eventCount[channel]++;
            }
            decodedSamples = checked(decodedSamples + samples);
            ProcessEvents();
        }

        private void ProcessEvents(bool final = false)
        {
            for (int channel = 0; channel < eventCount.Length; channel++)
            {
                while (eventCount[channel] > 0)
                {
                    int index = channel * MaximumEvents + eventHead[channel];
                    Ac3TransientEvent item = events[index];
                    if (!final && item.Transient > decodedSamples) { break; }
                    Apply(channel, item);
                    eventHead[channel] = (eventHead[channel] + 1) % MaximumEvents; eventCount[channel]--;
                }
            }
        }

        private void Apply(int channel, Ac3TransientEvent item)
        {
            if (item.TargetStart >= decodedSamples) { return; }
            int correctionSamples = item.Transient > decodedSamples ? checked((int)(decodedSamples - item.TargetStart)) : item.CorrectionLength;
            Span<float> synthesis = stackalloc float[1020];
            Span<byte> unknown = stackalloc byte[1020];
            int sourceUnit = head, targetUnit = head;
            ushort channelMask = (ushort)(1 << channel);
            for (int sample = 0; sample < correctionSamples; sample++)
            {
                long position = item.SourceStart + sample;
                if (position < 0)
                {
                    if (originalBeginning) { throw new InvalidDataException("E-AC-3 transient synthesis precedes the original PCM stream."); }
                    synthesis[sample] = 0; unknown[sample] = 1;
                }
                else
                {
                    ref Ac3BufferedAccessUnit unit = ref Locate(position, ref sourceUnit);
                    synthesis[sample] = unit.Samples[checked((int)(position - unit.Start)) * format.Channels.Count + channel];
                    unknown[sample] = IsUnknown(position, channelMask) ? (byte)1 : (byte)0;
                }
            }
            for (int sample = 0; sample < correctionSamples; sample++)
            {
                long position = item.TargetStart + sample;
                if (position < 0) { continue; }
                ref Ac3BufferedAccessUnit unit = ref Locate(position, ref targetUnit);
                Span<float> pcm = unit.MutableSamples;
                int index = checked((int)(position - unit.Start)) * format.Channels.Count + channel;
                double replacement = 1;
                if (sample < 256) { replacement = FadeIn[sample]; }
                else if (sample >= item.CorrectionLength - 128) { replacement = 1 - FadeBack[sample - item.CorrectionLength + 128]; }
                pcm[index] = (float)(pcm[index] * (1 - replacement) + synthesis[sample] * replacement);
                bool unresolved = replacement < 1 && IsUnknown(position, channelMask) || replacement > 0 && unknown[sample] != 0;
                SetUnknown(position, channelMask, unresolved);
            }
        }

        private ref Ac3BufferedAccessUnit Locate(long position, ref int cursor)
        {
            for (int attempt = 0; attempt < count; attempt++)
            {
                ref Ac3BufferedAccessUnit unit = ref units[cursor];
                if (position >= unit.Start && position < unit.End) { return ref unit; }
                cursor = (cursor + 1) % MaximumUnits;
            }
            throw new InvalidDataException("E-AC-3 transient synthesis exceeds the retained PCM history.");
        }

        private Span<ushort> Taint => MemoryMarshal.Cast<byte, ushort>(taint!.Span);

        private bool IsUnknown(long position, ushort channelMask) => taint is not null &&
            (Taint[(int)(position & (HistorySamples - 1))] & channelMask) != 0;

        private void SetUnknown(long position, ushort channelMask, bool value)
        {
            if (taint is null) { return; }
            ref ushort mask = ref Taint[(int)(position & (HistorySamples - 1))];
            mask = value ? (ushort)(mask | channelMask) : (ushort)(mask & ~channelMask);
        }

        private int FindOutput()
        {
            for (int item = 0; item < count; item++)
            {
                int index = (head + item) % MaximumUnits;
                if (!units[index].Delivered) { return completed || units[index].End <= decodedSamples - HoldbackSamples ? index : -1; }
            }
            return -1;
        }

        public AudioFrame Receive()
        {
            int index = FindOutput();
            if (index < 0) { throw new InvalidOperationException("AC-3 output requires more input."); }
            ref Ac3BufferedAccessUnit unit = ref units[index];
            if (taint is not null)
            {
                for (int sample = 0; sample < unit.PresentationSamples; sample++)
                {
                    if (Taint[(int)((unit.Start + sample) & (HistorySamples - 1))] != 0)
                    { throw new NotSupportedException("Exact E-AC-3 transient recovery is not established; decode from an earlier independently recoverable position."); }
                }
            }
            unit.Published = unit.Writable!.Publish(); unit.Writable.Dispose(); unit.Writable = null;
            using MediaBuffer view = unit.Published.Slice(0, format.GetBufferSize(unit.PresentationSamples));
            AudioFrame result = new(format, unit.PresentationSamples, view, unit.Timestamp);
            unit.Delivered = true;
            return result;
        }

        public void CompleteInput()
        {
            ProcessEvents(final: true);
            completed = true;
        }

        private void ReleaseHistory()
        {
            while (count > 0 && units[head].Delivered && units[head].End <= decodedSamples - HistorySamples)
            { units[head].Dispose(); head = (head + 1) % MaximumUnits; count--; }
        }

        private static double[] CreateWindow(int length)
        {
            double[] result = new double[length];
            for (int sample = 0; sample < length; sample++) { result[sample] = .5 - .5 * Math.Cos(Math.PI * sample / (length - 1)); }
            return result;
        }

        public void Dispose()
        {
            for (int index = 0; index < units.Length; index++) { units[index].Dispose(); }
            taint?.Dispose(); taint = null;
        }
    }
}
