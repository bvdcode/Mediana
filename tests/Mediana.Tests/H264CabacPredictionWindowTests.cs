// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class H264CabacPredictionWindowTests
    {
        [Fact]
        public void SyntaxHistoryMatchesFullPictureStorageAcrossRowsAndSliceStarts() => H264CabacPredictionWindowTestInput.Verify();
    }
}
