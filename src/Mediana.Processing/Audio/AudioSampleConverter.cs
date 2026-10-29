// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Processing.Audio
{
    public static class AudioSampleConverter
    {
        /// <summary>Converts native-endian PCM storage without changing rate, channels or timing.
        /// Borrows input and returns an owned frame. Integer output saturates and rounds to nearest even
        /// without dithering. Float output preserves finite headroom; non-finite samples are rejected.</summary>
        public static AudioFrame Apply(AudioFrame input, AudioSampleFormat sampleFormat, AudioStorageLayout layout,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            AudioFormat format = new(input.Format.SampleRate, sampleFormat, layout, input.Format.Channels.ToArray());
            using WritableMediaBuffer writable = allocator.Allocate(format.GetBufferSize(input.SamplesPerChannel));
            for (int sample = 0; sample < input.SamplesPerChannel; sample++)
            {
                if ((sample & 255) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                for (int channel = 0; channel < format.Channels.Count; channel++)
                {
                    int index = format.Layout switch
                    {
                        AudioStorageLayout.Interleaved => sample * format.Channels.Count + channel,
                        AudioStorageLayout.Planar => channel * input.SamplesPerChannel + sample,
                        _ => throw new ArgumentOutOfRangeException(nameof(layout))
                    };
                    PcmSampleAccess.Write(writable.Memory.Span[(index * format.BytesPerSample)..], sampleFormat,
                        PcmSampleAccess.Read(input, sample, channel));
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            return new AudioFrame(format, input.SamplesPerChannel, buffer, input.Timestamp);
        }
    }
}
