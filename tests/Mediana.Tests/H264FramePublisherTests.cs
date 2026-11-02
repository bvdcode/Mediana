// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
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
