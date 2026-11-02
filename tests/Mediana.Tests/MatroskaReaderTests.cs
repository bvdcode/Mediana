// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class MatroskaReaderTests
    {
        [Fact]
        public Task ChecksumsHaveExplicitScopeAndMetadataLimitsAccumulate() => MatroskaReaderResourceInput.VerifyChecksumsAndAggregateMetadataAsync();
        [Fact]
        public Task FactoryUsesSignaturesAndRequiresKnownLengthRandomAccess() => MatroskaReaderResourceInput.VerifyFactoryAndSourceRequirementsAsync();
        [Fact]
        public Task NativeMetadataCommonFactsAndPacketsSurviveDisposal() => MatroskaReaderTestInput.VerifyMetadataAndOwnershipAsync();
        [Fact]
        public Task IdenticalRecurringHeadersAreAcceptedAndUnavailableFactsStayUnknown() => MatroskaReaderTestInput.VerifyRecurringAndMissingMetadataAsync();
        [Fact]
        public Task PublicLimitsAlsoBoundUnknownClusterTraversal() => MatroskaReaderTestInput.VerifyLimitsAndTraversalAsync();
        [Fact]
        public Task CancellationReleasesStorageAndConcurrentOperationsCannotMoveCursors() => MatroskaReaderTestInput.VerifyCancellationAndConcurrentReadAsync();
    }
}
