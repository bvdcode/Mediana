// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;

namespace Mediana.Codecs.Aac
{
    internal class AacChannelLayout
    {
        private readonly byte[] elements;
        private readonly string[] channels;
        private readonly int[] channelMap;

        public AacChannelLayout(int configuration)
        {
            // ISO/IEC 14496-3, Table 1.11; map received channels to speaker-bit PCM order.
            (byte[] Elements, string[] Channels, int[] Map) description = configuration switch
            {
                1 => ([0], ["front-center"], [0]),
                2 => ([1], ["front-left", "front-right"], [0, 1]),
                3 => ([0, 1], ["front-left", "front-right", "front-center"], [2, 0, 1]),
                4 => ([0, 1, 0], ["front-left", "front-right", "front-center", "back-center"], [2, 0, 1, 3]),
                5 => ([0, 1, 1], ["front-left", "front-right", "front-center", "back-left", "back-right"], [2, 0, 1, 3, 4]),
                6 => ([0, 1, 1, 3], ["front-left", "front-right", "front-center", "low-frequency", "back-left", "back-right"],
                    [2, 0, 1, 4, 5, 3]),
                7 => ([0, 1, 1, 1, 3], ["front-left", "front-right", "front-center", "low-frequency",
                    "back-left", "back-right", "front-left-of-center", "front-right-of-center"], [2, 6, 7, 0, 1, 4, 5, 3]),
                _ => throw new ArgumentOutOfRangeException(nameof(configuration))
            };
            elements = description.Elements;
            channels = description.Channels;
            channelMap = description.Map;
        }

        public ReadOnlySpan<byte> Elements => elements;
        public int ChannelCount => channels.Length;
        public int OutputChannel(int codedChannel) => channelMap[codedChannel];

        public AudioFormat CreateFormat(int sampleRate) =>
            new(sampleRate, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, channels);
    }
}
