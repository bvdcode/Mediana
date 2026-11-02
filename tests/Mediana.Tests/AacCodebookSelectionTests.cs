// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class AacCodebookSelectionTests
    {
        [Fact]
        public void EverySpectralTupleAndSignPatternPreservesExactSelectionAndWrittenBits() =>
            Assert.Equal(5936, AacCodebookSelectionTestInput.VerifyTuples());

        [Fact]
        public void LongShortSparseAndEscapedBandsPreserveCompleteSelectionAndDecodedValues() =>
            Assert.Equal(1188, AacCodebookSelectionTestInput.VerifyBands());

    }
}
