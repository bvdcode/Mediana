// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class HdrPreviewTests
    {
        [Theory]
        [InlineData(TransferFunction.Pq, "pq-hable.rgba")]
        [InlineData(TransferFunction.Hlg, "hlg-hable.rgba")]
        public void FullPrecisionGrayAndColorRampsMatchIndependentToneMapping(TransferFunction transfer, string reference)
        {
            MediaBufferAllocator allocator = new(64 * 1024);
            using (VideoFrame input = Ramp(allocator, transfer))
            using (VideoFrame output = SrgbConverter.ApplyWithColorPolicy(input, null, new HdrToneMappingOptions(), allocator))
            {
                byte[] expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "Preview", reference));
                byte[] actual = output.Data.ToArray();
                Assert.Equal(expected.Length, actual.Length);
                for (int index = 0; index < expected.Length; index++)
                { Assert.True(Math.Abs(expected[index] - actual[index]) <= 1, $"{reference} byte {index}: expected {expected[index]}, actual {actual[index]}."); }
                Assert.Equal(VideoColorInfo.Srgb, output.Format.Color);
                Assert.Equal(transfer, input.Format.Color.Transfer);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(TransferFunction.Pq)]
        [InlineData(TransferFunction.Hlg)]
        public void RotatedPreviewMatchesSeparateConversionAndAllocatesOnlyTheOutput(TransferFunction transfer)
        {
            MediaBufferAllocator sourceAllocator = new(16384);
            using VideoFrame input = Ramp(sourceAllocator, transfer);
            VideoDisplayMatrix rotation = new(0, -1, 0, 1, 0, 0, 0, 0, 1);
            MediaBufferAllocator referenceAllocator = new(16384);
            using VideoFrame converted = SrgbConverter.ApplyWithColorPolicy(input, null, new HdrToneMappingOptions(), referenceAllocator);
            using VideoFrame expected = DisplayResize.Fit(converted, rotation, 4, 64, referenceAllocator);
            MediaBufferAllocator allocator = new(2 * 64 * 4);
            using VideoFrame actual = SrgbPreview.FitWithColorPolicy(input, null, new HdrToneMappingOptions(), rotation, 4, 64, allocator);
            Assert.Equal(expected.Data.ToArray(), actual.Data.ToArray());
            Assert.Equal(allocator.LimitBytes, allocator.AllocatedBytes);
            input.Dispose();
            Assert.Equal(0, sourceAllocator.AllocatedBytes);
            Assert.Equal(expected.Data.ToArray(), actual.Data.ToArray());
            actual.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(TransferFunction.Pq)]
        [InlineData(TransferFunction.Hlg)]
        public async Task ExportRequiresExplicitToneMappingAndReportsItsDisplayInterpretation(TransferFunction transfer)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (VideoFrame input = Ramp(allocator, transfer))
            using (MemoryStream output = new())
            {
                await Assert.ThrowsAsync<NotSupportedException>(() => MediaEngine.ExportImageAsync(input, output, new PngImageEncoder(),
                    allocator, VideoDisplayMatrix.Identity));
                Assert.Equal(0, output.Length);
                List<string> warnings = [];
                await MediaEngine.ExportImageAsync(input, output, new PngImageEncoder(), allocator, VideoDisplayMatrix.Identity,
                    options: new() { ToneMapping = new(), WarningHandler = warnings.Add });
                string warning = Assert.Single(warnings);
                Assert.Contains("Hable", warning, StringComparison.Ordinal);
                Assert.Contains(transfer.ToString(), warning, StringComparison.Ordinal);
                using VideoFrame decoded = await new PngImageDecoder().DecodeAsync(output.ToArray(), new(), allocator);
                using VideoFrame expected = SrgbConverter.ApplyWithColorPolicy(input, null, new HdrToneMappingOptions(), allocator);
                Assert.Equal(expected.Data.ToArray(), decoded.Data.ToArray());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(double.NaN, 100)]
        [InlineData(double.PositiveInfinity, 100)]
        [InlineData(0, 100)]
        [InlineData(10001, 100)]
        [InlineData(1000, -1)]
        [InlineData(1000, double.NaN)]
        [InlineData(1000, 1001)]
        [InlineData(1000, double.Epsilon)]
        public void InvalidDisplayLuminanceDoesNotAllocate(double source, double target)
        {
            MediaBufferAllocator allocator = new(16384);
            using VideoFrame input = Ramp(allocator, TransferFunction.Pq);
            long before = allocator.AllocatedBytes;
            Assert.Throws<ArgumentOutOfRangeException>(() => SrgbConverter.ApplyWithColorPolicy(input, null,
                new HdrToneMappingOptions { SourcePeakLuminance = source, TargetPeakLuminance = target }, allocator));
            Assert.Equal(before, allocator.AllocatedBytes);
        }

        [Fact]
        public void MissingHdrLabelsUseExplicitPolicyAndPreserveDeclaredTransfer()
        {
            VideoColorInfo color = new(Transfer: TransferFunction.Pq);
            VideoFormat format = new(5, 3, VideoPixelFormat.Yuv420P10, color);
            List<string> warnings = [];
            VideoColorInfo resolved = VideoColorPolicy.Resolve(format, PreviewColorPolicy.Resolve, warnings.Add);
            Assert.Equal(new(ColorPrimaries.Bt2020, TransferFunction.Pq, MatrixCoefficients.Bt2020NonConstant,
                color.Range, ChromaLocation.Left), resolved);
            Assert.Single(warnings);
            Assert.Equal(color, format.Color);
        }

        [Fact]
        public void PqReferenceLuminancesAndHlgEndpointsFollowBt2100()
        {
            Assert.InRange(HdrTransfer.Sample(HdrTransfer.Pq, 0.508078421517399), 99.999, 100.002);
            Assert.InRange(HdrTransfer.Sample(HdrTransfer.Pq, 0.751827096247041), 999.99, 1000.01);
            Assert.Equal(0, HdrTransfer.Sample(HdrTransfer.Pq, 0));
            Assert.InRange(HdrTransfer.Sample(HdrTransfer.Pq, 1), 9999.999, 10000.001);
            Assert.Equal(0, HdrTransfer.Sample(HdrTransfer.Hlg, 0));
            Assert.Equal(1.0 / 12, HdrTransfer.Sample(HdrTransfer.Hlg, 0.5), 12);
            Assert.InRange(HdrTransfer.Sample(HdrTransfer.Hlg, 1), 0.999999, 1.000001);
        }

        [Fact]
        public async Task ExplicitToneMappingPreservesIccPrecedenceAndStraightAlpha()
        {
            MediaBufferAllocator allocator = new(64 * 1024);
            byte[] profile = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Icc", "linear-v2.icc"));
            using VideoFrame input = IccColorTestInput.CreateFrame(profile, [80, 60, 40, 127], 1, 1, allocator);
            using VideoFrame expected = SrgbConverter.Apply(input, allocator);
            using VideoFrame actual = SrgbConverter.ApplyWithColorPolicy(input,
                _ => throw new InvalidOperationException("An ICC profile must take precedence over the color resolver."), new HdrToneMappingOptions(), allocator);
            Assert.Equal(expected.Data.ToArray(), actual.Data.ToArray());
            Assert.Equal(127, actual.Data.Span[3]);
        }

        [Theory]
        [InlineData(TransferFunction.Pq)]
        [InlineData(TransferFunction.Hlg)]
        public void RgbaToneMappingPreservesAlphaCancellationAndOwnership(TransferFunction transfer)
        {
            MediaBufferAllocator allocator = new(32);
            using (WritableMediaBuffer writable = allocator.Allocate(8))
            {
                new byte[] { 0, 0, 0, 17, 255, 255, 255, 129 }.CopyTo(writable.Span);
                using MediaBuffer buffer = writable.Publish();
                VideoFormat format = new(2, 1, VideoPixelFormat.Rgba32,
                    new(ColorPrimaries.Bt2020, transfer, MatrixCoefficients.Rgb, ColorRange.Full), SampleAspectRatio.Square);
                using VideoFrame input = new(format, buffer, format.CreateTightLayout());
                Assert.Throws<OperationCanceledException>(() => SrgbConverter.ApplyWithColorPolicy(input, null,
                    new HdrToneMappingOptions(), allocator, new CancellationToken(true)));
                Assert.Equal(8, allocator.AllocatedBytes);
                using VideoFrame actual = SrgbConverter.ApplyWithColorPolicy(input, null, new HdrToneMappingOptions(), allocator);
                Assert.Equal(new byte[] { 0, 0, 0, 17, 255, 255, 255, 129 }, actual.Data.ToArray());
                input.Dispose(); buffer.Dispose();
                Assert.Equal(8, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static VideoFrame Ramp(MediaBufferAllocator allocator, TransferFunction transfer)
        {
            VideoColorInfo color = new(ColorPrimaries.Bt2020, transfer, MatrixCoefficients.Rgb, ColorRange.Full);
            VideoFormat format = new(256, 8, new PlanarVideoPixelFormat("gbr16", PlanarColorModel.Gbr, 16), color, SampleAspectRatio.Square);
            PlaneLayout[] planes = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)planes[^1].EndOffset));
            Span<ushort> data = MemoryMarshal.Cast<byte, ushort>(writable.Span);
            for (int plane = 0; plane < 3; plane++)
            {
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 256; x++)
                    {
                        (int red, int green, int blue) = y switch
                        {
                            0 => (x * 257, x * 257, x * 257),
                            1 => (x * 257, 0, 0),
                            2 => (0, x * 257, 0),
                            3 => (0, 0, x * 257),
                            4 => (x * 257, 65535 - x * 257, 16384),
                            5 => (32768, x * 257, 65535 - x * 257),
                            6 => (65535 - x * 257, 16384, x * 257),
                            7 => (x * 257, x * 193, x * 129),
                            _ => throw new ArgumentOutOfRangeException(nameof(y))
                        };
                        int value = plane switch { 0 => green, 1 => blue, 2 => red, _ => throw new ArgumentOutOfRangeException(nameof(plane)) };
                        data[(plane * 8 + y) * 256 + x] = (ushort)value;
                    }
                }
            }
            using MediaBuffer buffer = writable.Publish();
            return new(format, buffer, planes);
        }
    }
}
