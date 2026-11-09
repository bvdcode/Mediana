// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    public class AdtsTransportCopyTests
    {
        [Theory]
        [InlineData("multi-protected")]
        [InlineData("multi-unprotected")]
        public async Task ExistingAdtsFramesReachTransportStreamWithoutWrappingOrAnotherPayloadAllocation(string name) =>
            await AdtsTransportCopyTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Adts"), name);

        [Fact]
        public void FragmentedMp4RequiresRawAccessUnitsInsteadOfTransportFrames()
        {
            EncodedStreamInfo stream = new(0, MediaKind.Audio, "aac", StreamFormats.AacAdts, new(1, 48000),
                AacConfiguration.Create(48000, 2).ToAudioSpecificConfig());
            Assert.Throws<NotSupportedException>(() => new Mp4AudioTrackDefinition(stream, 1));
        }
    }
}
