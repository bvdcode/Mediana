// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class RgbaFrameTests
    {
        [Fact]
        public void ImportOwnsPixelsAndRetainedLease()
        {
            byte[] source = [10, 20, 30, 40, 50, 60, 70, 80];
            MediaBufferAllocator allocator = new(8);
            VideoColorInfo color = new(ColorPrimaries.DisplayP3, TransferFunction.Srgb,
                MatrixCoefficients.Rgb, ColorRange.Full);
            using VideoFrame original = RgbaFrame.CopyFrom(source, 2, 1, color, allocator);
            source[0] = 99;
            using VideoFrame retained = original.Retain();
            original.Dispose();
            Assert.Equal(10, retained.Data.Span[0]);
            Assert.Equal(color, retained.Format.Color);
            Assert.Null(retained.Format.SampleAspectRatio);
            Assert.Null(retained.Timestamp);
            Assert.Equal(8, allocator.AllocatedBytes);
            retained.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ImportValidatesLengthBudgetAndCancellation()
        {
            MediaBufferAllocator allocator = new(3);
            Assert.Throws<ArgumentException>(() => RgbaFrame.CopyFrom(new byte[3], 1, 1, new(), allocator));
            Assert.Throws<MediaMemoryLimitException>(() => RgbaFrame.CopyFrom(new byte[4], 1, 1, new(), allocator));
            Assert.Throws<OperationCanceledException>(() => RgbaFrame.CopyFrom(new byte[4], 1, 1, new(), allocator,
                cancellationToken: new CancellationToken(true)));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(1, ColorPrimaries.Bt709)]
        [InlineData(9, ColorPrimaries.Bt2020)]
        [InlineData(12, ColorPrimaries.DisplayP3)]
        [InlineData(2, ColorPrimaries.Unspecified)]
        [InlineData(255, ColorPrimaries.Unspecified)]
        public void CicpPrimariesPreserveInterpretation(int code, ColorPrimaries expected)
        {
            Assert.Equal(expected, CicpColorimetry.GetPrimaries(code));
        }

        [Theory]
        [InlineData(13, TransferFunction.Srgb)]
        [InlineData(16, TransferFunction.Pq)]
        [InlineData(18, TransferFunction.Hlg)]
        [InlineData(2, TransferFunction.Unspecified)]
        public void CicpTransferPreservesInterpretation(int code, TransferFunction expected)
        {
            Assert.Equal(expected, CicpColorimetry.GetTransfer(code));
        }
    }
}
