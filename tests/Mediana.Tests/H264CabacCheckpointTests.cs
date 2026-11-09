// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class H264CabacCheckpointTests
    {
        [Fact]
        public void ArithmeticCostAndRestoredOutputMatchTheExactInterval() => H264CabacCheckpointTestInput.Verify();
    }
}
