// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Av1.Tests
{
    internal static class Av1ClippedPredictionTestInput
    {
        internal static void Verify(string root) =>
            Av1PictureSequenceTestInput.Verify(root, "clippedCompound", [0x81, 0, 13, 0]);
    }
}
