// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class JpegLosslessTests
    {
        [Theory]
        [InlineData(1, 16, 0, false)]
        [InlineData(2, 12, 2, false)]
        [InlineData(3, 8, 0, false)]
        [InlineData(4, 16, 0, false)]
        [InlineData(5, 8, 1, false)]
        [InlineData(6, 10, 0, false)]
        [InlineData(7, 2, 0, false)]
        [InlineData(4, 8, 0, true)]
        public async Task ReconstructsLibjpegSamplesAcrossRestarts(int predictor, int precision, int shift, bool rgb)
        {
            byte[] jpeg = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg", "Lossless",
                $"{(rgb ? "rgb" : "gray")}-p{predictor}-{precision}-{shift}.jpg"));
            using VideoFrame frame = await new JpegImageDecoder().DecodeAsync(jpeg, new(), new(4096));
            Assert.Equal((17, 7), (frame.Format.Width, frame.Format.Height));
            int maximum = (1 << precision) - 1;
            for (int y = 0; y < 7; y++)
            {
                for (int x = 0; x < 17; x++)
                {
                    int offset = (y * 17 + x) * 4;
                    for (int c = 0; c < 3; c++)
                    {
                        int sample = (x * 1009 + y * 1901 + x * y * 503 + (rgb ? c * 331 : 0)) & maximum;
                        int restored = (sample >> shift) << shift;
                        Assert.Equal((byte)((restored * 255 + maximum / 2) / maximum), frame.Data.Span[offset + c]);
                    }
                    Assert.Equal(byte.MaxValue, frame.Data.Span[offset + 3]);
                }
            }
        }

        [Fact]
        public async Task SubsampledScanMaintainsIndependentPredictorLines()
        {
            byte[] jpeg = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data/Jpeg/Lossless/ycbcr-420.jpg"));
            using VideoFrame frame = await new JpegImageDecoder(sourceColorSpace: JpegColorSpace.YCbCr).DecodeAsync(jpeg, new(), new(4096));
            for (int y = 0; y < 14; y++)
            {
                for (int x = 0; x < 18; x++)
                {
                    byte luma = (byte)((x * 11 + y * 17 + x * y * 3) & 255);
                    Assert.True(frame.Data.Span.Slice((y * 18 + x) * 4, 4).SequenceEqual([luma, luma, luma, byte.MaxValue]));
                }
            }
        }

        [Fact]
        public async Task SeparateColorScansMatchInterleavedOutput()
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "Data/Jpeg/Lossless");
            byte[] separate = await File.ReadAllBytesAsync(Path.Combine(directory, "rgb-separate.jpg"));
            byte[] interleaved = await File.ReadAllBytesAsync(Path.Combine(directory, "rgb-p4-8-0.jpg"));
            JpegImageDecoder decoder = new();
            using VideoFrame expected = await decoder.DecodeAsync(interleaved, new(), new(4096));
            using VideoFrame actual = await decoder.DecodeAsync(separate, new(), new(4096));
            Assert.True(expected.Data.Span.SequenceEqual(actual.Data.Span));
        }

        [Theory]
        [InlineData(218, 6, 1)]
        [InlineData(218, 7, 0)]
        [InlineData(218, 9, 16)]
        [InlineData(195, 12, 1)]
        [InlineData(208, 1, 213)]
        public async Task RejectsMalformedPredictiveStreamsAndReleasesBuffers(byte marker, int offset, byte value)
        {
            byte[] jpeg = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data/Jpeg/Lossless/gray-p1-16-0.jpg"));
            jpeg[JpegTestData.Find(jpeg, marker) + offset] = value;
            MediaBufferAllocator allocator = new(4096);
            Assert.Throws<InvalidDataException>(() => new JpegImageDecoder().Decode(jpeg, new(), allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task PredictorRowsRespectCallerBudget()
        {
            byte[] jpeg = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data/Jpeg/Lossless/gray-p1-16-0.jpg"));
            MediaBufferAllocator allocator = new(17 * 7);
            Assert.Throws<MediaMemoryLimitException>(() => new JpegImageDecoder().Decode(jpeg, new(), allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
