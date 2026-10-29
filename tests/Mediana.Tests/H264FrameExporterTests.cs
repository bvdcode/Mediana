// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264FrameExporterTests
    {
        [Fact]
        public void ExportCropsEveryPlaneAndRetainsIndependentStorageWithTimeAndDisplayMetadata()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            H264SequenceParameterSet sequence = Sequence() with
            {
                Vui = new H264VuiParameters
                {
                    AspectRatioWidth = 4,
                    AspectRatioHeight = 3,
                    ColorPrimaries = 1,
                    TransferCharacteristics = 1,
                    MatrixCoefficients = 1
                }
            };
            VideoFrame exported;
            MediaTimestamp timestamp = new(7, new TimeBase(1, 30));
            MediaDuration duration = new(1, new TimeBase(1, 30));
            using (H264Picture picture = new(sequence, allocator, H264SliceKind.P))
            {
                for (int plane = 0; plane < 3; plane++)
                {
                    for (int index = 0; index < picture.Plane(plane).Length; index++)
                    {
                        picture.Plane(plane)[index] = (byte)((index + plane * 50) % 256);
                    }
                }
                exported = H264FrameExporter.Export(picture, allocator, timestamp, duration, default);
                for (int plane = 0; plane < 3; plane++)
                {
                    int scale = plane == 0 ? 1 : 2;
                    for (int row = 0; row < exported.Planes[plane].Height; row++)
                    {
                        for (int x = 0; x < exported.Planes[plane].RowBytes; x++)
                        {
                            int source = (row + sequence.CropTop / scale) * picture.Stride(plane) + sequence.CropLeft / scale + x;
                            Assert.Equal((byte)((source + plane * 50) % 256), exported.GetRow(plane, row)[x]);
                        }
                    }
                    picture.Plane(plane).Clear();
                }
            }
            using (exported)
            {
                Assert.Equal(26, exported.Format.Width);
                Assert.Equal(22, exported.Format.Height);
                Assert.Equal(VideoPixelFormat.Yuv420P8, exported.Format.PixelFormat);
                Assert.Equal(new SampleAspectRatio(4, 3), exported.Format.SampleAspectRatio);
                Assert.Equal(new VideoColorInfo(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709,
                    ColorRange.Limited, ChromaLocation.Left), exported.Format.Color);
                Assert.Equal(timestamp, exported.Timestamp);
                Assert.Equal(duration, exported.Duration);
                Assert.Equal((byte)130, exported.GetRow(0, 0)[0]);
                Assert.Equal(26 * 22 * 3 / 2, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ExportCancellationAndMemoryLimitLeaveTheReferencePictureIntact()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264Picture picture = new(Sequence(), allocator, H264SliceKind.P);
            picture.Plane(0).Fill(77);
            long before = allocator.AllocatedBytes;
            Assert.Throws<MediaMemoryLimitException>(() => H264FrameExporter.Export(picture, new MediaBufferAllocator(32), null, null, default));
            Assert.Throws<OperationCanceledException>(() => H264FrameExporter.Export(picture, allocator, null, null, new CancellationToken(true)));
            Assert.Equal(before, allocator.AllocatedBytes);
            Assert.Equal(77, picture.Plane(0)[0]);
        }

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
