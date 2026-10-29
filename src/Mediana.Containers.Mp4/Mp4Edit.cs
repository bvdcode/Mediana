// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Containers.Mp4
{
    /// <summary>Duration uses the movie time base; media time uses the track time base.
    /// A media time of -1 represents an empty edit. Rate is signed 16.16 fixed point.</summary>
    public readonly record struct Mp4Edit(MediaDuration Duration, long MediaTime, int Rate);
}
