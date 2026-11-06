// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents
{
    /// <summary>Describes the origin of block boundaries, hierarchy and reading order, independently of character decoding.</summary>
    public enum DocumentTextStructure
    {
        Declared,
        Reconstructed
    }
}
