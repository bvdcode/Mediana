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
            string[] channels = configuration.GetChannelOrder()
                ?? throw new NotSupportedException("The Opus channel mapping family is not supported.");
            return new(OpusConfiguration.ClockRate, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, channels);
        }
    }
}
