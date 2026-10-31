// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    [Collection("ImageAllocation")]
    public class ImageOutputAllocationTests
    {
        [Fact]
        public async Task VectorAndPageDecodingAllocatesOneRgbaOutputWithinItsBudget() =>
            await ImageOutputAllocationTestInput.VerifyAsync();
    }
}
