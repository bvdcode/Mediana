// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Opus
{
    internal static class OpusChannelLayout
    {
        public static AudioFormat Create(OpusConfiguration configuration)
        {
            string[] channels = configuration.MappingFamily switch
            {
                0 or 1 => VorbisChannelOrder.Create(configuration.ChannelCount),
                255 => VorbisChannelOrder.CreateDiscrete(configuration.ChannelCount),
                _ => throw new NotSupportedException("The Opus channel mapping family is not supported.")
            };
            return new(OpusConfiguration.ClockRate, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, channels);
        }
    }
}
