// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Core.Audio;
using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    /// <summary>Explicit source PCM declarations and AAC output settings for on-demand HLS.
    /// Presentation bounds describe the decoder's sample-exact audible timeline, including container edits.</summary>
    public class HlsAacEncoding
    {
        public HlsAacEncoding(int sourceStreamIndex, IAudioDecoderFactory decoder, AudioFormat inputFormat,
            MediaTimeRange presentation, AacEncoderOptions? options = null, int? channels = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(sourceStreamIndex);
            ArgumentNullException.ThrowIfNull(decoder);
            ArgumentNullException.ThrowIfNull(inputFormat);
            presentation.Start.TimeBase.Validate();
            presentation.End.TimeBase.Validate();
            SourceStreamIndex = sourceStreamIndex;
            Decoder = decoder;
            InputFormat = inputFormat;
            Presentation = presentation;
            Options = options ?? new();
            Channels = channels;
        }

        public int SourceStreamIndex { get; }
        public IAudioDecoderFactory Decoder { get; }
        public AudioFormat InputFormat { get; }
        public MediaTimeRange Presentation { get; }
        public AacEncoderOptions Options { get; }
        public int? Channels { get; }
    }
}
