// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Opus
{
    internal class OpusDecoderWorkspace : IDisposable
    {
        private readonly CeltTransformPlan plan;
        private readonly CeltPulseCodebook codebook;
        private readonly OpusFrameDecoder[] decoders;
        private readonly OpusConfiguration configuration;
        private readonly WritableMediaBuffer? mappingStorage;

        public OpusDecoderWorkspace(OpusConfiguration configuration, MediaBufferAllocator allocator)
        {
            this.configuration = configuration;
            decoders = new OpusFrameDecoder[configuration.StreamCount];
            CeltTransformPlan createdPlan = CeltTransformPlan.Create(allocator);
            CeltPulseCodebook? createdCodebook = null;
            bool succeeded = false;
            try
            {
                createdCodebook = CeltPulseCodebook.Create(CeltBandTables.MaximumBandDimensions, 128, allocator);
                for (int index = 0; index < decoders.Length; index++)
                {
                    int channels = index < configuration.CoupledStreamCount ? 2 : 1;
                    decoders[index] = new(createdPlan, createdCodebook, allocator, channels, false);
                }
                if (configuration.MappingFamily != 0)
                {
                    mappingStorage = allocator.Allocate(2 * OpusPacketInfo.MaximumFrameSamples * sizeof(float));
                }
                plan = createdPlan; codebook = createdCodebook; succeeded = true;
            }
            finally
            {
                if (!succeeded)
                {
                    foreach (OpusFrameDecoder? decoder in decoders) { decoder?.Dispose(); }
                    createdCodebook?.Dispose(); createdPlan.Dispose();
                }
            }
        }

        public void Decode(ReadOnlySpan<byte> packet, int expectedSamples, Span<float> output, CancellationToken token)
        {
            Span<Range> frames = stackalloc Range[48];
            int position = 0;
            if (mappingStorage is not null) { output.Clear(); }
            for (int stream = 0; stream < decoders.Length; stream++)
            {
                token.ThrowIfCancellationRequested();
                ReadOnlySpan<byte> remaining = packet[position..];
                OpusPacketInfo info;
                int consumed;
                if (stream + 1 == decoders.Length)
                {
                    info = OpusPacketInfo.ReadFrames(remaining, frames); consumed = remaining.Length;
                }
                else { info = OpusPacketInfo.ReadSelfDelimitedFrames(remaining, frames, out consumed); }
                if (info.SampleCount != expectedSamples) { throw new InvalidDataException("The Opus coded streams have different packet durations."); }
                OpusPacketConfiguration coding = OpusPacketConfiguration.FromToc(remaining[0]);
                OpusFrameDecoder decoder = decoders[stream];
                for (int frame = 0; frame < info.FrameCount; frame++)
                {
                    int offset = frame * info.SamplesPerFrame;
                    Span<float> target;
                    if (mappingStorage is null) { target = output[(offset * decoder.Channels)..]; }
                    else { target = MemoryMarshal.Cast<byte, float>(mappingStorage.Span); }
                    decoder.Decode(remaining[frames[frame]], coding, target, token);
                    if (mappingStorage is not null) { Map(stream, target, output, offset, info.SamplesPerFrame); }
                }
                position += consumed;
            }
        }

        private void Map(int stream, ReadOnlySpan<float> input, Span<float> output, int start, int count)
        {
            OpusFrameDecoder decoder = decoders[stream];
            int codedOffset = stream + configuration.CoupledStreamCount;
            if (decoder.Channels == 2) { codedOffset = 2 * stream; }
            ReadOnlySpan<byte> mapping = configuration.ChannelMapping.Span;
            for (int channel = 0; channel < mapping.Length; channel++)
            {
                int source = mapping[channel] - codedOffset;
                if (source < 0 || source >= decoder.Channels) { continue; }
                for (int sample = 0; sample < count; sample++)
                {
                    output[(start + sample) * mapping.Length + channel] = input[sample * decoder.Channels + source];
                }
            }
        }

        public void Dispose()
        {
            foreach (OpusFrameDecoder decoder in decoders) { decoder.Dispose(); }
            mappingStorage?.Dispose(); codebook.Dispose(); plan.Dispose();
        }
    }
}
