// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class H264SourceBlockTests
    {
        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 3)]
        [InlineData(17, 19)]
        [InlineData(32, 16)]
        [InlineData(35, 33)]
        public void EveryPlaneMatchesIndependentTransformWithEdgeReplicationAndPaddedRows(int width, int height)
        {
            H264SourceBlockTestInput.Verify(width, height, false);
            H264SourceBlockTestInput.Verify(width, height, true);
        }
    }
}
