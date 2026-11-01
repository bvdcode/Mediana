// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    [Collection("ImageAllocation")]
    public class PdfStreamOwnershipTests
    {
        [Fact]
        public async Task FilteredSamplesAndDetachedPixelsSurviveRepeatedParsing() =>
            await PdfStreamOwnershipTestInput.VerifyAsync();

        [Fact]
        public async Task CachedStreamsDoNotDuplicateCompleteRgbPayloads() =>
            await PdfStreamOwnershipTestInput.VerifyAllocationAsync();
    }
}
