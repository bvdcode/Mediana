// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class VideoStreamMetadataTests
    {
        [Fact]
        public void ConfigurationsAreOwnedAndNoItemIsSelectedAsActive()
        {
            VideoStreamConfiguration first = new(1920, 1080, VideoChromaSampling.Sampling420, 10, 10, true,
                new SampleAspectRatio(4, 3), new VideoColorInfo(ColorPrimaries.Bt2020, TransferFunction.Pq));
            VideoStreamConfiguration second = new(3840, 2160, VideoChromaSampling.Sampling444, 8, 8);
            VideoStreamConfiguration[] input = [first, second];
            VideoStreamMetadata metadata = new(input);
            input[0] = second;
            Assert.Equal(Mediana.Core.MediaKind.Video, metadata.Kind);
            Assert.Same(first, metadata.Configurations[0]);
            Assert.Same(second, metadata.Configurations[1]);
            IList<VideoStreamConfiguration> snapshot = Assert.IsAssignableFrom<IList<VideoStreamConfiguration>>(metadata.Configurations);
            Assert.Throws<NotSupportedException>(() => snapshot[0] = second);
            Assert.True(first.IsProgressive);
            Assert.Null(second.IsProgressive);
            Assert.Equal(10, first.LumaBitDepth);
            Assert.Equal(10, first.ChromaBitDepth);
            Assert.Equal(TransferFunction.Pq, first.Color.Transfer);
            Assert.Equal(new SampleAspectRatio(4, 3), first.SampleAspectRatio);
        }

        [Fact]
        public void InvalidDescriptionsCannotBecomeKnownMetadata()
        {
            Assert.Throws<ArgumentNullException>(() => new VideoStreamMetadata(null!));
            Assert.Throws<ArgumentException>(() => new VideoStreamMetadata([]));
            Assert.Throws<ArgumentException>(() => new VideoStreamMetadata([null!]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VideoStreamConfiguration(0, 1, VideoChromaSampling.Monochrome, 8, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VideoStreamConfiguration(1, 0, VideoChromaSampling.Monochrome, 8, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VideoStreamConfiguration(1, 1, (VideoChromaSampling)10, 8, 8));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VideoStreamConfiguration(1, 1, VideoChromaSampling.Monochrome, 0, null));
            Assert.Throws<ArgumentException>(() => new VideoStreamConfiguration(1, 1, VideoChromaSampling.Monochrome, 8, 8));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VideoStreamConfiguration(1, 1, VideoChromaSampling.Sampling420, 8, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VideoStreamConfiguration(1, 1, VideoChromaSampling.Sampling420, 8, 0));
            Assert.Throws<ArgumentException>(() => new VideoStreamConfiguration(1, 1, VideoChromaSampling.Monochrome, 8, null,
                sampleAspectRatio: default(SampleAspectRatio)));
        }
    }
}
