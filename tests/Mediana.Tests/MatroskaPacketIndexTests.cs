// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class MatroskaPacketIndexTests
    {
        [Fact]
        public Task InterleavedLacedPacketsRemainAddressableAcrossIndexPageBoundaries() => MatroskaPacketIndexTestInput.VerifyManyPacketsAsync();

        [Fact]
        public void PagedStoragePreservesTenThousandValuesAcrossGrowthBoundaries() => MatroskaIndexTableTestInput.VerifyPages();

        [Fact]
        public Task SharedIndexBudgetAndPartialLastPageHaveExactBounds() => MatroskaIndexTableTestInput.VerifyBoundsAsync();

        [Fact]
        public Task BlockGroupKeepsReferencesNativeTimingAndDeferredBinaryRanges() => MatroskaBlockGroupTestInput.VerifyValuesAsync();

        [Fact]
        public Task BlockGroupRejectsDuplicateFieldsMissingBlockAndInvalidReferences() => MatroskaBlockGroupTestInput.VerifyInvalidAsync();

        [Fact]
        public Task PacketIndexPreservesPhysicalOrderTrackOrderAndEveryLacedFrame() => MatroskaPacketIndexTestInput.VerifyTrackOrderAsync();

        [Fact]
        public Task InvalidClustersTracksCountsAndAllocationFailuresReleaseIndexStorage() => MatroskaPacketIndexTestInput.VerifyInvalidAsync();

        [Fact]
        public Task ExplicitClusterChecksumPolicyStillValidatesCrcPlacementAndSize() => MatroskaPacketIndexTestInput.VerifyChecksumsAsync();

        [Fact]
        public Task UnknownClustersLateTimestampsAndExactByteQuotaAreSupported() => MatroskaPacketIndexTestInput.VerifyUnknownClustersAndExactQuotaAsync();

        [Fact]
        public Task PendingIndexCancellationReleasesPagesAndAllowsSourceReuse() => MatroskaPacketIndexTestInput.VerifyCancellationAsync();

        [Fact]
        public Task AllNinetySixIndependentPacketsSupportPhysicalAndReverseTrackLookup() => MatroskaPacketIndexReferenceInput.VerifyAsync();
    }
}
