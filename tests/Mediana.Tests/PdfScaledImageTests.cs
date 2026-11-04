// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class PdfScaledImageTests
    {
        [Fact]
        public Task PreviewReducesSamplesAndPreservesSourceAndWorkingLimits() => PdfScaledImageTestInput.VerifyBudgetsAsync(Root);
        [Fact]
        public Task LaterLargerPlacementsRequireTheirOwnResolution() => PdfScaledImageTestInput.VerifyMultiplePlacementsAsync(Root);
        [Fact]
        public Task PageOwnsItsImagesAndExportedFrameSurvivesDisposal() => PdfScaledImageTestInput.VerifyOwnershipAsync();
        [Fact]
        public Task LargeEncodedScanRendersWithinThreeMiB() => PdfScaledImageTestInput.VerifyLargeScanAsync();
        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg");
    }
}
