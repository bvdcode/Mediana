// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Core.Audio
{
    /// <summary>A tightly packed PCM block retaining its own buffer lease.
    /// Codec delay, padding removal and resampling are outside this storage type.</summary>
    public class AudioFrame : IDisposable
    {
        private readonly MediaBuffer buffer;

        public AudioFrame(AudioFormat format, int samplesPerChannel, MediaBuffer buffer, MediaTimestamp? timestamp = null)
        {
            ArgumentNullException.ThrowIfNull(format);
            ArgumentNullException.ThrowIfNull(buffer);
            timestamp?.TimeBase.Validate();
            int required = format.GetBufferSize(samplesPerChannel);
            if (buffer.Memory.Length != required)
            {
                throw new ArgumentException("The buffer size does not match the audio format and sample count.", nameof(buffer));
            }
            Format = format;
            SamplesPerChannel = samplesPerChannel;
            Timestamp = timestamp;
            Duration = new MediaDuration(samplesPerChannel, new TimeBase(1, format.SampleRate));
            this.buffer = buffer.Retain();
        }

        public AudioFormat Format { get; }
        public int SamplesPerChannel { get; }
        public MediaTimestamp? Timestamp { get; }
        public MediaDuration Duration { get; }

        public ReadOnlyMemory<byte> GetPlane(int index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Format.PlaneCount);
            ReadOnlyMemory<byte> data = buffer.Memory;
            int length = data.Length / Format.PlaneCount;
            return data.Slice(index * length, length);
        }

        public AudioFrame Retain() => new(Format, SamplesPerChannel, buffer, Timestamp);
        public void Dispose() => buffer.Dispose();
    }
}
