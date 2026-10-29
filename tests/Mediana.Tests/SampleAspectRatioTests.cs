// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class SampleAspectRatioTests
    {
        [Fact]
        public void NormalizesPositiveTermsAndKeepsUnspecifiedSeparateFromSquare()
        {
            Assert.Equal(new SampleAspectRatio(4, 3), new SampleAspectRatio(8, 6));
            Assert.Equal(SampleAspectRatio.Square, new SampleAspectRatio(int.MaxValue, int.MaxValue));
            VideoFormat unspecified = new(16, 16, VideoPixelFormat.Yuv420P8);
            VideoFormat square = new(16, 16, VideoPixelFormat.Yuv420P8, sampleAspectRatio: SampleAspectRatio.Square);
            Assert.Null(unspecified.SampleAspectRatio);
            Assert.Equal(SampleAspectRatio.Square, square.SampleAspectRatio);
        }

        [Fact]
        public void RejectsZeroNegativeAndUninitializedRatios()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SampleAspectRatio(0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SampleAspectRatio(1, -1));
            Assert.Throws<ArgumentException>(() => new VideoFormat(16, 16, VideoPixelFormat.Yuv420P8,
                sampleAspectRatio: default(SampleAspectRatio)));
        }
    }
}
