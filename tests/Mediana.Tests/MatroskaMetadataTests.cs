// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Matroska;
using Mediana.Core.Metadata;
using Mediana.IO;
using static Mediana.Tests.MatroskaMetadataTestData;

namespace Mediana.Tests
{
    public class MatroskaMetadataTests
    {
        [Fact]
        public async Task ProbeExposesPcmTrackAudioSettingsAsCommonMetadata()
        {
            byte[] little = PcmTrack(1, "A_PCM/INT/LIT"u8, 8000, 1, 16);
            byte[] big = PcmTrack(2, "A_PCM/INT/BIG"u8, 44100, 2, 24);
            byte[] ieee = PcmTrack(3, "A_PCM/FLOAT/IEEE"u8, 48000, 2, 32);
            byte[] bytes = MatroskaReaderTestData.Document(Field(0x1549A966, InfoFields), Field(0x1654AE6B, [.. little, .. big, .. ieee]));
            await using RandomAccessStreamByteSource source = new(new MemoryStream(bytes));
            MediaInfo probe = await new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory()).Build().ProbeAsync(source);
            (int Rate, int Channels, int Bits)[] expected = [(8000, 1, 16), (44100, 2, 24), (48000, 2, 32)];
            for (int stream = 0; stream < expected.Length; stream++)
            {
                AudioStreamMetadata metadata = Assert.IsType<AudioStreamMetadata>(probe.StreamMetadata[stream]);
                Assert.Equal(expected[stream].Rate, metadata.SampleRate);
                Assert.Equal(expected[stream].Channels, metadata.ChannelCount);
                Assert.Equal(expected[stream].Bits, metadata.BitsPerSample);
            }
        }

        private static byte[] PcmTrack(ulong number, ReadOnlySpan<byte> codec, double rate, ulong channels, ulong precision) =>
            Field(0xAE, [.. Unsigned(0xD7, number), .. Unsigned(0x73C5, number), .. Unsigned(0x83, 2), .. Field(0x86, codec),
                .. Field(0xE1, [.. Float(0xB5, rate), .. Unsigned(0x9F, channels), .. Unsigned(0x6264, precision)])]);

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
