// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3
{
    internal readonly record struct Ac3DecoderExpectation(int SampleRate, int BitstreamId, int ChannelMode, bool HasLfe)
    {
        public void Validate(Ac3FrameHeader header)
        {
            if (header.SampleRate != SampleRate || header.BitstreamId != BitstreamId || header.ChannelMode != ChannelMode ||
                header.HasLfe != HasLfe || header.SubstreamId != 0)
            { throw new InvalidDataException("The AC-3 syncframe disagrees with its external configuration."); }
        }
    }
}
