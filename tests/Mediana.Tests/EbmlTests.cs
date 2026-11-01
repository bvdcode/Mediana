// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class EbmlTests
    {
        [Fact]
        public void VariableIntegersFollowEveryWidthAndUnknownSizeRule() => EbmlValueTestInput.VerifyVariableIntegers();

        [Fact]
        public void ElementIdentifiersFollowTheUpdatedMatroskaRanges() => EbmlValueTestInput.VerifyIdentifiers();

        [Fact]
        public void ElementValuesPreserveDefaultsSignsAndFloatingPoint() => EbmlValueTestInput.VerifyElementValues();

        [Fact]
        public void StringsUseStrictEncodingsAndNormativeTermination() => EbmlValueTestInput.VerifyStrings();

        [Fact]
        public void InvalidAndTruncatedValuesAreRejected() => EbmlValueTestInput.VerifyInvalidEncodings();
    }
}
