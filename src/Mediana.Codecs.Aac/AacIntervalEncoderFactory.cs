// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Codecs.Aac
{
    /// <summary>Creates a fresh encoder for an explicitly bounded PCM interval. Use the plan's input and packet ranges together.
    /// No preceding compressed packets or encoder checkpoints are retained.</summary>
    public class AacIntervalEncoderFactory : IAudioEncoderFactory
    {
        private readonly AacEncodingInterval interval;
        private readonly AacEncoderOptions? options;

        public AacIntervalEncoderFactory(AacEncodingInterval interval, AacEncoderOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(interval);
            if (!interval.HasPackets) { throw new ArgumentException("The AAC interval contains no packets.", nameof(interval)); }
            this.interval = interval;
            this.options = options;
        }

        public string ImplementationId => "mediana.aac.interval.encoder";
        public string CodecId => "aac";

        public IMediaEncoder<AudioFrame> Create(AudioFormat format, TimeBase timeBase, MediaBufferAllocator allocator, int streamIndex = 0)
        {
            ArgumentNullException.ThrowIfNull(format);
            if (format.SampleRate != interval.SampleRate)
            { throw new NotSupportedException("The AAC interval sample rate must match the prepared PCM."); }
            return AacEncoder.CreateAtFrameOffset(format, timeBase, allocator, interval.InputFrameOffset, options, streamIndex);
        }
    }
}
