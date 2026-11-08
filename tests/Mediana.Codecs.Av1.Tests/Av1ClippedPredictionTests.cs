// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1ClippedPredictionTests
    {
        [Fact]
        public void CompoundPredictionAtClippedFrameEdgeMatchesIndependentDecode()
        {
            Av1ClippedPredictionTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data"));
        }
    }
}
