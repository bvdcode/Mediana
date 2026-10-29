// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class VideoDisplayMatrixTests
    {
        [Fact]
        public void CompositionUsesRowVectorsAndPreservesOperationOrder()
        {
            VideoDisplayMatrix rotation = new(0, 1, 0, -1, 0, 0, 10, 20, 1);
            VideoDisplayMatrix scale = new(2, 0, 0, 0, 3, 0, 0, 0, 1);
            Assert.Equal(new VideoDisplayMatrix(0, 3, 0, -2, 0, 0, 20, 60, 1), rotation.Then(scale));
            Assert.Equal(new VideoDisplayMatrix(0, 2, 0, -3, 0, 0, 10, 20, 1), scale.Then(rotation));
            Assert.Equal(rotation, rotation.Then(VideoDisplayMatrix.Identity));
            Assert.Equal(rotation, VideoDisplayMatrix.Identity.Then(rotation));
        }

        [Fact]
        public void NonFiniteInputAndCompositionOverflowAreRejected()
        {
            VideoDisplayMatrix nan = VideoDisplayMatrix.Identity with { M31 = double.NaN };
            Assert.Throws<ArgumentException>(nan.Validate);
            Assert.Throws<ArgumentException>(() => VideoDisplayMatrix.Identity.Then(nan));
            VideoDisplayMatrix huge = VideoDisplayMatrix.Identity with { M11 = double.MaxValue };
            Assert.Throws<ArgumentException>(() => huge.Then(huge));
        }

        [Fact]
        public void StreamRetainsAnExplicitVideoTransformAndKeepsUnknownDistinctFromIdentity()
        {
            EncodedStreamInfo unspecified = new(0, MediaKind.Video, "test", new TimeBase(1, 1));
            Assert.Null(unspecified.DisplayMatrix);
            VideoDisplayMatrix transform = new(0, 1, 0, -1, 0, 0, 0, 0, 1);
            EncodedStreamInfo video = new(1, MediaKind.Video, "test", new TimeBase(1, 1), displayMatrix: transform);
            Assert.Equal(transform, video.DisplayMatrix);
            Assert.Throws<ArgumentException>(() => new EncodedStreamInfo(0, MediaKind.Audio, "test", new TimeBase(1, 1), displayMatrix: transform));
        }
    }
}
