// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class JpegReducedTests
    {
        [Fact]
        public Task SequentialAndProgressiveReducedPixelsMatchIndependentReferences() =>
            JpegReducedTestInput.VerifyReferencesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg"));

        [Fact]
        public void SampleStorageScalesWithResolution() => JpegReducedTestInput.VerifyBudgets();

        [Fact]
        public Task AllSamplingModesHandleOddReducedDimensions() =>
            JpegReducedTestInput.VerifySamplingAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg"));

        [Fact]
        public Task EncodedLimitsCoefficientsAndFailedReadsRemainBounded() =>
            JpegReducedTestInput.VerifyFailuresAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg"));
    }
}
