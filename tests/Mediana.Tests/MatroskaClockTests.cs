// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Matroska;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats.Audio;
using static Mediana.Tests.MatroskaIndexTestData;
using static Mediana.Tests.MatroskaMetadataTestData;
using static Mediana.Tests.MatroskaReaderTestData;

namespace Mediana.Tests
{
    public class MatroskaClockTests
    {
        [Theory]
        [InlineData(1, 333000000, true)]
        [InlineData(1, 334000000, true)]
        [InlineData(1, 335000000, false)]
        [InlineData(0.5, 333500000, true)]
        [InlineData(0.5, 333000000, true)]
        [InlineData(0.5, 334000000, false)]
        public void AudioContinuityUsesTheDeclaredTrackTickPrecision(double trackScale, long nanoseconds, bool matches)
        {
            MatroskaTrackClock clock = new(1000000, new(trackScale, null, 0, 0));
            Assert.Equal(matches, clock.MatchesTimestampPrecision(new(nanoseconds, MatroskaTrackClock.TimeBase), new(1, new(1, 3))));
            Assert.Equal(matches, clock.MatchesTimestampPrecision(new(-nanoseconds, MatroskaTrackClock.TimeBase), new(-1, new(1, 3))));
        }

        [Fact]
        public async Task FixedAudioPacketsRemoveRoundingAndKeepDiscontinuitiesDuringIndexedReads()
        {
            short[] stored = [0, 21, 43, 80, 101, 123];
            int[] samples = [0, 1024, 2048, 3840, 4864, 5888];
            byte[] configuration = AacConfiguration.Create(48000, 1).ToAudioSpecificConfig();
            byte[] cluster = Cluster([Unsigned(0xE7, 0), .. stored.Select(time => Block(timestamp: time))]);
            byte[] document = Document(Field(0x1549A966, InfoFields), MatroskaPacketTestData.Data(configuration), cluster);
            MediaBufferAllocator allocator = new(131072);
            await using EbmlElementTestSource source = new(document);
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator))
            {
                for (int packet = stored.Length - 1; packet >= 0; packet--)
                {
                    MediaTimestamp expected = new(samples[packet], new(1, 48000));
                    Assert.Equal(expected, reader.GetPacketTiming(0, packet).PresentationTime);
                    using MediaPacket payload = await reader.ReadPacketAtAsync(0, packet);
                    Assert.Equal(expected, payload.PresentationTime);
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public Task SegmentDurationUsesExactFloatScaleAndNearestEvenNanoseconds() => MatroskaClockTestInput.VerifySegmentDurationsAsync();

        [Fact]
        public Task CompleteClockExpressionsMatchIndependentExactRationalVectors() => MatroskaClockTestInput.VerifyReferencesAsync();

        [Fact]
        public Task InvalidScalesAndUninitializedOffsetTimeBasesAreRejected() => MatroskaClockTestInput.VerifyInvalidAsync();

        [Fact]
        public void UnitScalePacketClockHasNoWarmedHeapAllocation() => MatroskaClockTestInput.VerifyAllocation();
    }
}
