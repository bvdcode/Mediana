// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    /// <summary>A requested HEVC coding tool is not supported by the decoder.</summary>
    public class UnsupportedHevcFeatureException(string feature) : NotSupportedException("Unsupported HEVC feature: " + feature)
    {
        public string Feature { get; } = feature;
    }
}
