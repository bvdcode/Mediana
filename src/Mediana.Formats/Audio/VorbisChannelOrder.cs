// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    internal static class VorbisChannelOrder
    {
        public static string[] Create(int channels) => channels switch
        {
            1 => ["front-center"],
            2 => ["front-left", "front-right"],
            3 => ["front-left", "front-center", "front-right"],
            4 => ["front-left", "front-right", "back-left", "back-right"],
            5 => ["front-left", "front-center", "front-right", "back-left", "back-right"],
            6 => ["front-left", "front-center", "front-right", "back-left", "back-right", "low-frequency"],
            7 => ["front-left", "front-center", "front-right", "side-left", "side-right", "back-center", "low-frequency"],
            8 => ["front-left", "front-center", "front-right", "side-left", "side-right", "back-left", "back-right", "low-frequency"],
            _ => CreateDiscrete(channels)
        };

        public static string[] CreateDiscrete(int channels)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(channels, 255);
            string[] result = new string[channels];
            for (int index = 0; index < channels; index++) { result[index] = $"discrete-{index}"; }
            return result;
        }
    }
}
