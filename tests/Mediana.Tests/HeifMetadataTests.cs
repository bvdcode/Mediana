// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Images.Heif;

namespace Mediana.Tests
{
    public class HeifMetadataTests
    {
        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data", "Heif");

        [Fact]
        public async Task MetadataIsIndependentOfPixelDecodingLimits() => await HeifMetadataTestInput.VerifyAsync(Root);

        [Theory]
        [InlineData(0u, 1u)]
        [InlineData(1u, 0u)]
        [InlineData(2147483648u, 1u)]
        [InlineData(1u, 2147483648u)]
        public async Task InvalidOrUnrepresentableDimensionsRemainErrors(uint width, uint height)
        {
            byte[] input = await HeifMetadataTestInput.CreateAsync(Root, width, height);
            HeicImageDecoder decoder = new(SampleAspectRatio.Square);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await decoder.ReadInfoAsync(input));
        }

        [Fact]
        public async Task MetadataStillEnforcesInputAndItemLimitsAndCancellation()
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(Root, "grid.heic"));
            HeicImageDecoder inputLimited = new(SampleAspectRatio.Square, new(maxInputBytes: input.Length - 1));
            HeicImageDecoder itemLimited = new(SampleAspectRatio.Square, new(maxItems: 1));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await inputLimited.ReadInfoAsync(input));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await itemLimited.ReadInfoAsync(input));
            HeicImageDecoder decoder = new(SampleAspectRatio.Square);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await decoder.ReadInfoAsync(input, new(true)));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await decoder.ReadInfoAsync(input.AsMemory(0, input.Length - 1)));
        }

        [Fact]
        public async Task DecodeAdmissionStillIncludesTheExactPixelBoundary()
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(Root, "basic-8bit.heic"));
            const int pixels = 64 * 48;
            HeicImageDecoder admitted = new(SampleAspectRatio.Square, new(maxPixels: pixels));
            HeicImageDecoder rejected = new(SampleAspectRatio.Square, new(maxPixels: pixels - 1));
            MediaBufferAllocator allocator = new(pixels * 4);
            using (await admitted.DecodeAsync(input, new(), allocator))
            {
                Assert.Equal(pixels * 4, allocator.AllocatedBytes);
            }
            await Assert.ThrowsAsync<InvalidDataException>(async () => await rejected.DecodeAsync(input, new(), allocator));
            Assert.Throws<InvalidDataException>(() => HeicDocument.Load(input, new(maxPixels: pixels - 1)));
            using MemoryStream stream = new(input);
            await Assert.ThrowsAsync<InvalidDataException>(() => HeicDocument.LoadAsync(stream, new(maxPixels: pixels - 1)));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
