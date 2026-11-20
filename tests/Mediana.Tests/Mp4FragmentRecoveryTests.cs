// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    public class Mp4FragmentRecoveryTests
    {
        [Fact]
        public async Task EveryFragmentMapsItsSamplesToTheInitializationRecoveryDescription() =>
            await Mp4FragmentRecoveryTestInput.VerifyAsync();

        [Theory]
        [InlineData(Mp4RecoveryKind.Roll)]
        [InlineData(Mp4RecoveryKind.PreRoll)]
        public async Task FragmentInputResolvesRecoveryAcrossFragmentBoundaries(Mp4RecoveryKind kind)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            EncodedStreamInfo stream = new(0, MediaKind.Audio, "aac", StreamFormats.Aac, new(1, 48000),
                AacConfiguration.Create(48000, 1).ToAudioSpecificConfig());
            Mp4AudioTrackDefinition track = new(stream, 9, 0) { Recovery = new(kind, -1) };
            using MemoryStream output = new();
            await Mp4EncodedFragmentWriter.WriteInitializationAsync([track], output, allocator);
            using (WritableMediaBuffer writable = allocator.Allocate(3))
            {
                writable.Span.Fill(42);
                using MediaBuffer bytes = writable.Publish();
                for (int index = 0; index < 3; index++)
                {
                    using MediaPacket packet = new(0, bytes, new(index * 1024, stream.TimeBase),
                        new(index * 1024, stream.TimeBase), new(1024, stream.TimeBase), true);
                    await Mp4EncodedFragmentWriter.WriteFragmentAsync([new(track, [packet])], (uint)index + 1, output, allocator);
                }
            }
            await using TrackingRandomAccessSource source = new(output.ToArray());
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            Mp4SampleRecoveryGroup group = Assert.Single(reader.Tracks[0].RecoveryGroups);
            Assert.Equal(3, group.SampleCount);
            for (int index = 0; index < 3; index++)
            {
                Assert.Equal((short)-1, group.GetDistance(index));
                ContainerSeekPoint start = reader.GetPrerollStart(0, index, 0);
                Assert.Equal(Math.Max(0, index - 1), start.PacketIndex);
            }
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void RecoveryRequiresAKnownKindAndNonzeroDistance()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Mp4RecoveryDescription((Mp4RecoveryKind)(-1), -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Mp4RecoveryDescription((Mp4RecoveryKind)int.MaxValue, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Mp4RecoveryDescription(Mp4RecoveryKind.Roll, 0));
        }
    }
}
