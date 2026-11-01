// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class CodecProfileMetadataTests
    {
        [Fact]
        public void AvcProfilesConstraintVariantsAndLevel1bPreserveRawCodes() => CodecProfileTestInput.VerifyDeclarations();

        [Fact]
        public void MultipleOpeningConfigurationsHaveIndependentProfiles() => CodecProfileTestInput.VerifyAlternatives();

        [Fact]
        public void CommonDescriptionsRetainNamedLevelsAndLeaveAbsentFactsUnknown()
        {
            CodecProfileInfo profile = new("custom profile", "1b");
            AudioStreamMetadata audio = new(48000, 2) { CodecProfile = profile };
            VideoStreamConfiguration video = new(16, 16, VideoChromaSampling.Monochrome, 8, null) { CodecProfile = profile };
            Assert.Same(profile, audio.CodecProfile);
            Assert.Same(profile, video.CodecProfile);
            Assert.Equal("custom profile", profile.Name);
            Assert.Equal("1b", profile.Level);
            Assert.Null(new CodecProfileInfo("LC").Level);
            Assert.Null(new AudioStreamMetadata(48000, 2).CodecProfile);
            Assert.Null(new VideoStreamConfiguration(16, 16, VideoChromaSampling.Monochrome, 8, null).CodecProfile);
            Assert.Throws<ArgumentNullException>(() => new CodecProfileInfo(null!));
            Assert.Throws<ArgumentException>(() => new CodecProfileInfo(" "));
            Assert.Throws<ArgumentException>(() => new CodecProfileInfo("High", " "));
        }
    }
}
