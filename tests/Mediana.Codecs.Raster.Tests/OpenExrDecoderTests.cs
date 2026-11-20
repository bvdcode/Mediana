// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Buffers;
using Mediana.Codecs.OpenExr;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Codecs.Raster.Tests
{
    public class OpenExrDecoderTests
    {
        [Theory]
        [InlineData("half", "none")]
        [InlineData("half", "rle")]
        [InlineData("half", "zip1")]
        [InlineData("half", "zip16")]
        [InlineData("float", "none")]
        [InlineData("float", "rle")]
        [InlineData("float", "zip1")]
        [InlineData("float", "zip16")]
        public async Task ScanlineSamplesMatchIndependentFloatDecodeIncludingAlphaAndExtendedRange(string precision, string compression)
        {
            byte[] input = await LoadAsync($"exr-{precision}-{compression}.exr");
            byte[] expected = await LoadAsync($"exr-{precision}.rgba-f32");
            OpenExrImageDecoder decoder = new();
            MediaBufferAllocator allocator = new(100_000);
            ImageInfo info = await decoder.ReadInfoAsync(input);
            Assert.Equal(17, info.Width); Assert.Equal(19, info.Height); Assert.Equal(1, info.PageCount);
            using (VideoFrame frame = await decoder.DecodeAsync(input, new(), allocator))
            {
                Assert.Equal(VideoPixelFormat.RgbaFloat32Premultiplied, frame.Format.PixelFormat);
                Assert.Equal(expected, frame.Data.ToArray());
                using VideoFrame srgb = SrgbConverter.Apply(frame, allocator);
                ReadOnlySpan<float> source = MemoryMarshal.Cast<byte, float>(frame.Data.Span);
                for (int i = 0; i < source.Length; i += 4)
                {
                    double alpha = source[i + 3];
                    for (int component = 0; component < 3; component++)
                    {
                        double value = alpha > 0 ? Math.Clamp(source[i + component] / alpha, 0, 1) : 0;
                        double encoded = value <= 0.0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055;
                        Assert.Equal((byte)Math.Floor(encoded * 255 + 0.5), srgb.Data.Span[i + component]);
                    }
                    Assert.Equal((byte)Math.Floor(alpha * 255 + 0.5), srgb.Data.Span[i + 3]);
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task InvalidChunksAndBudgetFailureReleaseEveryLease()
        {
            byte[] input = await LoadAsync("exr-float-zip16.exr");
            OpenExrImageDecoder decoder = new();
            MediaBufferAllocator small = new(17 * 19 * 16);
            await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await decoder.DecodeAsync(input, new(), small));
            Assert.Equal(0, small.AllocatedBytes);
            MediaBufferAllocator allocator = new(100_000);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await decoder.DecodeAsync(input, new(), allocator, new(true)));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await decoder.DecodeAsync(input, new() { MaximumPixels = 1 }, allocator));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await decoder.DecodeAsync(input, new() { PageIndex = 1 }, allocator));
            for (int count = 1; count <= 8; count++)
            {
                await Assert.ThrowsAsync<InvalidDataException>(async () => await decoder.DecodeAsync(input.AsMemory(0, input.Length - count), new(), allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            int compression = input.AsSpan().IndexOf("compression\0compression\0"u8) + 28;
            Assert.InRange(compression, 1, input.Length - 1);
            input[compression] = 4;
            await Assert.ThrowsAsync<NotSupportedException>(async () => await decoder.DecodeAsync(input, new(), allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task MultipartSelectionPreservesMixedPrecisionLayersAndChromaticities()
        {
            byte[] file = await LoadAsync("exr-multipart.exr");
            OpenExrImageDecoder decoder = new(new() { Layer = "beauty" });
            ImageInfo info = await decoder.ReadInfoAsync(file);
            Assert.Equal(2, info.PageCount); Assert.Equal(7, info.Width); Assert.Equal(5, info.Height);
            MediaBufferAllocator allocator = new(100_000);
            using (VideoFrame first = await decoder.DecodeAsync(file, new(), allocator))
            {
                Assert.Equal(await LoadAsync("exr-multipart-0.rgba-f32"), first.Data.ToArray());
                Assert.Equal(new SampleAspectRatio(3, 2), first.Format.SampleAspectRatio);
                RgbChromaticitiesColorDescription description = Assert.IsType<RgbChromaticitiesColorDescription>(first.Format.ColorDescription);
                Assert.InRange(description.Chromaticities.RedX, .6799, .6801);
                using VideoFrame converted = SrgbConverter.Apply(first, allocator);
                Assert.Equal(VideoPixelFormat.Rgba32, converted.Format.PixelFormat);
            }
            using (VideoFrame second = await new OpenExrImageDecoder().DecodeAsync(file, new() { PageIndex = 1 }, allocator))
            { Assert.Equal(await LoadAsync("exr-multipart-1.rgba-f32"), second.Data.ToArray()); }
            await Assert.ThrowsAsync<NotSupportedException>(async () => await new OpenExrImageDecoder().DecodeAsync(file, new(), allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task IeeeSpecialValuesRemainInDecodedFramesAndFailExplicitDisplayConversion()
        {
            byte[] file = await LoadAsync("exr-nonfinite.exr");
            MediaBufferAllocator allocator = new(100_000);
            using (VideoFrame frame = await new OpenExrImageDecoder().DecodeAsync(file, new(), allocator))
            {
                ReadOnlySpan<float> pixels = MemoryMarshal.Cast<byte, float>(frame.Data.Span);
                Assert.True(float.IsPositiveInfinity(pixels[0])); Assert.True(float.IsNegativeInfinity(pixels[4]));
                Assert.True(float.IsNaN(pixels[8])); Assert.Equal(-.25f, pixels[12]); Assert.Equal(2, pixels[16]);
                long retained = allocator.AllocatedBytes;
                Assert.Throws<InvalidDataException>(() => SrgbConverter.Apply(frame, allocator));
                Assert.Equal(retained, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task SharedDecoderAndCallerPoolSupportParallelLifetimes()
        {
            byte[] file = await LoadAsync("exr-half-zip16.exr");
            byte[] expected = await LoadAsync("exr-half.rgba-f32");
            OpenExrImageDecoder decoder = new();
            MediaBufferAllocator allocator = new(200_000, MemoryPool<byte>.Shared);
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                for (int iteration = 0; iteration < 5; iteration++)
                {
                    using VideoFrame frame = await decoder.DecodeAsync(file, new(), allocator);
                    await Task.Yield();
                    Assert.Equal(expected, frame.Data.ToArray());
                }
            })));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static async Task<byte[]> LoadAsync(string name) => await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", name));

        [Fact]
        public async Task UnicodeLayersFollowEncodedChannelOrdering()
        {
            byte[] file = await LoadAsync("exr-unicode.exr");
            MediaBufferAllocator allocator = new(4096);
            using (VideoFrame frame = await new OpenExrImageDecoder(new() { Layer = "\U00010000" }).DecodeAsync(file, new(), allocator))
            {
                ReadOnlySpan<float> pixels = MemoryMarshal.Cast<byte, float>(frame.Data.Span);
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    Assert.Equal(.25f, pixels[i]); Assert.Equal(.5f, pixels[i + 1]);
                    Assert.Equal(1, pixels[i + 2]); Assert.Equal(.5f, pixels[i + 3]);
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(0x802)]
        [InlineData(0xa02)]
        public async Task ContradictoryLayoutFlagsAreRejected(uint flags)
        {
            byte[] file = await LoadAsync("exr-float-zip16.exr");
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), flags);
            MediaBufferAllocator allocator = new(100_000);
            await Assert.ThrowsAnyAsync<InvalidDataException>(async () => await new OpenExrImageDecoder().DecodeAsync(file, new(), allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task CorruptChunkCoordinateAndChecksumReleaseOutput()
        {
            byte[] file = await LoadAsync("exr-float-zip16.exr");
            ExrDocument document = new(file, default);
            int offset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(document.Parts[0].Offsets.Span));
            int size = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(offset + 4));
            Assert.InRange(size, 1, 17 * 16 * 16 - 1);
            MediaBufferAllocator allocator = new(100_000);
            foreach (int index in new[] { offset, offset + 8 + size - 1 })
            {
                byte[] damaged = (byte[])file.Clone();
                damaged[index] ^= 1;
                await Assert.ThrowsAsync<InvalidDataException>(async () => await new OpenExrImageDecoder().DecodeAsync(damaged, new(), allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }
    }
}
