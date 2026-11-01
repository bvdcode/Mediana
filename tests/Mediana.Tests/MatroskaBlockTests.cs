// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class MatroskaBlockTests
    {
        [Fact]
        public Task EveryRealPacketMatchesItsIndependentBytesBoundariesAndStoredTimestamp() => MatroskaBlockReferenceInput.VerifyAsync();

        [Fact]
        public Task EveryLacingModePreservesExactFrameBoundaries() => MatroskaBlockTestInput.VerifyLacingAsync();

        [Fact]
        public Task TrackNumbersSignedTimestampsAndFlagsRetainTheirOwnSemantics() => MatroskaBlockTestInput.VerifyIntegersAndFlagsAsync();

        [Fact]
        public Task InvalidTruncatedAndOversizedBlockLayoutsAreRejected() => MatroskaBlockTestInput.VerifyInvalidLayoutsAsync();

        [Fact]
        public Task HeaderLimitsAndCanceledReadsPreserveBoundedReusableStorage() => MatroskaBlockTestInput.VerifyCancellationAndLimitsAsync();
    }
}
