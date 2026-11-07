// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class SilkSignalMathTests
    {
        [Fact]
        public void EnergySquareRootAndFilterExpansionMatchIndependentFixedPointResults() =>
            Assert.Equal(4096, OpusSilkSignalTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));
    }
}
