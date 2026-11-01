// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;

namespace Mediana.Tests
{
    public class JpegImageDecoderTests
    {
        [Fact]
        public Task CompleteSequentialAndProgressiveCorpusMatchesIndependentPixels() =>
            JpegDecodeTestInput.VerifyCorpusAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg"));

        [Fact]
        public Task TruncationTablesPaddingRestartsAndUnsupportedPresentationAreRejected() =>
            JpegDecodeTestInput.VerifyIntegrityAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg"));

        [Fact]
        public Task ExactWorkingBudgetsMetadataAndCancellationArePreserved() => JpegDecodeTestInput.VerifyResourcesAsync();

        [Fact]
        public Task ProgressiveCoefficientBandsRefineInOrderAndQuantizationRemainsStable() => JpegProgressiveTestInput.VerifyOrderingAsync();

        [Fact]
        public Task ProgressiveEntropyRunsRestartsPaddingAndTruncationAreValidated() =>
            JpegProgressiveTestInput.VerifyEntropyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg"));

        [Fact]
        public Task ProgressiveCoefficientAndSampleStorageObeyExactBudgets() => JpegProgressiveTestInput.VerifyResourcesAsync();

        [Fact]
        public Task RealJpegCoversDecodeAfterContainerDisposalWithoutAnotherEncodedInputCopy() =>
            JpegCoverTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));

        [Fact]
        public Task UnknownColorAndConflictingExplicitInterpretationsAreNotGuessed() =>
            JpegDecodeTestInput.VerifyInterpretationsAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg"));

        [Fact]
        public void InvalidLimitsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new JpegImageDecoder(new() { MaximumEncodedBytes = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new JpegImageDecoder(new() { MaximumPixels = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new JpegImageDecoder(new() { MaximumMarkers = 0 }));
        }
    }
}
