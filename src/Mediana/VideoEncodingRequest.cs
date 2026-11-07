// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Timing;
using Mediana.Processing;

namespace Mediana
{
    public class VideoEncodingRequest : MediaTrackRequest
    {
        public VideoEncodingRequest(int inputStreamIndex, int outputStreamIndex, IVideoEncoderFactory encoder,
            VideoPreparationOptions? preparation = null, TimeBase? outputTimeBase = null) : base(inputStreamIndex, outputStreamIndex)
        {
            ArgumentNullException.ThrowIfNull(encoder);
            Encoder = encoder;
            Preparation = preparation ?? new();
            outputTimeBase?.Validate();
            OutputTimeBase = outputTimeBase;
        }

        public IVideoEncoderFactory Encoder { get; }
        public VideoPreparationOptions Preparation { get; }
        /// <summary>An explicit exact output clock. Null derives it from indexed times and selected bounds.
        /// No frame timestamps are rounded to fit this clock.</summary>
        public TimeBase? OutputTimeBase { get; }
        internal override ValueTask<ProcessedMediaTrack> OpenAsync(MediaEngine engine, MediaTrackInput input, CancellationToken token) =>
            engine.OpenVideoTrackAsync(this, input, token);
    }
}
