// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Dts
{
    internal class DtsFrameDecoder : IDisposable
    {
        private const int PredictionSamples = 5 * 32 * 4;
        private const int SynthesisSamples = 5 * 576;
        private readonly WritableMediaBuffer state;
        private readonly DtsChannelCoding[] channels = [new(), new(), new(), new(), new()];
        private int sampleRate, channelMode, lfeMode;
        private bool initialized;

        public DtsFrameDecoder(MediaBufferAllocator allocator)
        {
            state = allocator.Allocate((PredictionSamples + SynthesisSamples + 8 + 5 * 256) * sizeof(double));
            state.Span.Clear();
        }

        public void Decode(ReadOnlySpan<byte> encoded, DtsFrameHeader header, Span<float> output, CancellationToken token)
        {
            if (encoded.Length < header.FrameBytes) { throw new InvalidDataException("The DTS Core frame is truncated."); }
            if (encoded.Length != header.FrameBytes || header.HasExtensions)
            { throw new NotSupportedException("DTS-HD and core extension presentations require an extension decoder."); }
            if (initialized && (sampleRate != header.SampleRate || channelMode != header.ChannelMode || lfeMode != header.LfeMode))
            { throw new NotSupportedException("DTS stream configuration changed without resetting the decoder."); }
            sampleRate = header.SampleRate; channelMode = header.ChannelMode; lfeMode = header.LfeMode; initialized = true;
            int count = header.PrimaryChannelCount;
            int outputChannels = header.ChannelCount;
            Span<double> data = MemoryMarshal.Cast<byte, double>(state.Span);
            Span<double> prediction = data[..PredictionSamples];
            if (!header.PredictFromPreviousFrame) { prediction.Clear(); }
            Span<double> filter = data.Slice(PredictionSamples, SynthesisSamples);
            Span<double> lfeHistory = data.Slice(PredictionSamples + SynthesisSamples, 8);
            Span<double> samples = data[(PredictionSamples + SynthesisSamples + 8)..];
            DtsBitReader bits = new(encoded);
            bits.Skip(header.HeaderBits);
            int subframes = DtsCodingHeader.Read(ref bits, header, channels, count);
            int writtenBlocks = 0;
            Span<double> lfe = stackalloc double[16];
            double gain = 1;
            for (int subframe = 0; subframe < subframes; subframe++)
            {
                token.ThrowIfCancellationRequested();
                (int groups, int partial) = DtsSubframeHeader.Read(ref bits, header, channels, count);
                if (partial != 0 && (!header.IsTermination || subframe + 1 != subframes))
                { throw new InvalidDataException("A partial DTS subsubframe must end a termination frame."); }
                int blocks = groups * 8 - (partial == 0 ? 0 : 8 - partial);
                if (blocks > header.SampleBlocks - writtenBlocks) { throw new InvalidDataException("DTS subframes exceed the declared sample count."); }
                for (int ch = 0; ch < count; ch++)
                {
                    for (int band = channels[ch].VectorStart; band < channels[ch].ActiveBands; band++)
                    { channels[ch].VectorIndexes[band] = bits.Read(10); }
                }
                if (header.LfeMode != 0)
                {
                    int lfeCount = 2 * header.LfeMode * groups;
                    for (int i = 0; i < lfeCount; i++) { lfe[i] = bits.Signed(8); }
                    int scaleIndex = bits.Read(8);
                    if (scaleIndex >= 125) { throw new InvalidDataException("The DTS LFE scale index is reserved."); }
                    double scale = DtsQuantizationTables.Scale7[scaleIndex] * 0.035;
                    for (int i = 0; i < lfeCount; i++) { lfe[i] *= scale; }
                    DtsSynthesis.LowFrequency(lfe[..lfeCount], lfeHistory, 256 / (2 * header.LfeMode),
                        output.Slice(writtenBlocks * 32 * outputChannels, blocks * 32 * outputChannels), count, outputChannels, gain);
                }
                for (int group = 0; group < groups; group++)
                {
                    token.ThrowIfCancellationRequested();
                    for (int ch = 0; ch < count; ch++)
                    { DtsSubbandDecoder.Read(ref bits, header, channels[ch], group, samples.Slice(ch * 256, 256), prediction.Slice(ch * 128, 128)); }
                    Joint(samples, channels, count);
                    if (header.FrontSumDifference) { SumDifference(samples, header.ChannelMode is 5 or 7 or 9 ? 1 : 0); }
                    if (header.SurroundSumDifference)
                    {
                        if (count < 4) { throw new InvalidDataException("DTS surround sum/difference requires a surround pair."); }
                        SumDifference(samples, count - 2);
                    }
                    int groupBlocks = group + 1 == groups && partial != 0 ? partial : 8;
                    for (int sample = 0; sample < groupBlocks; sample++)
                    {
                        Span<float> destination = output[((writtenBlocks + sample) * 32 * outputChannels)..];
                        for (int ch = 0; ch < count; ch++)
                        {
                            DtsSynthesis.Apply(samples.Slice(ch * 256 + sample * 32, 32), filter.Slice(ch * 576, 576),
                                header.PerfectReconstruction, destination, ch, outputChannels, gain);
                        }
                    }
                    writtenBlocks += groupBlocks;
                    if ((header.SyncEachSubsubframe || group + 1 == groups) && bits.Read(16) != 65535)
                    { throw new InvalidDataException("The DTS subsubframe synchronization word is invalid."); }
                }
            }
            if (writtenBlocks != header.SampleBlocks) { throw new InvalidDataException("DTS subframe samples differ from the frame header."); }
            if (header.HasTimeCode) { bits.Skip(32); }
            if (header.HasAuxiliaryData)
            {
                int auxiliary = bits.Read(6);
                bits.Skip((32 - bits.Position % 32) % 32);
                bits.Skip(auxiliary * 8);
            }
            if (header.HasCrc && header.HasDynamicRange) { bits.Skip(16); }
            output[(header.CodedSamples * outputChannels)..].Clear();
        }

        private static void Joint(Span<double> samples, DtsChannelCoding[] channels, int count)
        {
            for (int ch = 0; ch < count; ch++)
            {
                DtsChannelCoding channel = channels[ch];
                if (channel.JointSource < 0) { continue; }
                for (int band = channel.ActiveBands; band < channels[channel.JointSource].ActiveBands; band++)
                {
                    for (int i = 0; i < 8; i++)
                    { samples[ch * 256 + i * 32 + band] = samples[channel.JointSource * 256 + i * 32 + band] * channel.JointScales[band]; }
                }
            }
        }

        private static void SumDifference(Span<double> samples, int first)
        {
            for (int i = 0; i < 256; i++)
            {
                double a = samples[first * 256 + i], b = samples[(first + 1) * 256 + i];
                samples[first * 256 + i] = a + b;
                samples[(first + 1) * 256 + i] = a - b;
            }
        }

        public void Reset() { state.Span.Clear(); initialized = false; }
        public void Dispose() => state.Dispose();
    }
}
