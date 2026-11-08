// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Processing.Audio;

namespace Mediana.Streaming.Hls
{
    internal class HlsEncodedAudioTrack : IHlsAudioTrack
    {
        private readonly HlsAacEncoding settings;

        public HlsEncodedAudioTrack(IIndexedContainerReader reader, HlsAacEncoding settings, MediaBufferAllocator allocator, int outputStreamIndex, uint trackId)
        {
            this.settings = settings;
            EncodedStreamInfo input = reader.Streams.SingleOrDefault(stream => stream.StreamIndex == settings.SourceStreamIndex)
                ?? throw new ArgumentException("The selected audio stream does not exist.", nameof(settings));
            if (input.Kind != MediaKind.Audio) { throw new ArgumentException("The selected stream is not audio.", nameof(settings)); }
            if (settings.Decoder.GetUnsupportedReason(input) is string reason) { throw new NotSupportedException(reason); }
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = settings.Decoder.Create(input, allocator))
            {
                if (settings.Decoder is not IRandomAccessDecoderFactory &&
                    !(reader is IIndexedPrerollReader && decoder is IPacketPrerollDecoder))
                { throw new NotSupportedException("On-demand audio encoding requires indexed decoder restart or bounded preroll."); }
            }
            AacEncodingInterval plan = AacEncodingInterval.Create(settings.Presentation, settings.Presentation, settings.InputFormat.SampleRate);
            if (!plan.HasPackets) { throw new ArgumentException("The audio presentation must contain samples.", nameof(settings)); }
            AudioFormat format = new AudioEncodingPcm(settings.InputFormat, settings.Channels).OutputFormat;
            TimeBase clock = TimeBase.Common(settings.Presentation.Start.ReduceClock().TimeBase, new(1, format.SampleRate));
            using AacEncoder encoder = AacEncoder.Create(format, clock, allocator, settings.Options, outputStreamIndex);
            long first = checked(settings.Presentation.Start.Rescale(clock, TimestampRounding.TowardZero).Value -
                encoder.PrimingSamples * (clock.Denominator / format.SampleRate));
            Definition = new(encoder.Stream, trackId, first < 0 ? checked(-first) : 0, settings.Presentation.End)
            { Recovery = new(Mp4RecoveryKind.Roll, -1) };
        }

        public Mp4AudioTrackDefinition Definition { get; }

        public MediaTrackRequest? CreateRequest(HlsVideoWindow window)
        {
            AacEncodingInterval plan = AacEncodingInterval.Create(new(window.Start, window.End), settings.Presentation, settings.InputFormat.SampleRate);
            if (!plan.HasPackets) { return null; }
            return new AudioEncodingRequest(settings.SourceStreamIndex, Definition.Stream.StreamIndex,
                new AacIntervalEncoderFactory(plan, settings.Options), settings.Channels, plan.Input, plan.Output);
        }
    }
}
