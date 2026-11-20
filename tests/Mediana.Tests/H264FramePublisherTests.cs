// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264FramePublisherTests
    {
        [Fact]
        public void PublicationSharesImmutableCroppedPixelsAndIndependentLifetimes() =>
            H264FramePublicationTestInput.VerifyPublication();

        [Fact]
        public void CancelledPublicationLeavesPixelsWritableAndBudgetUnchanged() =>
            H264FramePublicationTestInput.VerifyCancellation();

        [Fact]
        public void FailedPictureConstructionReleasesBothAllocations() =>
            H264FramePublicationTestInput.VerifyConstructionFailure();

        [Fact]
        public void MapsStandardDefinitionAndHdrColorWithoutChangingItsEncoding()
        {
            VideoFormat sd = H264VideoFormat.Create(Sequence() with
            {
                Vui = new H264VuiParameters { ColorPrimaries = 6, TransferCharacteristics = 6, MatrixCoefficients = 5, FullRange = true }
            });
            Assert.Equal(new VideoColorInfo(ColorPrimaries.Smpte170M, TransferFunction.Bt709, MatrixCoefficients.Bt601,
                ColorRange.Full, ChromaLocation.Left), sd.Color);
            VideoFormat hdr = H264VideoFormat.Create(Sequence() with
            {
                Vui = new H264VuiParameters
                {
                    ColorPrimaries = 9,
                    TransferCharacteristics = 16,
                    MatrixCoefficients = 9,
                    ChromaLocationTop = 2,
                    ChromaLocationBottom = 2
                }
            });
            Assert.Equal(new VideoColorInfo(ColorPrimaries.Bt2020, TransferFunction.Pq, MatrixCoefficients.Bt2020NonConstant,
                ColorRange.Limited, ChromaLocation.TopLeft), hdr.Color);
        }

        [Fact]
        public void FourTwoTwoUsesCositedChromaRegardlessOfFourTwoZeroVuiLocations()
        {
            H264SequenceParameterSet sequence = Sequence() with
            {
                Profile = 122,
                Level = 30,
                ChromaFormat = 2,
                Vui = new H264VuiParameters { ChromaLocationTop = 1, ChromaLocationBottom = 5 }
            };
            H264PictureParameterSet picture = new(0, 0, false, false, 26, 0, 0, true, false, false, false, 1, 1, false, 0);
            H264Configuration configuration = new(4, new Dictionary<int, H264SequenceParameterSet> { [0] = sequence },
                new Dictionary<int, H264PictureParameterSet> { [0] = picture });
            Assert.Null(H264Support.GetUnsupportedReason(configuration));
            Assert.Equal(ChromaLocation.TopLeft, H264VideoFormat.Create(sequence).Color.ChromaLocation);
            H264SequenceMetadata metadata = new(sequence);
            Assert.Equal(ChromaLocation.TopLeft, metadata.Color.ChromaLocation);
            Assert.Equal(1, metadata.TopFieldChromaLocation);
            Assert.Equal(5, metadata.BottomFieldChromaLocation);
        }

        [Fact]
        public void KeepsUnspecifiedAspectAndReservedColorCodesUnspecified()
        {
            VideoFormat absent = H264VideoFormat.Create(Sequence());
            Assert.Null(absent.SampleAspectRatio);
            Assert.Equal(ColorPrimaries.Unspecified, absent.Color.Primaries);
            Assert.Equal(ColorRange.Limited, absent.Color.Range);
            VideoFormat reserved = H264VideoFormat.Create(Sequence() with
            {
                Vui = new H264VuiParameters
                {
                    ColorPrimaries = 99,
                    TransferCharacteristics = 99,
                    MatrixCoefficients = 99,
                    AspectRatioWidth = 0,
                    AspectRatioHeight = 77
                }
            });
            Assert.Null(reserved.SampleAspectRatio);
            Assert.Equal(absent.Color, reserved.Color);
            Assert.Throws<NotSupportedException>(() => H264VideoFormat.Create(Sequence() with
            {
                Vui = new H264VuiParameters { ChromaLocationTop = 0, ChromaLocationBottom = 2 }
            }));
        }

        [Theory]
        [InlineData(VideoFieldParity.Top, VideoSourceScan.Unknown, VideoScanMode.TopFieldFirst)]
        [InlineData(VideoFieldParity.Bottom, VideoSourceScan.Unknown, VideoScanMode.BottomFieldFirst)]
        [InlineData(VideoFieldParity.Top, VideoSourceScan.Progressive, VideoScanMode.Progressive)]
        [InlineData(VideoFieldParity.Bottom, VideoSourceScan.Progressive, VideoScanMode.Progressive)]
        [InlineData(VideoFieldParity.Top, VideoSourceScan.Interlaced, VideoScanMode.TopFieldFirst)]
        [InlineData(VideoFieldParity.Bottom, VideoSourceScan.Interlaced, VideoScanMode.BottomFieldFirst)]
        [InlineData(VideoFieldParity.Top, VideoSourceScan.Mixed, VideoScanMode.TopFieldFirst)]
        [InlineData(VideoFieldParity.Bottom, VideoSourceScan.Mixed, VideoScanMode.BottomFieldFirst)]
        public void AdaptiveCodingPreservesPresentationAndExplicitProgressiveSource(
            VideoFieldParity parity, VideoSourceScan sourceScan, VideoScanMode expected)
        {
            H264SequenceParameterSet sequence = Sequence() with
            {
                FrameMbsOnly = false,
                MbAdaptiveFrameField = true,
                CropLeft = 0,
                CropTop = 0,
                Width = 32,
                Height = 64
            };
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264Picture picture = new(sequence, allocator, H264SliceKind.I);
            VideoFramePresentation presentation = VideoFramePresentation.Fields(parity, 2, sourceScan);
            long allocated = allocator.AllocatedBytes;
            using VideoFrame frame = H264FramePublisher.Publish(picture, null, null, default, presentation: presentation);
            Assert.Equal(expected, frame.Format.ScanMode);
            Assert.Equal(presentation, frame.Presentation);
            Assert.Equal(allocated, allocator.AllocatedBytes);
        }

        private static H264SequenceParameterSet Sequence() => new()
        {
            WidthInMbs = 2,
            HeightInMapUnits = 2,
            FrameMbsOnly = true,
            CropLeft = 2,
            CropTop = 4,
            Width = 26,
            Height = 22
        };
    }
}
