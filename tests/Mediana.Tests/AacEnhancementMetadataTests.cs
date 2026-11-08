// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Formats;

namespace Mediana.Tests
{
    public class AacEnhancementMetadataTests
    {
        [Theory]
        [InlineData("1308", "LC", 24000, 1)]
        [InlineData("2B098800", "HE-AAC", 48000, 1)]
        [InlineData("130856E59D4800", "HE-AAC", 48000, 1)]
        [InlineData("EB098800", "HE-AAC v2", 48000, 2)]
        public void MetadataUsesOnlyDeclaredEnhancementAndOutputParameters(string hex, string profile, int sampleRate, int channels)
        {
            EncodedStreamInfo stream = new(0, MediaKind.Audio, "aac", StreamFormats.Aac,
                new TimeBase(1, 24000), Convert.FromHexString(hex));
            AudioStreamMetadata metadata = Assert.IsType<AudioStreamMetadata>(new AacStreamMetadataReader().Read(stream));
            Assert.Equal(profile, metadata.CodecProfile!.Name);
            Assert.Equal(sampleRate, metadata.SampleRate);
            Assert.Equal(channels, metadata.ChannelCount);
        }
    }
}
