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
            MatroskaClockTestInput.VerifyAllocation();
            Console.WriteLine("PASS Matroska structure: EBML/CRC, Segment bounds, unknown Clusters, all Block lacing modes and 96 independent encoded packets.");
            Console.WriteLine("PASS Matroska metadata: Info/Tracks, dependent defaults, streamed CRC, deferred payloads, cancellation and all five independent files.");
            Console.WriteLine("PASS Matroska packet index: bounded pages, BlockGroup fields, late timestamps, track lookup, cancellation and all 96 independent packets.");
            Console.WriteLine("PASS Matroska clock: 1645 independent exact rational vectors, nearest-even rounding, codec delay, sample offsets and allocation-free unit scale.");
        }
    }
}
