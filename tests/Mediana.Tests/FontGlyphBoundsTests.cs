// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    [Collection("ImageAllocation")]
    public class FontGlyphBoundsTests
    {
        [Fact]
        public void TruncatedGlyphsCannotUseAdjacentRecords() => FontGlyphTestInput.VerifyTruncatedGlyphs();

        [Fact]
        public void MissingContoursAreRejectedBeforeAllocation() => FontGlyphTestInput.VerifyRejectedAllocation();

        [Fact]
        public void CompositeOutlinesPreserveCoordinatesAndIndependentCopies() => FontGlyphTestInput.VerifyValidOutlines();

        [Fact]
        public void EmptyGlyphHeadersHaveOptionalInstructionData() => FontGlyphTestInput.VerifyEmptyOutlines();

        [Fact]
        public void InstructionsCanBeDeclaredBeforeTheLastComponent() => FontGlyphTestInput.VerifyCompositeInstructions();
    }
}
