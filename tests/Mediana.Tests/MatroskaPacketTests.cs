// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class MatroskaPacketTests
    {
        [Fact]
        public Task CompleteIndependentFilesDecodeEveryAudioSampleAndVideoPicture() => MatroskaPacketReferenceInput.VerifyAsync();

        [Fact]
        public Task CodecMappingsUseNativeInitializationAndIndependentConfigurationSnapshots() => MatroskaCodecTestInput.VerifyMappingsAsync();

        [Fact]
        public Task InvalidMappingsAndConfigurationLimitsAreRejected() => MatroskaCodecTestInput.VerifyInvalidAsync();

        [Fact]
        public Task ConfigurationCancellationAndQuotaReleaseTemporaryStorage() => MatroskaCodecTestInput.VerifyResourcesAsync();

        [Fact]
        public Task LacedSampleClocksAndIndependentCursorsPreservePacketOwnership() => MatroskaPacketTestInput.VerifyTimingAndOrderAsync();

        [Fact]
        public Task UnknownLaceTimesStayUnknownAndDeclaredDurationsKeepTheirUnits() => MatroskaPacketTestInput.VerifyUnknownTimesAsync();

        [Fact]
        public Task PacketSemanticsAreNeverSilentlyDropped() => MatroskaPacketTestInput.VerifyUnsupportedSemanticsAsync();

        [Fact]
        public Task CanceledAndFailedReadsCanRetryWithoutMovingTheCursor() => MatroskaPacketTestInput.VerifyCancellationAndRetryAsync();

        [Fact]
        public Task PacketsUseOnePayloadAllocationAndRespectTheExactLiveBudget() => MatroskaPacketTestInput.VerifyQuotaAndEmptyAsync();
    }
}
