// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal interface IMp4SampleIndex
    {
        int SampleCount { get; }
        long PayloadBytes { get; }
        long DecodeDurationTicks { get; }
        long DecodeTimeLeadTicks { get; }
        bool RequiresPresentationTiming { get; }
        IMp4SampleCursor CreateCursor();
        Mp4SampleLocation Locate(int sample, CancellationToken cancellationToken);
        int? FindSeekSample(Int128 target, int beforeSample, CancellationToken cancellationToken);
        int FindSyncSample(int sample);
    }
}
