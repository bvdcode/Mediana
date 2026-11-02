// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class PdfJpegTests
    {
        [Fact]
        public Task OriginalImageAndDetachedPagePixelsMatchIndependentDecoders() => PdfJpegTestInput.VerifyCorpusAsync(Path.Combine(AppContext.BaseDirectory, "Data", "PdfJpeg"));
        [Fact]
        public Task DictionaryPayloadAndTransformValidationRejectMalformedImages() => PdfJpegTestInput.VerifyInvalidAsync();
        [Fact]
        public Task ExactCombinedImageWorkspaceBudgetAndReusedImagesArePreserved() => PdfJpegTestInput.VerifyLimitsAndReuseAsync();
        [Fact]
        public Task PdfJpegDoesNotAllocateASecondCompleteRgbaImage() => PdfJpegTestInput.VerifyAllocationAsync(Path.Combine(AppContext.BaseDirectory, "Data", "PdfJpeg"));
        [Fact]
        public Task IndirectColorKeyAndExplicitImageMasksApplyBeforeDecodeMapping() => PdfJpegTestInput.VerifyMaskReferencesAsync();
    }
}
