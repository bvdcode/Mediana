// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Video;

namespace Mediana.Codecs.Vp9
{
    internal static class Vp9Superframe
    {
        internal static int Parse(ReadOnlySpan<byte> packet, Span<Range> frames)
        {
            return Vp9SuperframeIndex.Read(packet[^Math.Min(packet.Length, Vp9SuperframeIndex.MaximumIndexBytes)..], packet.Length, frames);
        }
    }
}
