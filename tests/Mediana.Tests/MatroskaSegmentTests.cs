// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class MatroskaSegmentTests
    {
        [Fact]
        public Task UnknownClustersEndAtSchemaSiblingsWithoutScanningBlockBytes() => MatroskaSegmentTestInput.VerifyUnknownClustersAsync();

        [Fact]
        public Task ParentEndGlobalsAndLargePayloadsPreserveBoundsAndStorage() => MatroskaSegmentTestInput.VerifyEofGlobalsAndLimitsAsync();

        [Fact]
        public Task InvalidHierarchyAndUnsupportedConcatenatedDocumentsAreRejected() => MatroskaSegmentTestInput.VerifyInvalidHierarchyAsync();

        [Fact]
        public Task CancelingClusterResolutionPreservesReaderReuseAndSourceOwnership() => MatroskaSegmentTestInput.VerifyCancellationAsync();

        [Fact]
        public Task EveryMuxedSegmentChildHasTheIndependentExpectedExtent() => MatroskaSegmentTestInput.VerifyMuxedFileAsync();
    }
}
