// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;

namespace Mediana
{
    public class AudioEncodingRequest : MediaTrackRequest
    {
        public AudioEncodingRequest(int inputStreamIndex, int outputStreamIndex, IAudioEncoderFactory encoder,
            int? channels = null) : base(inputStreamIndex, outputStreamIndex)
        {
            ArgumentNullException.ThrowIfNull(encoder);
            if (channels is int count) { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count); }
            Encoder = encoder;
            Channels = channels;
        }

        public IAudioEncoderFactory Encoder { get; }
        public int? Channels { get; }
        internal override ValueTask<ProcessedMediaTrack> OpenAsync(MediaEngine engine, MediaTrackInput input, CancellationToken token) =>
            engine.OpenAudioTrackAsync(this, input, token);
    }
}
