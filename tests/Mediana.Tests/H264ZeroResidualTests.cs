// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class H264ZeroResidualTests
    {
        [Fact]
        public void EmptyMacroblocksPreservePredictionModesCountsAndBitstreamAtEveryQuantizer() =>
            H264ZeroResidualTestInput.VerifyMacroblocks();
    }
}
