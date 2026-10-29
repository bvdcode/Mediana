// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif
{
    /// <summary>
    /// Indicates that a file requests a feature outside the decoder's support boundary.
    /// </summary>
    /// <param name="feature">The unsupported feature identified while reading the file.</param>
    public class UnsupportedHeicFeatureException(string feature) : NotSupportedException("Unsupported HEIC feature: " + feature)
    {
        /// <summary>
        /// Gets the name of the unsupported feature.
        /// </summary>
        public string Feature { get; } = feature;
    }
}
