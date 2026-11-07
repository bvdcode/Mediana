// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal readonly record struct SilkPayloadHeader(byte Activity, byte Redundancy)
    {
        public bool IsActive(int channel, int frame) => (Activity & (1 << (3 * channel + frame))) != 0;
        public bool IsRedundant(int channel, int frame) => (Redundancy & (1 << (3 * channel + frame))) != 0;

        public static SilkPayloadHeader Read(ref OpusRangeDecoder range, int channels, int frames)
        {
            if (channels is not (1 or 2)) { throw new ArgumentOutOfRangeException(nameof(channels)); }
            if (frames is < 1 or > 3) { throw new ArgumentOutOfRangeException(nameof(frames)); }
            int activity = 0, redundantChannels = 0, redundancy = 0;
            for (int channel = 0; channel < channels; channel++)
            {
                for (int frame = 0; frame < frames; frame++)
                {
                    if (range.ReadBit(1)) { activity |= 1 << (3 * channel + frame); }
                }
                if (range.ReadBit(1)) { redundantChannels |= 1 << channel; }
            }
            for (int channel = 0; channel < channels; channel++)
            {
                if ((redundantChannels & (1 << channel)) == 0) { continue; }
                int flags = frames switch
                {
                    1 => 1,
                    2 => 1 + range.ReadInverseCdf([203, 150, 0]),
                    3 => 1 + range.ReadInverseCdf([215, 195, 166, 125, 110, 82, 0]),
                    _ => throw new InvalidOperationException()
                };
                redundancy |= flags << (3 * channel);
            }
            return new((byte)activity, (byte)redundancy);
        }
    }
}
