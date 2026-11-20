// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Processing
{
    public enum ResizeColorSpace
    {
        /// <summary>Filter in linear light. Requires sRGB input without an overriding color description.</summary>
        LinearSrgb,
        /// <summary>Filter encoded channel values, retaining their existing color interpretation.</summary>
        Encoded
    }
}
