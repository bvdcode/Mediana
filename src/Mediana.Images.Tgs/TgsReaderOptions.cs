// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System.Collections.Generic;
using System.Text.Json;

namespace Mediana.Images.Tgs
{
    /// <summary>Bounds for TGS input, decompression and selected-frame geometry.</summary>
    public record TgsReaderOptions
    {
        /// <summary>Gets whether to omit Puppet deformation effects and report a warning. Defaults to false.</summary>
        public bool IgnorePuppetEffects { get; init; }
        /// <summary>Gets whether to use stored property values and keyframes without evaluating expressions,
        /// reporting a warning. Defaults to false; properties without stored values remain invalid.</summary>
        public bool IgnoreExpressions { get; init; }
        /// <summary>Gets the maximum encoded JSON/TGS input size, default 16 MiB.</summary>
        public int MaximumInputBytes { get; init; } = 16 * 1024 * 1024;
        /// <summary>Gets the maximum uncompressed JSON size, default 32 MiB.</summary>
        public int MaximumJsonBytes { get; init; } = 32 * 1024 * 1024;
        /// <summary>Gets the maximum JSON value count and expanded scene items.</summary>
        public int MaximumItems { get; init; } = 500_000;
        /// <summary>Gets the maximum JSON/precomposition nesting depth, from 1 to 64.</summary>
        public int MaximumDepth { get; init; } = 64;
        /// <summary>Gets the maximum generated path commands per selected frame.</summary>
        public int MaximumPathCommands { get; init; } = 1_000_000;
    }
}
