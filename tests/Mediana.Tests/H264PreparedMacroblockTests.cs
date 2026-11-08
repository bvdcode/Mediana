// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class H264PreparedMacroblockTests
    {
        [Fact]
        public void PreparedReconstructionMatchesActualWritersAtAllSupportedQuantizerExtremes() =>
            H264PreparedMacroblockTestInput.Verify();
    }
}
