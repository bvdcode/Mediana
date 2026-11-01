// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal readonly record struct MatroskaBlockExtensions(ulong? DurationTicks, long? DiscardPaddingNanoseconds,
        EbmlElement? CodecState, EbmlElement? BlockAdditions, ulong? ReferencePriority)
    {
        public bool HasValues => DurationTicks is not null || DiscardPaddingNanoseconds is not null ||
            CodecState is not null || BlockAdditions is not null || ReferencePriority is not null;
    }
}
