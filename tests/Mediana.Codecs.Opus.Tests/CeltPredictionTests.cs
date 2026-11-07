// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class CeltPredictionTests
    {
        [Fact]
        public void PitchLpcResidualAndSynthesisMatchIndependentSignals() =>
            Assert.Equal(128, OpusCeltPredictionTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));
    }
}
