// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class SilkComfortNoiseTests
    {
        [Fact]
        public void SmoothedNoiseAndSequentialSynthesisMatchIndependentPcmAndState() =>
            Assert.Equal(192, OpusSilkNoiseTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));
    }
}
