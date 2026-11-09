// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    /// <summary>Channel names in AC-3 coded order, before any downmix or reordering.</summary>
    public static class Ac3ChannelLayout
    {
        public static string[] GetChannelOrder(int channelMode, bool hasLfe)
        {
            string[] main = channelMode switch
            {
                0 or 2 => ["front-left", "front-right"],
                1 => ["front-center"],
                3 => ["front-left", "front-center", "front-right"],
                4 => ["front-left", "front-right", "back-center"],
                5 => ["front-left", "front-center", "front-right", "back-center"],
                6 => ["front-left", "front-right", "side-left", "side-right"],
                7 => ["front-left", "front-center", "front-right", "side-left", "side-right"],
                _ => throw new ArgumentOutOfRangeException(nameof(channelMode))
            };
            return hasLfe ? [.. main, "low-frequency"] : main;
        }

        public static int GetChannelCount(int channelMode, bool hasLfe) =>
            (channelMode switch
            {
                0 or 2 => 2,
                1 => 1,
                3 or 4 => 3,
                5 or 6 => 4,
                7 => 5,
                _ => throw new ArgumentOutOfRangeException(nameof(channelMode))
            }) + (hasLfe ? 1 : 0);

        internal static int GetSampleRate(int code) => code switch
        {
            0 => 48000,
            1 => 44100,
            2 => 32000,
            _ => throw new InvalidDataException("The AC-3 sample rate code is reserved.")
        };
    }
}
