// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Metadata;

namespace Mediana.Codecs.Mp3
{
    /// <summary>Reads sample rate and channel count from the opening MPEG Layer III frame header.</summary>
    public class Mp3StreamMetadataReader : IStreamMetadataReader
    {
        public string ImplementationId => "mediana.mp3.metadata";
        public string CodecId => Mp3FrameHeader.CodecId;

        public StreamMetadata Read(EncodedStreamInfo stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (stream.Kind != MediaKind.Audio || stream.CodecId != CodecId)
            {
                throw new ArgumentException("The stream is not MPEG Layer III audio.", nameof(stream));
            }
            Mp3FrameHeader header = Mp3FrameHeader.Read(stream.Configuration.Span);
            return new AudioStreamMetadata(header.SampleRate, header.ChannelCount);
        }
    }
}
