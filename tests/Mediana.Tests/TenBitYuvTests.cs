// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class TenBitYuvTests
    {
        [Theory]
        [InlineData(ColorRange.Full, 0, 1023, 3)]
        [InlineData(ColorRange.Limited, 64, 940, 67)]
        public void TenBitSrgbConversionPreservesSubEightBitLumaPrecision(ColorRange range, int black, int white, int nearBlack)
        {
            MediaBufferAllocator allocator = new(1024);
            using (WritableMediaBuffer writable = allocator.Allocate(24))
            {
                Span<ushort> samples = MemoryMarshal.Cast<byte, ushort>(writable.Memory.Span);
                for (int y = 0; y < 2; y++)
                {
                    samples[y * 4] = (ushort)black; samples[y * 4 + 1] = (ushort)white;
                    samples[y * 4 + 2] = (ushort)nearBlack; samples[y * 4 + 3] = 512;
                }
                samples[8..].Fill(512);
                VideoColorInfo color = new(ColorPrimaries.Bt709, TransferFunction.Srgb, MatrixCoefficients.Bt709, range, ChromaLocation.Left);
                VideoFormat format = new(4, 2, VideoPixelFormat.Yuv420P10, color, SampleAspectRatio.Square);
                using MediaBuffer buffer = writable.Publish();
                using VideoFrame frame = new(format, buffer, format.CreateTightLayout());
                using VideoFrame converted = SrgbConverter.Apply(frame, allocator);
                for (int y = 0; y < 2; y++)
                {
                    Assert.Equal(new byte[] { 0, 0, 0, 255 }, converted.GetRow(0, y)[..4].ToArray());
                    Assert.Equal(new byte[] { 255, 255, 255, 255 }, converted.GetRow(0, y).Slice(4, 4).ToArray());
                    Assert.Equal(new byte[] { 1, 1, 1, 255 }, converted.GetRow(0, y).Slice(8, 4).ToArray());
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(ChromaLocation.Left)]
        [InlineData(ChromaLocation.Center)]
        [InlineData(ChromaLocation.TopLeft)]
        [InlineData(ChromaLocation.Top)]
        [InlineData(ChromaLocation.BottomLeft)]
        [InlineData(ChromaLocation.Bottom)]
        public void ChromaInterpolationUsesTheSameGeometryAtBothPrecisions(ChromaLocation location)
        {
            MediaBufferAllocator allocator = new(1024);
            using (VideoFrame eight = Create(VideoPixelFormat.Yuv420P8, allocator))
            using (VideoFrame ten = Create(VideoPixelFormat.Yuv420P10, allocator))
            {
                for (int y = 0; y < 4; y++)
                {
                    BilinearChromaRow<byte> row8 = new(eight, 1, y, location);
                    BilinearChromaRow<ushort> row10 = new(ten, 1, y, location);
                    for (int x = 0; x < 4; x++) { Assert.Equal(row8.Sample(x) * 4, row10.Sample(x)); }
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static VideoFrame Create(VideoPixelFormat pixelFormat, MediaBufferAllocator allocator)
        {
            VideoFormat format = new(4, 4, pixelFormat);
            PlaneLayout[] planes = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)planes[^1].EndOffset));
            byte[] chroma = [0, 100, 200, 240];
            if (pixelFormat == VideoPixelFormat.Yuv420P8) { chroma.CopyTo(writable.Memory.Span[planes[1].Offset..]); }
            else
            {
                Span<ushort> samples = MemoryMarshal.Cast<byte, ushort>(writable.Memory.Span[planes[1].Offset..]);
                for (int i = 0; i < chroma.Length; i++) { samples[i] = (ushort)(chroma[i] * 4); }
            }
            using MediaBuffer buffer = writable.Publish();
            return new(format, buffer, planes);
        }
    }
}
