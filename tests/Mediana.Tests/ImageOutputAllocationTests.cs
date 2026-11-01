// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    [Collection("ImageAllocation")]
    public class ImageOutputAllocationTests
    {
        [Fact]
        public Task PngStreamOutputDoesNotAllocateACompleteIntermediateImage() => PngStreamingTestInput.VerifyAllocationAsync();

        [Fact]
        public async Task RegisteredHeicDecodingDoesNotCloneTheEncodedFile() =>
            await ImageOutputAllocationTestInput.VerifyHeicInputAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Heif", "basic-8bit.heic"));

        [Fact]
        public async Task VectorAndPageDecodingAllocatesOneRgbaOutputWithinItsBudget() =>
            await ImageOutputAllocationTestInput.VerifyAsync();
    }
}
