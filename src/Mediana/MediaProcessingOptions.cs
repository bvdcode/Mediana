// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana
{
    public record MediaProcessingOptions
    {
        /// <summary>Optional half-open interval on the input reader's presentation timeline.
        /// Original timestamps are preserved. Container-declared presentation selections also apply to encoded tracks.</summary>
        public MediaTimeRange? Selection { get; init; }
        public MediaOpenOptions Input { get; init; } = new();
    }
}
