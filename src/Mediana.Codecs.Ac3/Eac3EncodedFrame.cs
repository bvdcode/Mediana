// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3
{
    internal readonly record struct Eac3EncodedFrame(int Offset, Eac3FrameMetadata Metadata)
    {
        public Ac3FrameHeader Header => Metadata.Header;
    }
}
