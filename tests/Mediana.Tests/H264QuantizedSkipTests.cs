// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    [Collection("ImageAllocation")]
    public class H264QuantizedSkipTests
    {
        [Fact]
        public void QuantizedSkipPreservesScalarDecisionsChromaResidualsAndCommittedState() => H264QuantizedSkipTestInput.Verify();
    }
}
