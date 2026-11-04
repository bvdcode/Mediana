// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class CffOutlineTests
    {
        [Fact]
        public void ContoursCubicControlsHintsAndSubroutinesPreserveCoordinates() => CffOutlineTestInput.VerifyPaths();
        [Fact]
        public void TruncationOperandsOffsetsAndRecursionRemainBounded() => CffOutlineTestInput.VerifyRejected();
    }
}
