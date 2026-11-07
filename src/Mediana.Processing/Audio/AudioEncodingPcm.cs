// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Processing.Audio;

namespace Mediana.Processing.Audio
{
    /// <summary>Prepares mono/stereo interleaved Float32 frames, retaining compatible storage or applying explicit downmix.</summary>
    public class AudioEncodingPcm
    {
        private readonly AudioChannelMixer? mixer;

        public AudioEncodingPcm(AudioFormat input, int? channels)
        {
            mixer = channels switch
            {
                null => null,
                1 => AudioDownmix.CreateMono(input, AudioSampleFormat.Float32),
                2 => AudioDownmix.CreateStereo(input, AudioSampleFormat.Float32),
                _ => throw new ArgumentOutOfRangeException(nameof(channels), "Select one or two output audio channels.")
            };
            if ((mixer?.OutputFormat ?? input).Channels.Count > 2)
            {
                throw new NotSupportedException("Encoding preparation requires mono or stereo; select one or two output channels to downmix explicitly.");
            }
            OutputFormat = mixer?.OutputFormat ?? new(input.SampleRate, AudioSampleFormat.Float32,
                AudioStorageLayout.Interleaved, input.Channels.ToArray());
        }

        public AudioFormat OutputFormat { get; }

        public AudioFrame Apply(AudioFrame input, MediaBufferAllocator allocator, CancellationToken token)
        {
            if (mixer is null)
            {
                return AudioSampleConverter.ConvertOrRetain(input, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, allocator, token);
            }
            return mixer.Apply(input, allocator, token);
        }
    }
}
