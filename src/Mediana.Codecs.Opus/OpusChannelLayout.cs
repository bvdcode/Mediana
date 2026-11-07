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
            string[] channels;
            if (configuration.MappingFamily == 255)
            {
                channels = new string[configuration.ChannelCount];
                for (int index = 0; index < channels.Length; index++) { channels[index] = $"discrete-{index}"; }
            }
            else
            {
                channels = configuration.ChannelCount switch
                {
                    1 => ["front-center"],
                    2 => ["front-left", "front-right"],
                    3 => ["front-left", "front-center", "front-right"],
                    4 => ["front-left", "front-right", "back-left", "back-right"],
                    5 => ["front-left", "front-center", "front-right", "back-left", "back-right"],
                    6 => ["front-left", "front-center", "front-right", "back-left", "back-right", "low-frequency"],
                    7 => ["front-left", "front-center", "front-right", "side-left", "side-right", "back-center", "low-frequency"],
                    8 => ["front-left", "front-center", "front-right", "side-left", "side-right", "back-left", "back-right", "low-frequency"],
                    _ => throw new NotSupportedException("The Opus speaker layout is not supported.")
                };
            }
            return new(OpusConfiguration.ClockRate, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, channels);
        }
    }
}
