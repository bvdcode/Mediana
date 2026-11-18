// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Cli;

namespace Mediana.Tests
{
    public class ConversionSelectionTests
    {
        private static ConversionSelection Defaults => new(new(VideoCodecMode.Copy, false, null, null, null, null, null),
            new(null, null, null), new(new(), false, 0, 1));

        [Fact]
        public void OutputSelectionProducesTypedOperations()
        {
            Assert.IsType<WaveConversionRequest>(Defaults.Create("out.WAVE"));
            Assert.IsType<CopyConversionRequest>(Defaults.Create("out.ts"));
            Assert.IsType<ImageConversionRequest>(Defaults.Create("out.png"));
            Assert.IsType<AnimationConversionRequest>((Defaults with { Image = Defaults.Image with { Animate = true } }).Create("out.webp"));
            Assert.IsType<VideoConversionRequest>((Defaults with { Video = Defaults.Video with { Codec = VideoCodecMode.H264 } }).Create("out.ts"));
            Assert.IsType<AudioConversionRequest>((Defaults with { Audio = Defaults.Audio with { Codec = AudioCodecMode.Aac } }).Create("out.ts"));
        }

        [Fact]
        public void ConflictingOperationsAndUnusedImageSelectionsAreRejected()
        {
            ConversionSelection video = Defaults with { Video = Defaults.Video with { Codec = VideoCodecMode.H264 } };
            Assert.Throws<ArgumentException>(() => (video with { Image = Defaults.Image with { Animate = true } }).Create("out.ts"));
            Assert.Throws<NotSupportedException>(() => (video with { Audio = Defaults.Audio with { Codec = AudioCodecMode.Aac } }).Create("out.ts"));
            Assert.Throws<NotSupportedException>(() => (Defaults with { Image = Defaults.Image with { Page = 2 } }).Create("out.wav"));
            Assert.Throws<ArgumentOutOfRangeException>(() => (Defaults with { Image = Defaults.Image with { TimeMs = -1 } }).Create("out.png"));
        }
    }
}
