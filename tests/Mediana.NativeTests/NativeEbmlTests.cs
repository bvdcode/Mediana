// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeEbmlTests
    {
        public static async Task RunAsync()
        {
            await EbmlElementTestInput.VerifyBoundsAsync();
            await EbmlElementTestInput.VerifyTruncationAsync();
            await EbmlElementTestInput.VerifyFailureAndCancellationAsync();
            EbmlValueTestInput.VerifyVariableIntegers();
            EbmlValueTestInput.VerifyIdentifiers();
            EbmlValueTestInput.VerifyElementValues();
            EbmlValueTestInput.VerifyStrings();
            EbmlValueTestInput.VerifyInvalidEncodings();
            await MatroskaHeaderTestInput.VerifyDefaultsAndVersionsAsync();
            await MatroskaHeaderTestInput.VerifyInvalidFieldsAsync();
            await MatroskaHeaderTestInput.VerifyCrcAsync();
            await MatroskaHeaderTestInput.VerifyBoundsAndTruncationAsync();
            await MatroskaHeaderTestInput.VerifyCancellationAndQuotaAsync();
            await MatroskaHeaderTestInput.VerifyMuxedFileAsync();
            await MatroskaSegmentTestInput.VerifyUnknownClustersAsync();
            await MatroskaSegmentTestInput.VerifyEofGlobalsAndLimitsAsync();
            await MatroskaSegmentTestInput.VerifyInvalidHierarchyAsync();
            await MatroskaSegmentTestInput.VerifyCancellationAsync();
            await MatroskaSegmentTestInput.VerifyMuxedFileAsync();
            await MatroskaBlockTestInput.VerifyLacingAsync();
            await MatroskaBlockTestInput.VerifyIntegersAndFlagsAsync();
            await MatroskaBlockTestInput.VerifyInvalidLayoutsAsync();
            await MatroskaBlockTestInput.VerifyCancellationAndLimitsAsync();
            await MatroskaBlockReferenceInput.VerifyAsync();
            await MatroskaMetadataTestInput.VerifyInfoAsync();
            await MatroskaMetadataTestInput.VerifyTracksAsync();
            await MatroskaMetadataTestInput.VerifyVideoAsync();
            await MatroskaColourTestInput.VerifyNativeFieldsAsync();
            await MatroskaColourTestInput.VerifyColorResolutionAsync();
            MatroskaColourTestInput.VerifyPresentationMapping();
            await MatroskaMetadataTestInput.VerifyInvalidInfoAsync();
            await MatroskaMetadataTestInput.VerifyInvalidTracksAsync();
            await MatroskaMetadataTestInput.VerifyInvalidSettingsAsync();
            await MatroskaMetadataResourceInput.VerifyCrcAsync();
            await MatroskaMetadataResourceInput.VerifyLimitsAsync();
            await MatroskaMetadataResourceInput.VerifyCancellationAsync();
            await MatroskaMetadataResourceInput.VerifyDeferredAndTruncatedAsync();
            await MatroskaMetadataReferenceInput.VerifyAsync();
            MatroskaIndexTableTestInput.VerifyPages();
            await MatroskaIndexTableTestInput.VerifyBoundsAsync();
            await MatroskaBlockGroupTestInput.VerifyValuesAsync();
            await MatroskaBlockGroupTestInput.VerifyInvalidAsync();
            await MatroskaPacketIndexTestInput.VerifyTrackOrderAsync();
            await MatroskaPacketIndexTestInput.VerifyManyPacketsAsync();
            await MatroskaPacketIndexTestInput.VerifyInvalidAsync();
            await MatroskaPacketIndexTestInput.VerifyChecksumsAsync();
            await MatroskaPacketIndexTestInput.VerifyUnknownClustersAndExactQuotaAsync();
            await MatroskaPacketIndexTestInput.VerifyCancellationAsync();
            await MatroskaPacketIndexReferenceInput.VerifyAsync();
            await MatroskaClockTestInput.VerifyReferencesAsync();
            await MatroskaClockTestInput.VerifyInvalidAsync();
            await MatroskaClockTestInput.VerifySegmentDurationsAsync();
            MatroskaClockTestInput.VerifyAllocation();
            await MatroskaCodecTestInput.VerifyMappingsAsync();
            await MatroskaCodecTestInput.VerifyInvalidAsync();
            await MatroskaCodecTestInput.VerifyResourcesAsync();
            await MatroskaPacketTestInput.VerifyTimingAndOrderAsync();
            await MatroskaPacketTestInput.VerifyUnknownTimesAsync();
            await MatroskaPacketTestInput.VerifyUnsupportedSemanticsAsync();
            await MatroskaPacketTestInput.VerifyCancellationAndRetryAsync();
            await MatroskaPacketTestInput.VerifyQuotaAndEmptyAsync();
            await MatroskaPacketReferenceInput.VerifyAsync();
            await MatroskaReaderTestInput.VerifyMetadataAndOwnershipAsync();
            await MatroskaReaderTestInput.VerifyRecurringAndMissingMetadataAsync();
            await MatroskaReaderTestInput.VerifyLimitsAndTraversalAsync();
            await MatroskaReaderTestInput.VerifyCancellationAndConcurrentReadAsync();
            await MatroskaReaderResourceInput.VerifyChecksumsAndAggregateMetadataAsync();
            await MatroskaReaderResourceInput.VerifyFactoryAndSourceRequirementsAsync();
            await MatroskaAttachmentTestInput.VerifyOrderingAndOwnershipAsync();
            await MatroskaAttachmentTestInput.VerifyInvalidDeclarationsAsync();
            await MatroskaAttachmentTestInput.VerifyLimitsAndDeferredDataAsync();
            await MatroskaAttachmentTestInput.VerifyCancellationAndConcurrencyAsync();
            await MatroskaAttachmentTestInput.VerifyChecksumScopeAsync();
            Console.WriteLine("PASS Matroska public reader: signature probe, native metadata, bounded traversal, cancellation, complete audio/video sessions and indexed seeks.");
            Console.WriteLine("PASS Matroska packet reads: bounded AVC/AAC/MP3 configuration, owned payloads, lace clocks, retry/quotas and 178 real encoded packets.");
            Console.WriteLine("PASS Matroska structure: EBML/CRC, Segment bounds, unknown Clusters, all Block lacing modes and 96 independent encoded packets.");
            Console.WriteLine("PASS Matroska metadata: Info/Tracks, dependent defaults, streamed CRC, deferred payloads, cancellation and all five independent files.");
            Console.WriteLine("PASS Matroska packet index: bounded pages, BlockGroup fields, late timestamps, track lookup, cancellation and all 96 independent packets.");
            Console.WriteLine("PASS Matroska clock: 1645 independent exact rational vectors, nearest-even rounding, codec delay, sample offsets and allocation-free unit scale.");
        }
    }
}
