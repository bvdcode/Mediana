// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class H264SliceDeblockingTests
    {
        [Fact]
        public void SliceReferenceChangesPreserveHorizontalAndVerticalEdges() => H264SliceDeblockingTestInput.Verify();
    }
}
