// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class H264CabacBidirectionalTests
    {
        [Fact]
        public Task ContextsAndEveryMacroblockAndSubtypeMatchNormativeStatesAndExactIntervals() =>
            H264CabacBidirectionalTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"), Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"));
    }
}
