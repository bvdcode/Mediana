// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.OpenExr
{
    public record OpenExrDecodingOptions
    {
        /// <summary>Channel layer prefix. Empty selects R/G/B/A or Y/A; a layer selects layer.R etc.</summary>
        public string Layer { get; init; } = string.Empty;
    }
}
