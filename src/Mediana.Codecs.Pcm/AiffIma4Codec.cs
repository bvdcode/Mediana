// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Pcm
{
    /// <summary>Decodes Apple IMA4 to interleaved PCM16 and reads its AIFF-C audio parameters.</summary>
    public class AiffIma4Codec(int maxSamplesPerFrame = 65536) : IAudioDecoderFactory, IStreamMetadataReader
    {
        private readonly int sampleLimit = maxSamplesPerFrame >= AiffIma4Configuration.SamplesPerPacket
            ? maxSamplesPerFrame : throw new ArgumentOutOfRangeException(nameof(maxSamplesPerFrame));

        public string ImplementationId => "mediana.aiff.ima4";
        public string CodecId => AiffIma4Configuration.CodecId;

        public string? GetUnsupportedReason(EncodedStreamInfo stream) => ReadConfiguration(stream) is null
            ? "The stream is not AIFF-C ima4 audio." : null;

        public IMediaCodec<MediaPacket, AudioFrame> Create(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            AiffIma4Configuration configuration = ReadConfiguration(stream)
                ?? throw new NotSupportedException("The stream is not AIFF-C ima4 audio.");
            return new AiffIma4Decoder(stream.StreamIndex, configuration, allocator, sampleLimit);
        }

        public StreamMetadata? Read(EncodedStreamInfo stream)
        {
            AudioFormat? format = ReadConfiguration(stream)?.Format;
            return format is null ? null : new AudioStreamMetadata(format.SampleRate, format.Channels.Count, channelOrder: format.Channels);
        }

        private AiffIma4Configuration? ReadConfiguration(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return stream.Kind == MediaKind.Audio && stream.CodecId == CodecId && StreamFormats.AiffIma4.GetUnsupportedReason(stream.Format) is null
                ? AiffIma4Configuration.Read(stream.Configuration.Span) : null;
        }
    }
}
