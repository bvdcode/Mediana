// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Tests
{
    public class VideoAspectRatioDescriptionTests
    {
        [Theory]
        [InlineData(false, false, 1, 1)]
        [InlineData(true, false, 1, 1)]
        [InlineData(false, true, 1, 1)]
        [InlineData(true, true, 4, 3)]
        public void FormatDefaultsDoNotOverrideCodecDeclarations(bool isDefault, bool codecDeclared, int width, int height)
        {
            VideoAspectRatioDescription description = new(SampleAspectRatio.Square, isDefault);
            H264SequenceParameterSet sequence = new()
            {
                Width = 96,
                Height = 64,
                Vui = codecDeclared ? new H264VuiParameters { AspectRatioWidth = 4, AspectRatioHeight = 3 } : null
            };
            VideoFormat format = H264VideoFormat.Create(sequence, aspectRatioDescription: description);
            Assert.Equal(new SampleAspectRatio(width, height), format.SampleAspectRatio);
            Assert.Equal(codecDeclared ? 4 : 0, sequence.Vui?.AspectRatioWidth ?? 0);
        }

        [Fact]
        public void MissingDescriptionsPreserveUnknownGeometry()
        {
            Assert.Null(H264VideoFormat.Create(new H264SequenceParameterSet { Width = 96, Height = 64 }).SampleAspectRatio);
        }

        [Fact]
        public void DescriptionsRequireValidGeometryAndVideoStreams()
        {
            Assert.Throws<ArgumentException>(() => new VideoAspectRatioDescription(default));
            VideoAspectRatioDescription description = new(new SampleAspectRatio(8, 6));
            Assert.Equal(new SampleAspectRatio(4, 3), description.Value);
            Assert.Throws<ArgumentException>(() => description.Resolve(default(SampleAspectRatio)));
            EncodedStreamInfo stream = new(0, MediaKind.Video, "h264", StreamFormats.Avc, new TimeBase(1, 1),
                aspectRatioDescription: description);
            Assert.Same(description, stream.AspectRatioDescription);
            Assert.Throws<ArgumentException>(() => new EncodedStreamInfo(0, MediaKind.Audio, "aac", StreamFormats.Aac,
                new TimeBase(1, 48000), aspectRatioDescription: description));
        }
    }
}
