// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Timing;

namespace Mediana
{
    public class AudioEncodingRequest : MediaTrackRequest
    {
        public AudioEncodingRequest(int inputStreamIndex, int outputStreamIndex, IAudioEncoderFactory encoder,
            int? channels = null, MediaTimeRange? selection = null, MediaTimeRange? packetSelection = null) : base(inputStreamIndex, outputStreamIndex)
        {
            ArgumentNullException.ThrowIfNull(encoder);
            if (channels is int count) { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count); }
            if (selection is MediaTimeRange input) { input.Start.TimeBase.Validate(); input.End.TimeBase.Validate(); }
            if (packetSelection is MediaTimeRange output) { output.Start.TimeBase.Validate(); output.End.TimeBase.Validate(); }
            Selection = selection;
            PacketSelection = packetSelection;
            Encoder = encoder;
            Channels = channels;
        }

        /// <summary>Optional per-track PCM input interval, including any explicitly requested encoder warmup/lookahead.
        /// Null inherits the operation's interval. Container presentation bounds still apply.</summary>
        public MediaTimeRange? Selection { get; }
        /// <summary>Optional whole encoded-packet interval after encoding. Requires nondecreasing packet PTS.
        /// Input counters include warmup; output counters include only selected packets.</summary>
        public MediaTimeRange? PacketSelection { get; }
        public IAudioEncoderFactory Encoder { get; }
        public int? Channels { get; }
        internal override ValueTask<ProcessedMediaTrack> OpenAsync(MediaEngine engine, MediaTrackInput input, CancellationToken token) =>
            engine.OpenAudioTrackAsync(this, input, token);
    }
}
