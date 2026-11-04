// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class H264VlcTests
    {
        [Fact]
        public void CachedReadsPreserveBitsPositionsAlignmentAndEscaping() => H264BitReadingTestInput.Verify();

        [Fact]
        public void MixedCodesPreserveTailsAndCheckedBoundaries() => H264VlcTestInput.Verify();

        [Fact]
        public void WritingPreservesSymbolsBitsPositionsAndRejection() => H264VlcWritingTestInput.Verify();
    }
}
