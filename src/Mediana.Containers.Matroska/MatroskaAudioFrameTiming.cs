// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;

namespace Mediana.Containers.Matroska
{
    internal readonly record struct MatroskaAudioFrameTiming(int SampleRate, int Samples, int PrecedingSamples,
        int FrameBytes, ushort HeaderIdentity, bool IsFinalFrameSetInBlock)
    {
        public bool IsDependent => (HeaderIdentity >> 5 & 3) == 1;
        public static ushort GetIdentity(Ac3FrameHeader header) => (ushort)(header.BitstreamId |
            header.StreamType << 5 | header.SubstreamId << 7 | header.ChannelMode << 10 | (header.HasLfe ? 1 << 13 : 0));
    }
}
