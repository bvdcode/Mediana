// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3
{
    internal class Eac3ProgramLayout : IDisposable
    {
        private const int MaximumChannels = 16;
        private readonly MediaBufferAllocator allocator;
        private readonly bool applyDynamicRangeCompression;
        private readonly Eac3FrameMetadata[] configuration;
        private readonly Ac3FrameDecoder?[] decoders;
        private readonly int[][] maps;
        private readonly int[] locations = new int[MaximumChannels], lengths = new int[MaximumChannels];
        internal ReadOnlySpan<int> TransientLocations => locations;
        internal ReadOnlySpan<int> TransientLengths => lengths;
        public AudioFormat Format { get; }

        public Eac3ProgramLayout(ReadOnlySpan<Eac3EncodedFrame> firstGroup, MediaBufferAllocator allocator,
            bool applyDynamicRangeCompression, Ac3DecoderExpectation? expectation)
        {
            this.allocator = allocator; this.applyDynamicRangeCompression = applyDynamicRangeCompression;
            configuration = new Eac3FrameMetadata[firstGroup.Length]; decoders = new Ac3FrameDecoder?[firstGroup.Length];
            maps = new int[firstGroup.Length][];
            for (int component = 0; component < firstGroup.Length; component++) { configuration[component] = firstGroup[component].Metadata; }
            string[] channels = Eac3ChannelMap.GetProgramChannelOrder(configuration);
            int[] owners = new int[MaximumChannels];
            int dependentLocations = 0;
            for (int component = 0; component < firstGroup.Length; component++)
            {
                Eac3FrameMetadata metadata = firstGroup[component].Metadata;
                configuration[component] = metadata;
                string[] coded = metadata.GetChannelOrder();
                maps[component] = new int[coded.Length];
                for (int channel = 0; channel < coded.Length; channel++)
                {
                    int output = Array.IndexOf(channels, coded[channel]);
                    maps[component][channel] = output; owners[output] = component;
                }
                if (component != 0 && metadata.ChannelMap is ushort map)
                {
                    dependentLocations |= Eac3ChannelMap.GetAdditionalLocations(map);
                }
            }
            if (expectation is Ac3DecoderExpectation declared &&
                (declared.DependentSubstreams != firstGroup.Length - 1 || declared.DependentLocations != dependentLocations))
            { throw new InvalidDataException("The E-AC-3 frame set disagrees with its configured dependent substreams or locations."); }
            for (int component = 0; component < maps.Length; component++)
            {
                for (int channel = 0; channel < maps[component].Length; channel++)
                { if (owners[maps[component][channel]] != component) { maps[component][channel] = -1; } }
            }
            Format = new(configuration[0].Header.SampleRate, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, channels);
        }

        public void Validate(ReadOnlySpan<Eac3EncodedFrame> group)
        {
            if (group.Length != configuration.Length) { throw new InvalidDataException("The E-AC-3 dependent substream count changed within a presentation."); }
            for (int component = 0; component < group.Length; component++)
            {
                Eac3FrameMetadata current = group[component].Metadata, expected = configuration[component];
                Ac3FrameHeader header = current.Header, prior = expected.Header;
                if (header.SampleRate != prior.SampleRate || header.ChannelMode != prior.ChannelMode || header.HasLfe != prior.HasLfe ||
                    header.StreamType != prior.StreamType || header.SubstreamId != prior.SubstreamId || current.ChannelMap != expected.ChannelMap)
                { throw new NotSupportedException("E-AC-3 substream sampling format or channel ownership changes require a new decoder."); }
                if (component != 0)
                {
                    bool last = component == group.Length - 1;
                    if (current.Compression.HasValue != last || !last && current.SecondaryCompression.HasValue)
                    { throw new InvalidDataException("Only the last dependent E-AC-3 substream may carry programme compression metadata."); }
                }
            }
        }

        public void Decode(ReadOnlySpan<Eac3EncodedFrame> group, Span<float> output, CancellationToken token)
        {
            locations.AsSpan().Fill(-1);
            int last = group.Length - 1;
            DecodeComponent(last, group[last], output, default, token);
            ReadOnlySpan<double> dynamics = decoders[last]!.FrameDynamics;
            for (int component = 0; component < last; component++)
            {
                DecodeComponent(component, group[component], output, dynamics, token);
                if (component != 0 && decoders[component]!.DynamicRangeMetadataSeen)
                { throw new InvalidDataException("Programme dynamic range metadata occurs before the last dependent substream."); }
            }
        }

        private void DecodeComponent(int component, Eac3EncodedFrame frame,
            Span<float> output, ReadOnlySpan<double> gain, CancellationToken token)
        {
            Ac3FrameDecoder decoder = decoders[component] ??= new(allocator, applyDynamicRangeCompression);
            decoder.DecodeMapped(frame.Data.Span, frame.Header,
                output, Format.Channels.Count, maps[component], gain, token);
            for (int channel = 0; channel < maps[component].Length; channel++)
            {
                int destination = maps[component][channel];
                if (destination < 0 || channel == frame.Header.ChannelCount - 1 && frame.Header.HasLfe) { continue; }
                locations[destination] = decoder.TransientLocations[channel]; lengths[destination] = decoder.TransientLengths[channel];
            }
        }

        public void Dispose()
        {
            foreach (Ac3FrameDecoder? decoder in decoders) { decoder?.Dispose(); }
        }
    }
}
