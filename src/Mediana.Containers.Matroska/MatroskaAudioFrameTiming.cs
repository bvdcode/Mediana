// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;
using Mediana.Core.Timing;

namespace Mediana.Containers.Matroska
{
    internal readonly record struct MatroskaAudioFrameTiming(int SampleRate, int Samples, int PrecedingSamples,
        int FrameBytes, ushort HeaderIdentity, bool IsFinalFrameSetInBlock)
    {
        public int PresentedSamples { get; init; } = Samples;
        public MediaTimestamp PresentationTime { get; init; }
        public bool IsDependent => (HeaderIdentity & 0x8000) == 0 && (HeaderIdentity >> 5 & 3) == 1;
        public static ushort GetIdentity(DtsFrameHeader header) => (ushort)(0x8000 | header.ChannelMode | header.LfeMode << 8);
        public static ushort GetIdentity(Ac3FrameHeader header) => (ushort)(header.BitstreamId |
            header.StreamType << 5 | header.SubstreamId << 7 | header.ChannelMode << 10 | (header.HasLfe ? 1 << 13 : 0));
    }
}
