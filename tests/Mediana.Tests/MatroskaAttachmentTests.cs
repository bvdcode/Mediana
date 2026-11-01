// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class MatroskaAttachmentTests
    {
        [Fact]
        public Task DeclarationsRemainSeparateFromTracksAndPayloadOwnershipIsExplicit() => MatroskaAttachmentTestInput.VerifyOrderingAndOwnershipAsync();
        [Fact]
        public Task MissingRepeatedAndInvalidDeclarationsAreRejected() => MatroskaAttachmentTestInput.VerifyInvalidDeclarationsAsync();
        [Fact]
        public Task MetadataCountsAndReadSizesAreBoundedWhileLargeDataIsDeferred() => MatroskaAttachmentTestInput.VerifyLimitsAndDeferredDataAsync();
        [Fact]
        public Task CancelledAndFailedReadsReleaseStorageAndExcludeConcurrentOperations() => MatroskaAttachmentTestInput.VerifyCancellationAndConcurrencyAsync();
        [Fact]
        public Task AttachmentChecksumsHaveExplicitScopeAndKnownIndependentValues() => MatroskaAttachmentTestInput.VerifyChecksumScopeAsync();
    }
}
