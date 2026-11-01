// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Png;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class PngImageDecoderTests
    {
        [Fact]
        public Task AllColorDepthFilterAndAdam7CombinationsMatchIndependentCompletePixels() =>
            PngDecodeTestInput.VerifyCorpusAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Png"));

        [Fact]
        public Task InvalidChunksPaletteDataAndEveryTruncatedZlibTrailerAreRejectedWithoutLeaks() =>
            PngDecodeTestInput.VerifyIntegrityAsync();

        [Fact]
        public Task ExactWorkingBudgetsCancellationColorPriorityAndOwnershipArePreserved() =>
            PngDecodeTestInput.VerifyBudgetsAndColorAsync();

        [Fact]
        public async Task UnresolvedColorCannotBeSilentlyInterpretedAsSrgb()
        {
            byte[] encoded = PngDecodeTestData.Image([0, 1, 2, 3, 4], PngDecodeTestData.Chunk("gAMA", [0, 0, 177, 143]));
            MediaBufferAllocator allocator = new(64);
            using Core.Video.VideoFrame frame = await new PngImageDecoder().DecodeAsync(encoded, new(), allocator);
            Assert.Throws<NotSupportedException>(() => frame.Format.GetColorInterpretation());
        }

        [Theory]
        [InlineData(0, 8, 100)]
        [InlineData(1, 0, 100)]
        [InlineData(1, 8, 0)]
        public void NonpositiveLimitsAreRejected(int bytes, int pixels, int chunks) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => new PngImageDecoder(new()
            {
                MaximumEncodedBytes = bytes,
                MaximumPixels = pixels,
                MaximumChunks = chunks
            }));

        [Fact]
        public async Task ImageRequestsCannotSelectAnAnimationTimeOrDocumentPage()
        {
            PngImageDecoder decoder = new();
            MediaBufferAllocator allocator = new(64);
            await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await decoder.DecodeAsync(ReadOnlyMemory<byte>.Empty, new ImageDecodeRequest { PageIndex = 1 }, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
