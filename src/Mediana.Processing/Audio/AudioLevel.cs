// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Processing.Audio
{
    public readonly record struct AudioLevel(long StartSample, int SampleCount, MediaTimestamp? Timestamp,
        MediaDuration Duration, double RootMeanSquare, double Peak);
}
