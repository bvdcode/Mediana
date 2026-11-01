// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class PngStreamingTests
    {
        [Theory]
        [InlineData(1, 1, false)]
        [InlineData(1, 20000, true)]
        [InlineData(17000, 2, true)]
        [InlineData(1024, 64, true)]
        [InlineData(512, 512, true)]
        [InlineData(2560, 2560, false)]
        public Task BoundedAsyncOnlyNonSeekableOutputPreservesEveryChunkAndPixel(int width, int height, bool noisy)
            => PngStreamingTestInput.VerifyRoundtripAsync(width, height, noisy);

        [Fact]
        public Task PendingWriteOwnsPixelsUntilCompletion() => PngStreamingTestInput.VerifyPendingLifetimeAsync();

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public Task FailureAndCancellationDoNotContinueOutputOrMaskTheError(bool cancel)
            => PngStreamingTestInput.VerifyFailuresAsync(cancel);

        [Fact]
        public Task InsufficientWorkingBudgetIsRejectedBeforeWriting() => PngStreamingTestInput.VerifyEarlyRejectionAsync();
    }
}
