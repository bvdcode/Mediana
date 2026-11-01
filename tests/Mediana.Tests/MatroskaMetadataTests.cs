// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class MatroskaMetadataTests
    {
        [Fact]
        public Task InfoPreservesNativeUnitsDefaultsDatesAndUuidByteOrder() => MatroskaMetadataTestInput.VerifyInfoAsync();

        [Fact]
        public Task TrackOrderFlagsTimingAndDeferredPayloadsArePreserved() => MatroskaMetadataTestInput.VerifyTracksAsync();

        [Fact]
        public Task DependentAudioAndVideoDefaultsAreIndependentOfFieldOrder() => MatroskaMetadataTestInput.VerifyVideoAsync();

        [Fact]
        public Task InvalidOrDuplicateInfoFieldsAreRejected() => MatroskaMetadataTestInput.VerifyInvalidInfoAsync();

        [Fact]
        public Task TrackRequirementsSingletonFieldsAndCountLimitsAreEnforced() => MatroskaMetadataTestInput.VerifyInvalidTracksAsync();

        [Fact]
        public Task InvalidAudioVideoAndCropSettingsAreRejected() => MatroskaMetadataTestInput.VerifyInvalidSettingsAsync();

        [Fact]
        public Task MetadataCrcStreamsAcrossChunksAndRejectsCorruption() => MatroskaMetadataResourceInput.VerifyCrcAsync();

        [Fact]
        public Task MetadataLimitsAndConstructorFailuresReleaseTheirStorage() => MatroskaMetadataResourceInput.VerifyLimitsAsync();

        [Fact]
        public Task PendingTextAndCrcCancellationReleaseTemporaryStorage() => MatroskaMetadataResourceInput.VerifyCancellationAsync();

        [Fact]
        public Task PrivatePayloadsAreDeferredAndTruncatedReadsReleaseStorage() => MatroskaMetadataResourceInput.VerifyDeferredAndTruncatedAsync();

        [Fact]
        public Task FiveMuxedFilesPreserveAllTrackDeclarationsAndExternalCodecFacts() => MatroskaMetadataReferenceInput.VerifyAsync();
    }
}
