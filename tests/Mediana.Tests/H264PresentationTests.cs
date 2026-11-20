// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264PresentationTests
    {
        [Theory]
        [InlineData("avc-presentation-1")]
        [InlineData("avc-presentation-2")]
        [InlineData("avc-presentation-5")]
        [InlineData("avc-presentation-hrd")]
        public Task PreservesIndependentPicturesTimingAndDisplaySequencesAfterSeeking(string name) =>
            H264PresentationTestInput.VerifyAsync(Mp4TestData.PathFor(name));

        [Theory]
        [InlineData(0, null, 0, 0, VideoPresentationUnit.Frame, 1, null)]
        [InlineData(1, VideoFieldParity.Top, 0, 0, VideoPresentationUnit.Field, 1, VideoFieldParity.Top)]
        [InlineData(2, VideoFieldParity.Bottom, 0, 0, VideoPresentationUnit.Field, 1, VideoFieldParity.Bottom)]
        [InlineData(3, null, 0, 1, VideoPresentationUnit.Field, 2, VideoFieldParity.Top)]
        [InlineData(4, null, 1, 0, VideoPresentationUnit.Field, 2, VideoFieldParity.Bottom)]
        [InlineData(5, null, 0, 1, VideoPresentationUnit.Field, 3, VideoFieldParity.Top)]
        [InlineData(6, null, 1, 0, VideoPresentationUnit.Field, 3, VideoFieldParity.Bottom)]
        [InlineData(7, null, 0, 0, VideoPresentationUnit.Frame, 2, null)]
        [InlineData(8, null, 0, 0, VideoPresentationUnit.Frame, 3, null)]
        public async Task MapsDisplaySequenceWithoutDuplicatingTheCodedPicture(int code, VideoFieldParity? field, int top, int bottom,
            VideoPresentationUnit unit, int count, VideoFieldParity? first)
        {
            H264SliceHeader header = await HeaderAsync(field);
            VideoFramePresentation presentation = H264Presentation.Create(header, new(top, bottom, header.Structure),
                new((H264DisplayStructure)code, H264SourceScanTypes.Progressive), false, out _);
            Assert.Equal(unit, presentation.Unit);
            Assert.Equal(count, presentation.DisplayCount);
            Assert.Equal(first, presentation.FirstField);
            Assert.Equal(VideoSourceScan.Progressive, presentation.SourceScan);
            VideoFramePresentation compatible = H264Presentation.Create(header, new(top, bottom, header.Structure),
                new((H264DisplayStructure)code, H264SourceScanTypes.Progressive), true, out bool ignored);
            Assert.False(ignored);
            Assert.Equal(presentation, compatible);
        }

        [Theory]
        [InlineData(null, 0, 0, VideoPresentationUnit.Frame, 1, null)]
        [InlineData(null, -2, 0, VideoPresentationUnit.Field, 2, VideoFieldParity.Top)]
        [InlineData(null, 0, -2, VideoPresentationUnit.Field, 2, VideoFieldParity.Bottom)]
        [InlineData(VideoFieldParity.Top, 0, 0, VideoPresentationUnit.Field, 1, VideoFieldParity.Top)]
        [InlineData(VideoFieldParity.Bottom, 0, 0, VideoPresentationUnit.Field, 1, VideoFieldParity.Bottom)]
        public async Task InfersOnlyTheNormativeDisplayOrderWhenTimingIsAbsent(VideoFieldParity? field, int top, int bottom,
            VideoPresentationUnit unit, int count, VideoFieldParity? first)
        {
            H264SliceHeader header = await HeaderAsync(field);
            VideoFramePresentation presentation = H264Presentation.Create(header, new(top, bottom, header.Structure), null, false, out _);
            Assert.Equal(unit, presentation.Unit);
            Assert.Equal(count, presentation.DisplayCount);
            Assert.Equal(first, presentation.FirstField);
            Assert.Equal(VideoSourceScan.Unknown, presentation.SourceScan);
        }

        [Theory]
        [InlineData(0, VideoSourceScan.Unknown)]
        [InlineData(1, VideoSourceScan.Progressive)]
        [InlineData(2, VideoSourceScan.Interlaced)]
        [InlineData(3, VideoSourceScan.Mixed)]
        [InlineData(4, VideoSourceScan.Unknown)]
        [InlineData(5, VideoSourceScan.Unknown)]
        [InlineData(6, VideoSourceScan.Unknown)]
        [InlineData(7, VideoSourceScan.Mixed)]
        public async Task KeepsSourceScanSeparateFromDisplayCadence(int flags, VideoSourceScan expected)
        {
            H264SliceHeader header = await HeaderAsync(null);
            VideoFramePresentation presentation = H264Presentation.Create(header, new(0, 1),
                new(H264DisplayStructure.TopBottom, (H264SourceScanTypes)flags), false, out _);
            Assert.Equal(VideoPresentationUnit.Field, presentation.Unit);
            Assert.Equal(expected, presentation.SourceScan);
        }

        [Theory]
        [InlineData(0, null, 0, 1, true)]
        [InlineData(1, null, 0, 0, true)]
        [InlineData(2, VideoFieldParity.Top, 0, 0, true)]
        [InlineData(3, null, 2, 1, true)]
        [InlineData(4, null, 1, 2, true)]
        [InlineData(7, null, 0, 0, false)]
        [InlineData(8, null, 0, 1, true)]
        public async Task RejectsContradictoryDisplayStructure(int code, VideoFieldParity? field, int top, int bottom, bool fixedRate)
        {
            H264SliceHeader header = await HeaderAsync(field, fixedRate);
            Assert.Throws<InvalidDataException>(() => H264Presentation.Create(header, new(top, bottom, header.Structure),
                new((H264DisplayStructure)code, H264SourceScanTypes.None), false, out _));
            H264PictureOrder order = new(top, bottom, header.Structure);
            VideoFramePresentation inferred = H264Presentation.Create(header, order, null, false, out _);
            VideoFramePresentation recovered = H264Presentation.Create(header, order,
                new((H264DisplayStructure)code, H264SourceScanTypes.Progressive), true, out bool ignored);
            Assert.True(ignored);
            Assert.Equal(inferred, recovered);
            Assert.Equal(VideoSourceScan.Unknown, recovered.SourceScan);
        }

        [Fact]
        public async Task RecoveryDoesNotAcceptUnknownDisplayStructures()
        {
            H264SliceHeader header = await HeaderAsync(null);
            Assert.Throws<ArgumentOutOfRangeException>(() => H264Presentation.Create(header, new(0, 0),
                new((H264DisplayStructure)255, H264SourceScanTypes.None), true, out _));
        }

        [Fact]
        public void CombiningFieldsPreservesKnownAndUnknownSourceScan()
        {
            VideoSourceScan[] scans = [VideoSourceScan.Unknown, VideoSourceScan.Progressive, VideoSourceScan.Interlaced, VideoSourceScan.Mixed];
            VideoSourceScan[,] expected =
            {
                { VideoSourceScan.Unknown, VideoSourceScan.Unknown, VideoSourceScan.Unknown, VideoSourceScan.Mixed },
                { VideoSourceScan.Unknown, VideoSourceScan.Progressive, VideoSourceScan.Mixed, VideoSourceScan.Mixed },
                { VideoSourceScan.Unknown, VideoSourceScan.Mixed, VideoSourceScan.Interlaced, VideoSourceScan.Mixed },
                { VideoSourceScan.Mixed, VideoSourceScan.Mixed, VideoSourceScan.Mixed, VideoSourceScan.Mixed }
            };
            for (int a = 0; a < scans.Length; a++)
            {
                for (int b = 0; b < scans.Length; b++)
                {
                    VideoFramePresentation first = VideoFramePresentation.Fields(VideoFieldParity.Top, 1, scans[a]);
                    VideoFramePresentation second = VideoFramePresentation.Fields(VideoFieldParity.Bottom, 1, scans[b]);
                    Assert.Equal(VideoFramePresentation.Fields(VideoFieldParity.Bottom, 2, expected[a, b]),
                        H264Presentation.CombineFields(first, second, VideoFieldParity.Bottom));
                }
            }
            Assert.Null(H264Presentation.CombineFields(null, VideoFramePresentation.Fields(VideoFieldParity.Top, 1), VideoFieldParity.Top));
        }

        private static async Task<H264SliceHeader> HeaderAsync(VideoFieldParity? field, bool fixedRate = true)
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-aac.mp4"), 0);
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
            H264PictureStructure structure = field switch
            {
                null => H264PictureStructure.Frame,
                VideoFieldParity.Top => H264PictureStructure.TopField,
                VideoFieldParity.Bottom => H264PictureStructure.BottomField,
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };
            return header with
            {
                Structure = structure,
                Sequence = header.Sequence with { FrameMbsOnly = false, Vui = new() { FixedFrameRate = fixedRate } }
            };
        }
    }
}
